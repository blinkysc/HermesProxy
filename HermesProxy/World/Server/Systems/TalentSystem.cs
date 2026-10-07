using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>Pet talents, glyph removal and learning the talent frame's preview.</summary>
public static class TalentSystem
{
    // Modern V3_4_3 CMSG_PET_LEARN_TALENT (0x3554 / 13652) → legacy CMSG_PET_LEARN_TALENT (0x47A).
    // Legacy payload (CMaNGOS PetHandler.cpp:845-855 HandlePetLearnTalent):
    //   ObjectGuid guid; uint32 talent_id; uint32 requested_rank;
    // Modern client sends a separate opcode from CMSG_LEARN_TALENT (which is player-only).
    // Pet GUID is translated modern→legacy via existing GameSessionData.GetLegacyPetGuid
    // (project_pet_guid_fix infrastructure).
    // "Learn" in the talent frame's preview mode: every previewed point at once.
    [HandlesCmsg(Opcode.CMSG_LEARN_PREVIEW_TALENTS)]
    public static void HandleLearnPreviewTalents(in LearnPreviewTalents preview, in SessionContext ctx)
    {
        if (!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            return;
        WorldPacket packet = new WorldPacket(Opcode.CMSG_LEARN_PREVIEW_TALENTS);
        packet.WriteUInt32((uint)preview.Talents.Count);
        foreach (var (talentId, rank) in preview.Talents)
        {
            packet.WriteUInt32(talentId);
            packet.WriteUInt32(rank);
        }
        ctx.SendPacketToServer(packet);
    }

    [HandlesCmsg(Opcode.CMSG_PET_LEARN_TALENT)]
    public static void HandleLearnPetTalent(in LearnPetTalent talent, in SessionContext ctx)
    {
        var legacyGuid = ctx.GetSession().GameState.GetLegacyPetGuid(talent.PetGUID);
        if (legacyGuid == null)
        {
            Log.Print(LogType.Warn, $"CMSG_PET_LEARN_TALENT: no legacy pet GUID for {talent.PetGUID} — dropping");
            return;
        }

        WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_LEARN_TALENT);
        packet.WriteGuid(legacyGuid.Value);
        packet.WriteUInt32(talent.TalentID);
        packet.WriteUInt32(talent.Rank);
        ctx.SendPacketToServer(packet);
    }

    // Modern V3_4_3 CMSG_REMOVE_GLYPH (0x32E0 / 13056) → legacy CMSG_REMOVE_GLYPH (0x48A).
    // Both share the same payload: uint8 GlyphSlot (0-5). The legacy server replies with
    // a fresh SMSG_UPDATE_TALENT_DATA, which TalentHandler.HandleTalentsInfoUpdate processes
    // and re-emits SMSG_ACTIVE_GLYPHS — the slot will appear empty in the UI on next refresh.
    [HandlesCmsg(Opcode.CMSG_REMOVE_GLYPH)]
    public static void HandleRemoveGlyph(in RemoveGlyph remove, in SessionContext ctx)
    {
        Log.Print(LogType.Network, $"CMSG_REMOVE_GLYPH: slot={remove.GlyphSlot} → forwarding to legacy");
        WorldPacket packet = new WorldPacket(Opcode.CMSG_REMOVE_GLYPH);
        packet.WriteUInt8(remove.GlyphSlot);
        ctx.SendPacketToServer(packet);
    }
}
