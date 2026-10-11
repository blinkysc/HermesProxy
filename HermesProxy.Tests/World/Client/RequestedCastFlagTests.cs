using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// Retail 3.4 sets <c>CastFlag.NoGcd</c> on a player's or item's own casts and on nothing else.
/// AzerothCore sets it on any spell without a global cooldown, which left proc and NPC buff
/// visuals on (Vengeance, Thrall's Warchief's Blessing, capture of 2026-10-11 14:20).
/// </summary>
public class RequestedCastFlagTests
{
    private const uint Untriggered = (uint)(CastFlag.Unknown7 | CastFlag.NoGcd);
    private const uint Triggered = Untriggered | (uint)CastFlag.PendingCast;

    private static readonly WowGuid128 Player = WowGuid128.Create(HighGuidType703.Player, 452971);
    private static readonly WowGuid128 Item = WowGuid128.Create(HighGuidType703.Item, 2489184023);
    private static readonly WowGuid128 Thrall = WowGuid128.Create(HighGuidType703.Creature, 0, 32518, 80109);
    private static readonly WowGuid128 Pet = WowGuid128.Create(HighGuidType703.Pet, 0, 416, 7);

    [Fact]
    public void PlayersOwnCast_IsRequested()
        => Assert.True(WorldClient.IsRequestedCast(Player, Untriggered, 35395)); // Crusader Strike

    [Fact]
    public void ItemUse_IsRequested()
        => Assert.True(WorldClient.IsRequestedCast(Item, Untriggered, 49859)); // Rune of Command

    [Fact]
    public void PlayersTriggeredSpell_IsNotRequested()
        => Assert.False(WorldClient.IsRequestedCast(Player, Triggered, 20050)); // Vengeance

    [Fact]
    public void ItemProc_IsNotRequested()
        => Assert.False(WorldClient.IsRequestedCast(Item, Triggered, 51987)); // Arcane Infusion

    [Fact]
    public void CreatureCast_IsNotRequested()
        => Assert.False(WorldClient.IsRequestedCast(Thrall, Untriggered, 64670)); // Warchief's Blessing

    [Fact]
    public void PetCast_IsNotRequested()
        => Assert.False(WorldClient.IsRequestedCast(Pet, Untriggered, 3716)); // Torment
}
