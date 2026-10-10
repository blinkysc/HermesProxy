using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The legacy server's replies to the player's casts are matched by the cast count each request
/// carried, not by spell id.
/// </summary>
/// <remarks>
/// Two Frostbolts were indistinguishable by spell id: the server's NotReady for the second one
/// was handed to the first, which it was still casting, and the client cancelled that cast bar
/// (capture of 2026-10-09 20:18:53).
/// </remarks>
[Collection(nameof(SpellGlobalCooldownsData))]
public class CastCountMatchingTests
{
    private const uint Frostbolt = 27071;
    private readonly LegacyHandlerHarness _harness = new(recordClientPackets: true);
    private readonly WowGuid64 _player = new(HighGuidTypeLegacy.Player, 509309);

    public CastCountMatchingTests() => _harness.SetActivePlayer(_player);

    private ClientCastRequest Pending(byte castCount, uint low)
    {
        var request = new ClientCastRequest
        {
            SpellId = Frostbolt,
            CastCount = castCount,
            ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, 530, Frostbolt, low),
            PrepareSent = true,
        };
        _harness.Session.GameState.PendingNormalCasts.Enqueue(request);
        return request;
    }

    private byte[] SpellStart(byte castCount) => LegacyPacketBuilder.Build(Opcode.SMSG_SPELL_START, p =>
    {
        p.WritePackedGuid(_player);
        p.WritePackedGuid(_player);
        p.WriteUInt8(castCount);
        p.WriteUInt32(Frostbolt);
        p.WriteUInt32(0);    // cast flags
        p.WriteUInt32(2500); // cast time
        p.WriteUInt32(0);    // target flags: self
    });

    private byte[] SpellGo(byte castCount) => LegacyPacketBuilder.Build(Opcode.SMSG_SPELL_GO, p =>
    {
        p.WritePackedGuid(_player);
        p.WritePackedGuid(_player);
        p.WriteUInt8(castCount);
        p.WriteUInt32(Frostbolt);
        p.WriteUInt32(0);    // cast flags
        p.WriteUInt32(0);    // server time
        p.WriteUInt8(0);     // hit targets
        p.WriteUInt8(0);     // miss targets
        p.WriteUInt32(0);    // target flags: self
    });

    private static byte[] CastFailed(byte castCount, byte reason) => LegacyPacketBuilder.Build(Opcode.SMSG_CAST_FAILED, p =>
    {
        p.WriteUInt8(castCount);
        p.WriteUInt32(Frostbolt);
        p.WriteUInt8(reason);
    });

    private void Deliver(Opcode opcode, byte[] wire)
    {
        Action<WorldPacket> handler = opcode switch
        {
            Opcode.SMSG_SPELL_START => _harness.Client.HandleSpellStart,
            Opcode.SMSG_SPELL_GO => _harness.Client.HandleSpellGo,
            _ => _harness.Client.HandleCastFailed,
        };
        _harness.Deliver(opcode, wire, handler);
    }

    private const byte NotReady = 67;
    private const byte Interrupted = 40;

    [Fact]
    public void FailureOfTheSecondCast_LeavesTheFirstCasting()
    {
        var first = Pending(1, 10417);
        var second = Pending(2, 10418);

        Deliver(Opcode.SMSG_SPELL_START, SpellStart(1));
        Deliver(Opcode.SMSG_CAST_FAILED, CastFailed(2, NotReady));

        Assert.True(first.HasStarted);
        Assert.Equal([first], _harness.Session.GameState.PendingNormalCasts);
        var failed = Assert.Single(_harness.ClientWire.Sent.Select(s => s.Packet).OfType<CastFailed>());
        Assert.Equal(second.ServerGUID, failed.CastID);

        Deliver(Opcode.SMSG_SPELL_GO, SpellGo(1));
        Assert.Empty(_harness.Session.GameState.PendingNormalCasts);
        var go = Assert.Single(_harness.ClientWire.Sent.Select(s => s.Packet).OfType<SpellGo>());
        Assert.Equal(first.ServerGUID, go.Cast.CastID);
    }

    [Fact]
    public void ReplyWithCountZero_AnswersNoClientCast()
    {
        var pending = Pending(1, 10417);

        // The server's own (triggered) cast of the same spell carries count 0.
        Deliver(Opcode.SMSG_SPELL_START, SpellStart(0));
        Deliver(Opcode.SMSG_CAST_FAILED, CastFailed(0, NotReady));

        Assert.False(pending.HasStarted);
        Assert.Equal([pending], _harness.Session.GameState.PendingNormalCasts);
    }

    [Fact]
    public void NextCastCount_SkipsZeroAndDropsAPendingCastStillHoldingTheCount()
    {
        var state = _harness.Session.GameState;
        state.LastCastCount = 254;
        var stale = Pending(255, 1);

        Assert.Equal(255, state.NextCastCount());
        Assert.DoesNotContain(stale, state.PendingNormalCasts);
        Assert.Equal(1, state.NextCastCount());
    }

    [Fact]
    public void SpellStart_StartsTheGlobalCooldown_AndAnInterruptCancelsIt()
    {
        using var data = SpellGlobalCooldownsData.With(Frostbolt, new SpellGcdData(1500, 133, 1, 65536, 0, 3, 32, 0, 0));
        var state = _harness.Session.GameState;
        Pending(1, 10417);

        long before = Environment.TickCount64;
        Deliver(Opcode.SMSG_SPELL_START, SpellStart(1));
        long remaining = state.GlobalCooldownRemaining(Frostbolt, before);
        Assert.InRange(remaining, 1400, 1500 + (Environment.TickCount64 - before));

        Deliver(Opcode.SMSG_CAST_FAILED, CastFailed(1, Interrupted));
        Assert.Equal(0, state.GlobalCooldownRemaining(Frostbolt, Environment.TickCount64));
    }

    [Fact]
    public void InstantCast_StartsTheGlobalCooldownAtSpellGo()
    {
        using var data = SpellGlobalCooldownsData.With(Frostbolt, new SpellGcdData(1500, 133, 1, 65536, 0, 3, 32, 0, 0));
        Pending(1, 10417);

        long before = Environment.TickCount64;
        Deliver(Opcode.SMSG_SPELL_GO, SpellGo(1));

        Assert.True(_harness.Session.GameState.GlobalCooldownRemaining(Frostbolt, before) > 1400);
    }

    [Fact]
    public void Queue_BlockedByACastInProgress_ThenByTheGlobalCooldown_ThenFree()
    {
        using var data = SpellGlobalCooldownsData.With(Frostbolt, new SpellGcdData(1500, 133, 1, 65536, 0, 3, 32, 0, 0));
        var state = _harness.Session.GameState;
        var next = new ClientCastRequest { SpellId = Frostbolt };
        Pending(1, 10417);

        Deliver(Opcode.SMSG_SPELL_START, SpellStart(1));
        Assert.Equal(-1, SpellSystem.BlockedForMs(state, next, Environment.TickCount64));

        Deliver(Opcode.SMSG_SPELL_GO, SpellGo(1));
        long now = Environment.TickCount64;
        Assert.InRange(SpellSystem.BlockedForMs(state, next, now), 1, 1500);
        Assert.Equal(0, SpellSystem.BlockedForMs(state, next, now + 1500));
    }

    [Fact]
    public void Queue_HeldCastWaitingOnlyForTheGlobalCooldown_IsReleasedByATimer()
    {
        using var data = SpellGlobalCooldownsData.With(Frostbolt, new SpellGcdData(1500, 133, 1, 65536, 0, 3, 32, 0, 0));
        var state = _harness.Session.GameState;
        Pending(1, 10417);
        Deliver(Opcode.SMSG_SPELL_GO, SpellGo(1)); // an instant: no cast in progress, global cooldown running

        var held = new ClientCastRequest { SpellId = Frostbolt };
        state.HeldNormalCast = new HeldNormalCast(new SpellCastRequest(), held, 0);
        var ctx = new HermesProxy.World.Dispatch.SessionContext(_harness.Session, null, _harness.Client);
        SpellSystem.ReleaseHeldNormalCast(in ctx);

        Assert.Same(held, state.HeldNormalCast?.Request);   // not sent yet
        Assert.Equal(1, _harness.Session.ToClient.PendingCount); // the wake-up at the cooldown's end
    }
}

/// <summary>
/// Serialises the tests that swap <see cref="GameData.SpellGlobalCooldowns"/>, a process-wide table.
/// </summary>
[CollectionDefinition(nameof(SpellGlobalCooldownsData), DisableParallelization = true)]
public sealed class SpellGlobalCooldownsData : IDisposable
{
    private readonly FrozenDictionary<uint, SpellGcdData> _previous = GameData.SpellGlobalCooldowns;

    public static SpellGlobalCooldownsData With(uint spellId, SpellGcdData data)
    {
        var scope = new SpellGlobalCooldownsData();
        GameData.SpellGlobalCooldowns = new Dictionary<uint, SpellGcdData> { [spellId] = data }.ToFrozenDictionary();
        return scope;
    }

    public void Dispose() => GameData.SpellGlobalCooldowns = _previous;
}
