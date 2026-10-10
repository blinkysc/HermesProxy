using System;
using System.Linq;
using Framework.IO;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The full auction-house scan (CMSG_AUCTION_REPLICATE_ITEMS), against TrinityCore's
/// AuctionHouseObject::BuildReplicate and AuctionReplicateItems::Read.
/// </summary>
public class AuctionReplicateTests
{
    private static AuctionReplicate House(long now, params uint[] auctionIds)
        => new(auctionIds.Select(id => new AuctionItem { AuctionID = id }), now, 1500);

    private static uint[] Ids(AuctionReplicateResponse page) => [.. page.Items.Select(i => i.AuctionID)];

    [Fact]
    public void Pages_InAuctionIdOrder_UntilAnEmptyPageEndsTheScan()
    {
        var house = House(0, 20, 3, 12, 7, 9);

        var first = house.Page(0, 2);
        Assert.Equal([3u, 7u], Ids(first));
        Assert.Equal((7u, 20u), (first.ChangeNumberCursor, first.ChangeNumberTombstone));
        Assert.NotEqual(0u, first.ChangeNumberGlobal);
        Assert.Equal(1500u, first.DesiredDelay);

        var second = house.Answer(first.ChangeNumberGlobal, 7, 20, 2, 0);
        Assert.Equal([9u, 12u], Ids(second));

        // A short page: the tombstone drops to 0 while the cursor still points at the last auction.
        var third = house.Answer(first.ChangeNumberGlobal, 12, 20, 2, 0);
        Assert.Equal([20u], Ids(third));
        Assert.Equal((20u, 0u), (third.ChangeNumberCursor, third.ChangeNumberTombstone));
        Assert.True(house.InProgress);

        var last = house.Answer(first.ChangeNumberGlobal, 20, 0, 2, 0);
        Assert.Empty(last.Items);
        Assert.Equal((0u, 0u), (last.ChangeNumberCursor, last.ChangeNumberTombstone));
        Assert.False(house.InProgress);
    }

    [Fact]
    public void AnExactlyFullLastPage_EndsTheScan()
    {
        var house = House(0, 3, 7);
        var page = house.Page(0, 2);
        Assert.Equal((7u, 7u), (page.ChangeNumberCursor, page.ChangeNumberTombstone));
        Assert.False(house.InProgress);
    }

    [Fact]
    public void ChangeNumbersOtherThanTheLastAnswered_GetAnEmptyAnswer()
    {
        var house = House(0, 3, 7, 9);
        var first = house.Page(0, 1);

        var wrong = house.Answer(first.ChangeNumberGlobal, 0, 0, 1, 0);
        Assert.Empty(wrong.Items);
        Assert.Equal(0u, wrong.ChangeNumberGlobal);
        // The scan itself is untouched.
        Assert.Equal([7u], Ids(house.Answer(first.ChangeNumberGlobal, 3, 9, 1, 0)));
    }

    [Fact]
    public void ANewScan_WaitsOutTheCooldown()
    {
        var house = House(0, 3, 7);
        var page = house.Page(0, 5);                                   // the whole house, a short page
        house.Answer(page.ChangeNumberGlobal, 7, 0, 5, 0);             // and the empty page that ends it
        Assert.False(house.InProgress);

        long justBefore = (long)AuctionReplicate.Cooldown.TotalMilliseconds - 1;
        Assert.False(house.Expired(justBefore));
        Assert.Empty(house.Answer(0, 0, 0, 5, justBefore).Items);
        Assert.True(house.Expired(justBefore + 1));
    }

    [Fact]
    public void AnEmptyHouseOrCountZero_AnswersNothing()
    {
        Assert.Empty(House(0).Page(0, 10).Items);
        var house = House(0, 3);
        Assert.Equal(0u, house.Page(0, 0).ChangeNumberGlobal);
        Assert.Equal([3u], Ids(house.Page(0, 1)));
    }

    // ---- CMSG_AUCTION_REPLICATE_ITEMS ----

