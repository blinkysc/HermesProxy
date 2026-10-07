using Framework.Constants;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using System.Collections.Generic;
using System;

namespace HermesProxy.World.Server.Packets;

// Modern V3_4_3 (build 54261) achievement packets, wire layout per TrinityCore
// 3.4.3 source: src/server/game/Server/Packets/AchievementPackets.{h,cpp} +
// PacketUtilities.h Duration<int64> / Timestamp<int64>.
// CriteriaProgressPkt is shared with AllAccountCriteria — defined in MiscPackets.cs.

public struct EarnedAchievement
{
    public uint Id;
    public long Date;                  // unix time; WritePackedTime packs to wire UInt32
    public WowGuid128 Owner;
    public uint VirtualRealmAddress;
    public uint NativeRealmAddress;

    public void Write(WorldPacket data)
    {
        data.WriteUInt32(Id);
        data.WritePackedTime(Date);
        data.WritePackedGuid128(Owner);
        data.WriteUInt32(VirtualRealmAddress);
        data.WriteUInt32(NativeRealmAddress);
    }
}

public class AllAchievementData : ServerPacket
{
    public List<EarnedAchievement> Earned = new();
    public List<CriteriaProgressPkt> Progress = new();

    public AllAchievementData() : base(Opcode.SMSG_ALL_ACHIEVEMENT_DATA, ConnectionType.Instance) { }

    public override void Write()
    {
        WriteData(_worldPacket);
    }

    public void WriteData(WorldPacket packet)
    {
        packet.WriteInt32(Earned.Count);
        packet.WriteInt32(Progress.Count);
        foreach (var earned in Earned)
            earned.Write(packet);
        foreach (var progress in Progress)
            progress.Write(packet);
    }
}

public class CriteriaUpdatePkt : ServerPacket
{
    public uint CriteriaID;
    public ulong Quantity;
    public WowGuid128 PlayerGUID;
    public uint Flags;
    public long CurrentTime;           // unix -> WritePackedTime
    public long ElapsedTime;           // Duration<Seconds> = Int64
    public long CreationTime;          // Timestamp<int64> = Int64 (fork wrote UInt32; TC source = 8 B)
    public ulong? RafAcceptanceID;

    public CriteriaUpdatePkt() : base(Opcode.SMSG_CRITERIA_UPDATE, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(CriteriaID);
        _worldPacket.WriteUInt64(Quantity);
        _worldPacket.WritePackedGuid128(PlayerGUID);
        _worldPacket.WriteUInt32(0u);  // Unused_10_1_5
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WritePackedTime(CurrentTime);
        _worldPacket.WriteInt64(ElapsedTime);
        _worldPacket.WriteInt64(CreationTime);
        _worldPacket.WriteBit(RafAcceptanceID.HasValue);
        _worldPacket.FlushBits();
        if (RafAcceptanceID.HasValue)
            _worldPacket.WriteUInt64(RafAcceptanceID.Value);
    }
}

public class CriteriaDeletedPkt : ServerPacket
{
    public CriteriaDeletedPkt() : base(Opcode.SMSG_CRITERIA_DELETED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(CriteriaID);
    }

    public uint CriteriaID;
}

public class AchievementEarnedPkt : ServerPacket
{
    public WowGuid128 Sender;
    public WowGuid128 Earner;
    public uint AchievementID;
    public long Time;                  // unix -> WritePackedTime
    public uint EarnerNativeRealm;
    public uint EarnerVirtualRealm;
    public bool Initial;

    public AchievementEarnedPkt() : base(Opcode.SMSG_ACHIEVEMENT_EARNED, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Sender);
        _worldPacket.WritePackedGuid128(Earner);
        _worldPacket.WriteUInt32(AchievementID);
        _worldPacket.WritePackedTime(Time);
        _worldPacket.WriteUInt32(EarnerNativeRealm);
        _worldPacket.WriteUInt32(EarnerVirtualRealm);
        _worldPacket.WriteBit(Initial);
        _worldPacket.FlushBits();
    }
}

public class BroadcastAchievement : ServerPacket
{
    public string Name = "";
    public bool GuildAchievement;
    public WowGuid128 PlayerGUID;
    public uint AchievementID;

    public BroadcastAchievement() : base(Opcode.SMSG_BROADCAST_ACHIEVEMENT) { }

    public override void Write()
    {
        _worldPacket.WriteBits(Name.GetByteCount(), 7);
        _worldPacket.WriteBit(GuildAchievement);
        _worldPacket.FlushBits();
        _worldPacket.WritePackedGuid128(PlayerGUID);
        _worldPacket.WriteUInt32(AchievementID);
        _worldPacket.WriteString(Name);
    }
}

public class RespondInspectAchievements : ServerPacket
{
    public WowGuid128 Player;
    public AllAchievementData Data = new AllAchievementData();

    public RespondInspectAchievements() : base(Opcode.SMSG_RESPOND_INSPECT_ACHIEVEMENTS) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Player);
        Data.WriteData(_worldPacket);
    }
}

public class TitleEarned : ServerPacket
{
    public uint Index;

    public TitleEarned(Opcode opcode) : base(opcode) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(Index);
    }
}
