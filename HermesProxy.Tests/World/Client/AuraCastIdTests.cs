using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// An aura carries the cast id of the cast that applied it, as on a native server. With a made-up
/// id of its own, Arcane Infusion's hand glow (started by the trinket's cast) stayed on the player.
/// </summary>
public class AuraCastIdTests
{
    private const uint ArcaneInfusion = 51987;

    [Fact]
    public void AppliedAura_CarriesTheApplyingCastsId()
    {
        var (harness, player) = Setup();
        var castId = NoteCast(harness, player, 1);

        Assert.Equal(castId, Deliver(harness, player, ArcaneInfusion).CastID);
    }

    [Fact]
    public void AuraUpdatedInPlace_KeepsTheIdItWasAppliedWith()
    {
        var (harness, player) = Setup();
        var first = NoteCast(harness, player, 1);
        Deliver(harness, player, ArcaneInfusion);
        NoteCast(harness, player, 2);

        Assert.Equal(first, Deliver(harness, player, ArcaneInfusion).CastID);
    }

    [Fact]
    public void AuraAppliedAgainAfterRemoval_TakesTheNewCastsId()
    {
        var (harness, player) = Setup();
        NoteCast(harness, player, 1);
        Deliver(harness, player, ArcaneInfusion);
        Deliver(harness, player, 0);
        var second = NoteCast(harness, player, 2);

        Assert.Equal(second, Deliver(harness, player, ArcaneInfusion).CastID);
    }

    [Fact]
    public void AuraWithNoCastSeen_StillGetsAnId()
    {
        var (harness, player) = Setup();

        Assert.False(Deliver(harness, player, ArcaneInfusion).CastID.IsEmpty());
    }

    private static (LegacyHandlerHarness, WowGuid64) Setup()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var player = new WowGuid64(HighGuidTypeLegacy.Player, 77);
        harness.SetActivePlayer(player);
        return (harness, player);
    }

    private static WowGuid128 NoteCast(LegacyHandlerHarness harness, WowGuid64 caster, ulong counter)
    {
        var castId = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, 571, ArcaneInfusion, counter);
        harness.Session.GameState.NoteFinishedCast(caster.To128(harness.Session.GameState), ArcaneInfusion, castId);
        return castId;
    }

    private static AuraDataInfo Deliver(LegacyHandlerHarness harness, WowGuid64 player, uint spellId)
    {
        var wire = LegacyPacketBuilder.Build(Opcode.SMSG_AURA_UPDATE, packet =>
        {
            packet.WritePackedGuid(player);
            packet.WriteUInt8(14);
            packet.WriteUInt32(spellId);
            if (spellId == 0) return;
            packet.WriteUInt8(0x11); // EffectIndex0 | Positive
            packet.WriteUInt8(80);
            packet.WriteUInt8(1);
            packet.WritePackedGuid(player);
        });
        harness.Deliver(Opcode.SMSG_AURA_UPDATE, wire, harness.Client.HandleAuraUpdate);
        var update = Assert.IsType<AuraUpdate>(harness.ClientWire.Sent.Last().Packet);
        return Assert.Single(update.Auras).AuraData;
    }
}