    private static byte[] BuildRequest(bool tainted)
    {
        var payload = new WorldPacket(1u);
        payload.WritePackedGuid128(WowGuid128.Create(HighGuidType703.Creature, 0, 8719, 77));
        payload.WriteUInt32(11u); // ChangeNumberGlobal
        payload.WriteUInt32(22u); // ChangeNumberCursor
        payload.WriteUInt32(33u); // ChangeNumberTombstone
        payload.WriteUInt32(500u); // Count
        payload.WriteBit(tainted);
        payload.FlushBits();
        if (tainted)
        {
            const string name = "Auctionator", version = "10.3";
            payload.WriteBits(name.Length + 1, 10);
            payload.WriteBits(version.Length + 1, 10);
            payload.WriteBit(true);  // Loaded
            payload.WriteBit(false); // Disabled
            payload.FlushBits();
            payload.WriteString(name);
            payload.WriteUInt8(0);
            payload.WriteString(version);
            payload.WriteUInt8(0);
        }

        byte[] body = payload.GetData();
        var framed = new byte[body.Length + 2]; // the reader is built past the u16 opcode
        body.CopyTo(framed, 2);
        return framed;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Request_ReadsEveryFieldAndExactlyThePacket(bool tainted)
    {
        var r = new SpanPacketReader(new WorldPacket(BuildRequest(tainted)).GetRemainingSpan());

        AuctionReplicateItemsCodec.Read(ref r, out var request);

        Assert.Equal((11u, 22u, 33u, 500u),
            (request.ChangeNumberGlobal, request.ChangeNumberCursor, request.ChangeNumberTombstone, request.Count));
        Assert.Equal(0, r.Remaining);
    }

    // ---- the whole-house answer, end to end ----

    private static byte[] LegacyListResult(params uint[] auctionIds) => LegacyPacketBuilder.Build(Opcode.SMSG_AUCTION_LIST_ITEMS_RESULT, p =>
    {
        p.WriteUInt32((uint)auctionIds.Length);
        foreach (uint id in auctionIds)
        {
            p.WriteUInt32(id);
            p.WriteUInt32(2589); // Linen Cloth
            for (int slot = 0; slot < 7; slot++)
            {
                p.WriteUInt32(0); // enchant id
                p.WriteUInt32(0); // expiration
                p.WriteInt32(0);  // charges
            }
            p.WriteUInt32(0);    // random property
            p.WriteUInt32(0);    // suffix factor
            p.WriteInt32(20);    // count
            p.WriteInt32(0);     // charges
            p.WriteUInt32(0);    // flags
            p.WriteUInt64(0);    // owner
            p.WriteUInt32(100);  // min bid
            p.WriteUInt32(5);    // min increment
            p.WriteUInt32(200);  // buyout
            p.WriteInt32(3600000); // time left
            p.WriteUInt64(0);    // bidder
            p.WriteUInt32(0);    // bid
        }
        p.WriteInt32(auctionIds.Length); // total count
        p.WriteUInt32(300);              // search delay
    });

    [Fact]
    public void TheWholeHouseAnswer_IsTheScansFirstPage_AndASearchAfterItStaysASearch()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 509309));
        var lists = harness.Session.GameState.PendingAuctionLists;
        lists.Enqueue(new PendingAuctionReplicate(WowGuid128.Empty, 0, 2));
        lists.Enqueue(null); // a search sent after the scan's query

        harness.Deliver(Opcode.SMSG_AUCTION_LIST_ITEMS_RESULT, LegacyListResult(40, 10, 30), harness.Client.HandleAuctionListItemsResult);
        harness.Deliver(Opcode.SMSG_AUCTION_LIST_ITEMS_RESULT, LegacyListResult(10), harness.Client.HandleAuctionListItemsResult);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).ToList();
        var page = Assert.IsType<AuctionReplicateResponse>(sent[0]);
        Assert.Equal([10u, 30u], Ids(page));
        Assert.Equal((30u, 40u), (page.ChangeNumberCursor, page.ChangeNumberTombstone));
        Assert.Equal(1500u, page.DesiredDelay);
        Assert.Single(harness.Session.AuctionReplicates);

        var search = Assert.IsType<AuctionListItemsResult>(sent[1]);
        Assert.Single(search.Items);
        Assert.Empty(lists);
    }
}
