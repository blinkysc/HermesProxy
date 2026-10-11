using HermesProxy;
using HermesProxy.World;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// A damage log names the cast that went off, not the caster's next one. Thrall's previous Lava
/// Burst landing after his next one had started took the open cast's id, so that cast's go got a
/// new id and the client kept its precast fire on his hands (capture of 2026-10-11 14:22).
/// </summary>
public class SyntheticCastIdTests
{
    private const uint LavaBurst = 59519;
    private static readonly WowGuid128 Thrall = WowGuid128.Create(HighGuidType703.Creature, 0, 32518, 80109);

    [Fact]
    public void MissileLandingDuringNextCast_KeepsBothIds()
    {
        var state = GameSessionData.CreateNewGameSessionData(null!);
        var first = state.BeginSyntheticCast(Thrall, LavaBurst);
        Assert.Equal(first, state.FinishSyntheticCast(Thrall, LavaBurst));

        var second = state.BeginSyntheticCast(Thrall, LavaBurst);
        Assert.Equal(first, state.FinishedSyntheticCast(Thrall, LavaBurst));
        Assert.Equal(second, state.FinishSyntheticCast(Thrall, LavaBurst));
    }

    [Fact]
    public void DamageLogWithNoCastSeen_StillGetsAnId()
    {
        var state = GameSessionData.CreateNewGameSessionData(null!);
        var id = state.FinishedSyntheticCast(Thrall, LavaBurst);

        Assert.False(id.IsEmpty());
        Assert.Equal(id, state.FinishedSyntheticCast(Thrall, LavaBurst));
    }
}
