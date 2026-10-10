using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>The server's pending-sales answer reaches the client as its pending-sales result.</summary>
public class AuctionPendingSalesTests
{
    [Fact]
    public void TheServersEmptyList_IsAnEmptyResult()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_AUCTION_LIST_PENDING_SALES, p => p.WriteUInt32(0));

        harness.Deliver(Opcode.SMSG_AUCTION_LIST_PENDING_SALES, wire, harness.Client.HandleAuctionListPendingSales);

        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.IsType<AuctionListPendingSalesResult>(sent.Packet);
        Assert.Equal(new byte[8], sent.Bytes); // MailsCount 0, TotalNumRecords 0
    }
}
