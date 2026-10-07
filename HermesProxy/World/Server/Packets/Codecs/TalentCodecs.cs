using System;
using System.Collections.Generic;
using Framework.IO;

namespace HermesProxy.World.Server.Packets;

public static class LearnPreviewTalentsCodec
{
    // More than every talent of a class and its pet: a larger count is a corrupt packet.
    private const uint MaxTalents = 150;

    public static void Read(ref SpanPacketReader r, out LearnPreviewTalents packet)
    {
        uint count = r.ReadUInt32();
        int tabIndex = r.ReadInt32();
        var talents = new List<(uint TalentID, uint Rank)>((int)Math.Min(count, MaxTalents));
        for (uint i = 0; i < count && i < MaxTalents; i++)
        {
            uint talentId = r.ReadUInt32();
            uint rank = r.ReadUInt32();
            talents.Add((talentId, rank));
        }
        packet = new LearnPreviewTalents(tabIndex, talents);
    }
}
