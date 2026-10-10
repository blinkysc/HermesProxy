using System.Collections.Generic;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// <see cref="GlobalCooldown.Compute"/> against AzerothCore's Spell::TriggerGlobalCooldown, on rows
/// as CSV/SpellGlobalCooldowns3.csv has them.
/// </summary>
public class GlobalCooldownTests
{
    // Frostbolt 27071: 1500 ms, category 133, magic, mage family, class mask bit 5.
    private static readonly SpellGcdData Frostbolt = new(1500, 133, 1, 65536, 0, 3, 32, 0, 0);
    // Sinister Strike 1752: 1000 ms, melee, ability.
    private static readonly SpellGcdData SinisterStrike = new(1000, 133, 2, 327696, 1024, 8, 8388610, 0, 0);
    // Multi-Shot 2643: ranged, uses the ranged slot.
    private static readonly SpellGcdData MultiShot = new(1500, 133, 3, 65538, 0, 9, 4096, 0, 0);

    [Fact]
    public void NoStartRecoveryTime_NoGlobalCooldown()
        => Assert.Equal(0, GlobalCooldown.Compute(default, 0.8f, Class.Mage, null, null));

    [Fact]
    public void Haste_ShortensAMagicSpell()
        => Assert.Equal(1200, GlobalCooldown.Compute(Frostbolt, 0.8f, Class.Mage, null, null));

    [Fact]
    public void Haste_NeverGoesBelowOneSecond()
        => Assert.Equal(1000, GlobalCooldown.Compute(Frostbolt, 0.5f, Class.Mage, null, null));

    [Theory]
    [InlineData(2)] // melee
    [InlineData(3)] // ranged
    public void Haste_SkipsMeleeAndRanged(byte dmgClass)
        => Assert.Equal(1500, GlobalCooldown.Compute(Frostbolt with { DmgClass = dmgClass }, 0.8f, Class.Mage, null, null));

    [Fact]
    public void Haste_SkipsRangedSlotSpells()
        => Assert.Equal(1500, GlobalCooldown.Compute(MultiShot with { DmgClass = 1 }, 0.8f, Class.Hunter, null, null));

    [Fact]
    public void Haste_SkipsAbilities()
        => Assert.Equal(1500, GlobalCooldown.Compute(Frostbolt with { Attributes0 = 0x10 }, 0.8f, Class.Mage, null, null));

    [Fact]
    public void Haste_OnlyForExactly1500InCategory133()
        => Assert.Equal(1000, GlobalCooldown.Compute(SinisterStrike with { DmgClass = 1, Attributes0 = 0 }, 0.8f, Class.Rogue, null, null));

    [Fact]
    public void OutOfRange_IsLeftAlone()
        => Assert.Equal(2000, GlobalCooldown.Compute(Frostbolt with { StartRecoveryTime = 2000 }, 0.8f, Class.Mage,
            new Dictionary<byte, int> { [5] = -500 }, null));

    [Fact]
    public void FlatModifier_OnAMatchingBit()
        => Assert.Equal(1000, GlobalCooldown.Compute(Frostbolt, 1.0f, Class.Mage, new Dictionary<byte, int> { [5] = -500 }, null));

    [Fact]
    public void PercentModifier_OnAMatchingBit()
        => Assert.Equal(1200, GlobalCooldown.Compute(Frostbolt, 1.0f, Class.Mage, null, new Dictionary<byte, int> { [5] = -20 }));

    [Fact]
    public void Modifier_OnAnotherBit_DoesNotApply()
        => Assert.Equal(1500, GlobalCooldown.Compute(Frostbolt, 1.0f, Class.Mage, new Dictionary<byte, int> { [6] = -500 }, null));

    [Fact]
    public void Modifier_OnTheSecondMaskWord()
        => Assert.Equal(1000, GlobalCooldown.Compute(Frostbolt with { SpellClassMask0 = 0, SpellClassMask1 = 1 }, 1.0f, Class.Mage,
            new Dictionary<byte, int> { [32] = -500 }, null));

    [Fact]
    public void Modifier_OfAnotherClassFamily_DoesNotApply()
        => Assert.Equal(1500, GlobalCooldown.Compute(Frostbolt, 1.0f, Class.Priest, new Dictionary<byte, int> { [5] = -500 }, null));

    [Fact]
    public void Modifier_SkippedForIgnoreCasterModifiers()
        => Assert.Equal(1500, GlobalCooldown.Compute(Frostbolt with { Attributes3 = 0x20000000 }, 1.0f, Class.Mage,
            new Dictionary<byte, int> { [5] = -500 }, null));

    [Fact]
    public void Modifier_ThenHaste_ThenClamp()
        // (1500 - 100) * 0.9 = 1260
        => Assert.Equal(1260, GlobalCooldown.Compute(Frostbolt, 0.9f, Class.Mage, new Dictionary<byte, int> { [5] = -100 }, null));
}
