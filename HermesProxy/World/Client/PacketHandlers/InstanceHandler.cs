using HermesProxy.Enums;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    // Handlers for SMSG opcodes coming the legacy world server

    /// <summary>
    /// The "you will be saved to this instance" confirmation. Legacy sends two uint32s and a byte
    /// (Map.cpp, perm-bound branch); V3_4_3 renamed the opcode to SMSG_PENDING_RAID_LOCK and packs
    /// the trailing flags as bits. Without this the warning never reached the client, so the
    /// client never sent CMSG_INSTANCE_LOCK_RESPONSE and the server sat on a pending bind.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_INSTANCE_LOCK_WARNING_QUERY)]
    internal void HandleInstanceLockWarningQuery(WorldPacket packet)
    {
        PendingRaidLock pending = new PendingRaidLock();
        pending.TimeUntilLock = (int)packet.ReadUInt32();
        pending.CompletedMask = packet.ReadUInt32();
        pending.Extending = packet.ReadUInt8() != 0;
        pending.WarningOnly = false; // legacy has no equivalent; it only asks when a bind is pending
        SendPacketToClient(pending);
    }

    [HandlesSmsg(Opcode.SMSG_UPDATE_INSTANCE_OWNERSHIP)]
    internal void HandleUpdateInstanceOwnership(WorldPacket packet)
    {
        UpdateInstanceOwnership instance = new UpdateInstanceOwnership();
        instance.IOwnInstance = packet.ReadUInt32();
        SendPacketToClient(instance);
    }

    [HandlesSmsg(Opcode.SMSG_UPDATE_LAST_INSTANCE)]
    internal void HandleUpdateLastInstance(WorldPacket packet)
    {
        UpdateLastInstance last = new();
        last.MapID = packet.ReadUInt32();
        SendPacketToClient(last);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_RESET)]
    internal void HandleInstanceReset(WorldPacket packet)
    {
        InstanceReset reset = new InstanceReset();
        reset.MapID = packet.ReadUInt32();
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_RESET_FAILED)]
    internal void HandleInstanceResetFailed(WorldPacket packet)
    {
        InstanceResetFailed reset = new InstanceResetFailed();
        reset.ResetFailedReason = (ResetFailedReason)packet.ReadUInt32();
        reset.MapID = packet.ReadUInt32();
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_RESET_FAILED_NOTIFY)]
    internal void HandleResetFailedNotify(WorldPacket packet)
    {
        ResetFailedNotify reset = new ResetFailedNotify();
        packet.ReadUInt32(); // Map ID
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_INSTANCE_INFO)]
    internal void HandleRaidInstanceInfo(WorldPacket packet)
    {
        RaidInstanceInfo infos = new RaidInstanceInfo();
        int count = packet.ReadInt32();
        for (var i = 0; i < count; ++i)
        {
            InstanceLock instance = new InstanceLock();
            instance.MapID = packet.ReadUInt32();

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                instance.DifficultyID = (DifficultyModern)GameData.GetModernMapDifficulty(instance.MapID, packet.ReadUInt32()).DifficultyId;
            else
            {
                if (ModernVersion.ExpansionVersion == 1)
                    instance.DifficultyID = DifficultyModern.Raid40;
                else
                    instance.DifficultyID = DifficultyModern.Raid25N;
            }

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            {
                instance.InstanceID = packet.ReadUInt64();
                instance.Locked = packet.ReadBool();
                instance.Extended = packet.ReadBool();
                instance.TimeRemaining = packet.ReadInt32();
            }
            else
            {
                instance.TimeRemaining = packet.ReadInt32();
                instance.InstanceID = packet.ReadUInt32();

                if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                    packet.ReadUInt32(); // Counter
            }
            infos.LockList.Add(instance);
        }
        SendPacketToClient(infos);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_SAVE_CREATED)]
    internal void HandleInstanceSaveCreated(WorldPacket packet)
    {
        InstanceSaveCreated save = new InstanceSaveCreated();
        save.Gm = packet.ReadUInt32() != 0;
        SendPacketToClient(save);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_GROUP_ONLY)]
    internal void HandleRaidGroupOnly(WorldPacket packet)
    {
        RaidGroupOnly save = new RaidGroupOnly();
        save.Delay = packet.ReadInt32();
        save.Reason = (RaidGroupReason)packet.ReadUInt32();
        SendPacketToClient(save);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_INSTANCE_MESSAGE)]
    internal void HandleRaidInstanceMessage(WorldPacket packet)
    {
        RaidInstanceMessage instance = new RaidInstanceMessage();
        instance.Type = (InstanceResetWarningType)packet.ReadUInt32();
        instance.MapID = packet.ReadUInt32();

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            instance.DifficultyID = (DifficultyModern)GameData.GetModernMapDifficulty(instance.MapID, packet.ReadUInt32()).DifficultyId;
        else
        {
            if (ModernVersion.ExpansionVersion == 1)
                instance.DifficultyID = DifficultyModern.Raid40;
            else
                instance.DifficultyID = DifficultyModern.Raid25N;
        }

        packet.ReadUInt32(); // time

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) &&
            instance.Type == InstanceResetWarningType.Welcome)
        {
            instance.Locked = packet.ReadBool();
            instance.Extended = packet.ReadBool();
        }

        SendPacketToClient(instance);
    }

    // The map's difficulty changed (entering an instance, or the group leader switching it).
    [HandlesSmsg(Opcode.SMSG_INSTANCE_DIFFICULTY)]
    internal void HandleInstanceDifficulty(WorldPacket packet)
    {
        uint difficulty = packet.ReadUInt32();
        packet.ReadUInt32(); // IsDynamic
        var state = GetSession().GameState;
        state.CurrentLegacyMapDifficulty = difficulty;
        if (state.CurrentMapId is uint mapId)
            SendWorldServerInfo(mapId);
    }

    /// <summary>
    /// SMSG_WORLD_SERVER_INFO with the map's modern difficulty, unless the client already has it.
    /// </summary>
    internal void SendWorldServerInfo(uint mapId)
    {
        var state = GetSession().GameState;
        var difficulty = GameData.GetModernMapDifficulty(mapId, state.CurrentLegacyMapDifficulty);
        if (state.SentWorldServerDifficulty == difficulty)
            return;
        state.SentWorldServerDifficulty = difficulty;

        WorldServerInfo info = new();
        (info.DifficultyID, info.InstanceGroupSize) = difficulty;
        SendPacketToClient(info);
    }

    // Boss frames: 3.3.5a sends engage, disengage and priority as one opcode with a type.
    [HandlesSmsg(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT)]
    internal void HandleUpdateInstanceEncounterUnit(WorldPacket packet)
    {
        Opcode opcode = packet.ReadUInt32() switch
        {
            0 => Opcode.SMSG_INSTANCE_ENCOUNTER_ENGAGE_UNIT,
            1 => Opcode.SMSG_INSTANCE_ENCOUNTER_DISENGAGE_UNIT,
            2 => Opcode.SMSG_INSTANCE_ENCOUNTER_CHANGE_PRIORITY,
            _ => Opcode.MSG_NULL_ACTION,
        };
        if (opcode == Opcode.MSG_NULL_ACTION || ModernVersion.Build != ClientVersionBuild.V3_4_3_54261)
            return;

        InstanceEncounterUnit unit = new(opcode);
        unit.Unit = packet.ReadPackedGuid().To128(GetSession().GameState);
        unit.TargetFramePriority = packet.ReadUInt8();
        SendPacketToClient(unit);
    }
}
