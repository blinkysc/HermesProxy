using System;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// Translation for the modern client's auction-house CMSGs.
/// </summary>
/// <remarks>
/// <see cref="HandleAuctionListItems"/> reads through a ranged codec pair rather than a branch: the
/// V3_4_3 and pre-V3_4_3 layouts disagree on field order, on field widths, and on whether a
/// tainted-addon block is present at all. See AuctionCodecs.cs.
/// <para>
/// <see cref="HandleAuctionSellItem"/> carries the awkward part of this domain. Servers before
/// 3.2.2a have no quantity field — they auction the whole item — so selling part of a stack means
/// splitting it to a temporary bag slot first and auctioning that. <see cref="PartialStackAuctionPost"/>
/// sequences the splits on the server's inventory updates. It used to sleep the dispatch thread,
/// which froze the client's other packets and would deadlock once one thread owns the session.
/// </para>
/// </remarks>
public static class AuctionSystem
{
    [HandlesCmsg(Opcode.CMSG_AUCTION_HELLO_REQUEST)]
    public static void HandleAuctionHelloRequest(in InteractWithNPC interact, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.MSG_AUCTION_HELLO);
        packet.WriteGuid(interact.CreatureGUID.To64());
        ctx.SendPacketToServer(packet);
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_LIST_OWNED_ITEMS)]
    public static void HandleAuctionListOwnerItems(in AuctionListOwnerItems auction, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_OWNED_ITEMS);
        packet.WriteGuid(auction.Auctioneer.To64());
        packet.WriteUInt32(auction.Offset);
        ctx.SendPacketToServer(packet);
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_LIST_BIDDED_ITEMS)]
    public static void HandleAuctionListBidderItems(in AuctionListBidderItems auction, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_BIDDED_ITEMS);
        packet.WriteGuid(auction.Auctioneer.To64());
        packet.WriteUInt32(auction.Offset);
        packet.WriteInt32(auction.AuctionItemIDs.Count);
        foreach (var itemId in auction.AuctionItemIDs)
            packet.WriteUInt32(itemId);
        ctx.SendPacketToServer(packet);
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_LIST_ITEMS)]
    public static void HandleAuctionListItems(in AuctionListItems auction, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_ITEMS);
        packet.WriteGuid(auction.Auctioneer.To64());
        packet.WriteUInt32(auction.Offset);
        packet.WriteCString(auction.Name);
        packet.WriteUInt8(auction.MinLevel);
        packet.WriteUInt8(auction.MaxLevel);

        if (auction.ClassFilters.Count > 0)
        {
            if (auction.ClassFilters[0].SubClassFilters.Count == 1)
            {
                packet.WriteInt32(ModernToLegacyInventorySlotType(auction.ClassFilters[0].SubClassFilters[0].InvTypeMask));
                packet.WriteInt32(auction.ClassFilters[0].ItemClass);
                packet.WriteInt32(auction.ClassFilters[0].SubClassFilters[0].ItemSubclass);
            }
            else
            {
                packet.WriteInt32(-1); // inventorySlotId
                packet.WriteInt32(auction.ClassFilters[0].ItemClass);
                packet.WriteInt32(-1); // auctionSubCategory
            }
        }
        else
        {
            packet.WriteInt32(-1); // inventorySlotId
            packet.WriteInt32(-1); // auctionMainCategory
            packet.WriteInt32(-1); // auctionSubCategory
        }

        packet.WriteInt32(auction.Quality);
        packet.WriteBool(auction.OnlyUsable);

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
        {
            // 3.3.5a reads this byte as getAll, the whole-house scan, not exact match: a client
            // exact-match search asked the server for every auction there is.
            packet.WriteBool(false);
            packet.WriteUInt8((byte)auction.Sorts.Count);

            foreach (var sort in auction.Sorts)
            {
                packet.WriteUInt8(sort.Type);
                packet.WriteUInt8(sort.Direction);
            }
        }

        ctx.GetSession().GameState.PendingAuctionLists.Enqueue(null);
        ctx.SendPacketToServer(packet);
    }

    /// <summary>
    /// A page request of a full scan (Auctionator's, or the default UI's). See <see cref="AuctionReplicate"/>.
    /// </summary>
    [HandlesCmsg(Opcode.CMSG_AUCTION_REPLICATE_ITEMS)]
    public static void HandleAuctionReplicateItems(in AuctionReplicateItems request, in SessionContext ctx)
    {
        var session = ctx.GetSession();
        var player = session.GameState.CurrentPlayerGuid;
        long now = Environment.TickCount64;

        if (session.AuctionReplicates.TryGetValue(player, out var replicate) && replicate.Expired(now))
        {
            session.AuctionReplicates.Remove(player);
            replicate = null;
        }

        if (replicate != null)
        {
            ctx.SendPacket(replicate.Answer(request.ChangeNumberGlobal, request.ChangeNumberCursor,
                request.ChangeNumberTombstone, request.Count, now));
            return;
        }

        // A new scan: ask for the whole house. The answer arrives as an ordinary list result
        // (AuctionHandler.HandleAuctionListItemsResult). The cooldown starts with it, not here: the
        // server answers nothing when the auctioneer is out of reach, and the next scan asks again.
        var pending = new PendingAuctionReplicate(request.Auctioneer, request.ChangeNumberCursor, request.Count);
        session.GameState.PendingAuctionLists.Enqueue(pending);
        ctx.SendPacketToServer(BuildListAllAuctions(request.Auctioneer));
    }

    /// <summary>The 3.3.5a whole-house query: CMSG_AUCTION_LIST_ITEMS with no filters and getAll set.</summary>
    static WorldPacket BuildListAllAuctions(WowGuid128 auctioneer)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_ITEMS);
        packet.WriteGuid(auctioneer.To64());
        packet.WriteUInt32(0);   // list from
        packet.WriteCString("");  // name
        packet.WriteUInt8(0);    // min level
        packet.WriteUInt8(0);    // max level
        packet.WriteInt32(-1);   // inventory slot
        packet.WriteInt32(-1);   // item class
        packet.WriteInt32(-1);   // item subclass
        packet.WriteInt32(-1);   // quality
        packet.WriteBool(false); // usable only
        packet.WriteBool(true);  // getAll
        packet.WriteUInt8(0);    // sort count
        return packet;
    }

    /// <remarks>
    /// The modern client can search several inventory types at once; legacy takes one, so this
    /// picks the lowest set bit. The pre-conversion handler carried this twice — once as a local
    /// function inside HandleAuctionListItems and once as an instance method that local shadowed
    /// and nothing else called. Only the reachable one survives.
    /// </remarks>
    static int ModernToLegacyInventorySlotType(uint modernInventoryFlag)
    {
        if (modernInventoryFlag == uint.MaxValue)
            return -1;

        for (int i = 0; i < 32; i++)
        {
            if ((modernInventoryFlag & (1 << i)) > 0)
            {
                return i;
            }
        }

        return -1;
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_SELL_ITEM)]
    public static void HandleAuctionSellItem(in AuctionSellItem auction, in SessionContext ctx)
    {
        uint expireTime = auction.ExpireTime;

        // auction durations were increased in tbc
        // server ignores packet if you send wrong duration
        if (LegacyVersion.ExpansionVersion <= 1 &&
            ModernVersion.ExpansionVersion > 1)
        {
            switch (expireTime)
            {
                case 1 * 12 * 60: // 720
                {
                    expireTime = 1 * 2 * 60; // 120
                    break;
                }
                case 2 * 12 * 60: // 1440
                {
                    expireTime = 4 * 2 * 60; // 480
                    break;
                }
                case 4 * 12 * 60: // 2880
                {
                    expireTime = 12 * 2 * 60; // 1440
                    break;
                }
            }
        }
        else if (LegacyVersion.ExpansionVersion > 1 &&
                 ModernVersion.ExpansionVersion <= 1)
        {
            switch (expireTime)
            {
                case 1 * 2 * 60:
                {
                    expireTime = 1 * 12 * 60;
                    break;
                }
                case 4 * 2 * 60:
                {
                    expireTime = 2 * 12 * 60;
                    break;
                }
                case 12 * 2 * 60:
                {
                    expireTime = 4 * 12 * 60;
                    break;
                }
            }
        }

        if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_2_2a_10505))
        {
            // Pre-3.2.2a servers have no quantity field — they auction the entire item.
            // A partial stack is split to a temp slot and that is auctioned instead; the
            // original item keeps the remainder, which is what the modern client expects.
            // Each split waits for the server's inventory update, so the post runs as a
            // sequence of outbox holds rather than sleeps on this thread.
            var session = ctx.GetSession();
            new PartialStackAuctionPost(
                session.ToServer,
                new SessionAuctionInventory(session),
                auction.Auctioneer, auction.MinBid, auction.BuyoutPrice, expireTime,
                [.. auction.Items]).Start();
        }
        else
        {
            WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_SELL_ITEM);
            packet.WriteGuid(auction.Auctioneer.To64());
            packet.WriteInt32(auction.Items.Count);
            foreach (var item in auction.Items)
            {
                packet.WriteGuid(item.Guid.To64());
                packet.WriteUInt32(item.UseCount);
            }
            packet.WriteUInt32((uint)auction.MinBid);
            packet.WriteUInt32((uint)auction.BuyoutPrice);
            packet.WriteUInt32(expireTime);
            ctx.SendPacketToServer(packet);
        }
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_REMOVE_ITEM)]
    public static void HandleAuctionRemoveItem(in AuctionRemoveItem auction, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_REMOVE_ITEM);
        packet.WriteGuid(auction.Auctioneer.To64());
        packet.WriteUInt32(auction.AuctionID);
        ctx.SendPacketToServer(packet);
    }

    [HandlesCmsg(Opcode.CMSG_AUCTION_PLACE_BID)]
    public static void HandleAuctionPlaceBId(in AuctionPlaceBid auction, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_PLACE_BID);
        packet.WriteGuid(auction.Auctioneer.To64());
        packet.WriteUInt32(auction.AuctionID);
        packet.WriteUInt32((uint)auction.BidAmount);
        ctx.SendPacketToServer(packet);
    }
}
