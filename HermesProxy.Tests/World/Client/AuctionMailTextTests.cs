using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// AzerothCore's auction mail strings, rewritten to TrinityCore wotlk_classic's layout. The
/// sale-pending strings are from a ChromieCraft capture.
/// </summary>
public class AuctionMailTextTests
{
    private static WowGuid128 Player(ulong raw) => WowGuid128.Create(HighGuidType703.Player, raw);

    [Fact]
    public void Subject_GetsTheEmptyPetContextAndBonusFields()
    {
        var (subject, type) = AuctionMailText.Subject("34055:0:6:45685286:1");
        Assert.Equal("34055:0:6:45685286:1:0:0:0:0:0:0:0:", subject);
        Assert.Equal(6, type);
    }

    [Theory]
    [InlineData("Re: lunch")]
    [InlineData("34055:0:6:45685286")]
    [InlineData("34055:0:x:45685286:1")]
    public void Subject_NotInTheAuctionLayout_IsLeftAlone(string subject)
        => Assert.Equal((subject, (int?)null), AuctionMailText.Subject(subject));

    [Fact]
    public void SalePending_IsAnInvoiceWithAPlayerGuidString()
        => Assert.Equal("Player-1-00097251:39598:39598:10:593:150:445805835:0",
            AuctionMailText.Body("            97251:39598:39598:10:593:150:445805835", 6, Player));

    [Fact]
    public void Won_KeepsGuidBidAndBuyout()
        => Assert.Equal("Player-1-0007C57D:1000:2000:0",
            AuctionMailText.Body("           7c57d:1000:2000:0:0:0:0", 1, Player));

    [Fact]
    public void Sold_KeepsDepositAndCut()
        => Assert.Equal("Player-1-0007C57D:1000:2000:30:50:0",
            AuctionMailText.Body("           7c57d:1000:2000:30:50:0:0", 2, Player));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OutbidExpiredRemovedCancelled_HaveNoBody(int type)
        => Assert.Equal("", AuctionMailText.Body("           7c57d:1000:2000:30:50:0:0", type, Player));

    [Fact]
    public void AnEmptyGuid_IsSixteenZeros()
        => Assert.Equal("0000000000000000:0:2000:30:0:0", AuctionMailText.Body("               0:0:2000:30:0:0:0", 2, Player));

    [Fact]
    public void ABodyNotInTheLayout_IsLeftAlone()
        => Assert.Equal("hello:world", AuctionMailText.Body("hello:world", 6, Player));
}
