using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// One character's full auction-house scan (CMSG_AUCTION_REPLICATE_ITEMS), served the way
/// TrinityCore's <c>AuctionHouseObject::BuildReplicate</c> serves it.
/// </summary>
/// <remarks>
/// The 3.4.3 client's full scan (Auctionator's, or the default UI's) asks for the house in pages:
/// each request carries the change numbers of the previous answer, and the server returns the
/// next <c>Count</c> auctions by id after the cursor. A 3.3.5a server has no such request; it
/// has the whole-house query instead (CMSG_AUCTION_LIST_ITEMS with getAll, at most 55,000
/// auctions on AzerothCore). The proxy asks that once per scan and pages its answer.
/// <list type="bullet">
/// <item>Global is a replication id the scan keeps; Cursor is the last auction id sent;
/// Tombstone is the highest auction id while a page came back full, 0 once the house is done.</item>
/// <item>A request whose change numbers are not the ones last answered gets an empty answer, and
/// so does a new scan within <see cref="Cooldown"/> of the last (Auction.ReplicateItemsCooldown).</item>
/// </list>
/// </remarks>
public sealed class AuctionReplicate
{
    /// <summary>TrinityCore's default Auction.ReplicateItemsCooldown: one full scan per 15 minutes.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(900);

    private static int _lastReplicationId;

    public uint Global { get; }
    public uint Cursor { get; private set; }
    public uint Tombstone { get; private set; }
    public long NextAllowedTick { get; }
    public uint DesiredDelay { get; }

    // Auctions by id, as the server answered the whole-house query; dropped once the scan is done.
    private SortedDictionary<uint, AuctionItem>? _auctions;

    public AuctionReplicate(IEnumerable<AuctionItem> auctions, long nowTick, uint desiredDelay)
    {
        Global = (uint)Interlocked.Increment(ref _lastReplicationId);
        NextAllowedTick = nowTick + (long)Cooldown.TotalMilliseconds;
        DesiredDelay = desiredDelay;
        _auctions = new SortedDictionary<uint, AuctionItem>();
        foreach (var auction in auctions)
            _auctions[auction.AuctionID] = auction;
    }

    public bool InProgress => Cursor != Tombstone && Global != 0;

    /// <summary>Past its cooldown and not mid-scan: the next request starts a new scan.</summary>
    public bool Expired(long nowTick) => !InProgress && NextAllowedTick <= nowTick;

    /// <summary>The answer to a later request of this scan.</summary>
    public AuctionReplicateResponse Answer(uint global, uint cursor, uint tombstone, uint count, long nowTick)
    {
        if (global != Global || cursor != Cursor || tombstone != Tombstone)
            return Empty();
        if (!InProgress && NextAllowedTick > nowTick)
            return Empty();
        return Page(cursor, count);
    }

    /// <summary>The next <paramref name="count"/> auctions after <paramref name="cursor"/>.</summary>
    public AuctionReplicateResponse Page(uint cursor, uint count)
    {
        if (_auctions == null || _auctions.Count == 0 || count == 0)
            return Empty();

        var response = Empty();
        foreach (var (id, auction) in _auctions)
        {
            if (id <= cursor)
                continue;
            response.Items.Add(auction);
            if (response.Items.Count == count)
                break;
        }

        bool pageFull = response.Items.Count == count;
        response.ChangeNumberGlobal = Global;
        response.ChangeNumberCursor = Cursor = response.Items.Count > 0 ? response.Items[^1].AuctionID : 0;
        response.ChangeNumberTombstone = Tombstone = pageFull ? _auctions.Keys.Last() : 0;

        if (!InProgress)
            _auctions = null;
        return response;
    }

    private AuctionReplicateResponse Empty() => new() { DesiredDelay = DesiredDelay };
}

/// <summary>A scan's first request, waiting for the server's answer to the whole-house query.</summary>
public sealed record PendingAuctionReplicate(WowGuid128 Auctioneer, uint Cursor, uint Count);
