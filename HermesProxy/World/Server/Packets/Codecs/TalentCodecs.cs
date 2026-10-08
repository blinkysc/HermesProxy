using System;
using System.Collections.Generic;
using Framework.IO;

namespace HermesProxy.World.Server.Packets;

public static class LearnPreviewTalentsCodec
{
    // More than every talent of a class and its pet: a larger count is a corrupt packet.
    private const uint MaxTalents = 150;

    // V3_4_3: uint32 count, then { uint32 TalentID; uint8 Rank } per talent — no tab index.
    // Captured with one previewed point: 01-00-00-00 EB-03-00-00 00 (talent 1003, rank 0).
    public static void Read(ref SpanPacketReader r, out LearnPreviewTalents packet)
    {
        uint count = r.ReadUInt32();
        var talents = new List<(uint TalentID, uint Rank)>((int)Math.Min(count, MaxTalents));
        for (uint i = 0; i < count && i < MaxTalents; i++)
        {
            uint talentId = r.ReadUInt32();
            byte rank = r.ReadUInt8();
            talents.Add((talentId, rank));
        }
        packet = new LearnPreviewTalents(talents);
    }
}
