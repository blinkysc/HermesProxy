using Framework.IO;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Dispatch;

public class TalentCodecTests
{
    private static SpanPacketReader ReaderOver(byte[] body)
    {
        byte[] framed = new byte[body.Length + 2];
        body.CopyTo(framed, 2);
        return new SpanPacketReader(new WorldPacket(framed).GetRemainingSpan());
    }

    /// <summary>The body a 3.4.3 client sent for one previewed point in talent 1003.</summary>
    [Fact]
    public void LearnPreviewTalents_CapturedPacket_ReadsOneTalent()
    {
        var r = ReaderOver([0x01, 0x00, 0x00, 0x00, 0xEB, 0x03, 0x00, 0x00, 0x00]);

        LearnPreviewTalentsCodec.Read(ref r, out var packet);

        Assert.Equal([(1003u, 0u)], packet.Talents);
    }

    [Fact]
    public void LearnPreviewTalents_SeveralTalents_ReadsEachIdAndRank()
    {
        var r = ReaderOver([
            0x02, 0x00, 0x00, 0x00,
            0xEB, 0x03, 0x00, 0x00, 0x02,
            0xEC, 0x03, 0x00, 0x00, 0x04,
        ]);

        LearnPreviewTalentsCodec.Read(ref r, out var packet);

        Assert.Equal([(1003u, 2u), (1004u, 4u)], packet.Talents);
    }
}
