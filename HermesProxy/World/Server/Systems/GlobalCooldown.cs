using System;
using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// One spell's inputs to a 3.3.5a server's global cooldown, from CSV/SpellGlobalCooldowns3.csv.
/// A spell without a row has every field 0: no global cooldown of its own, and category 0.
/// </summary>
public readonly record struct SpellGcdData(
    uint StartRecoveryTime,
    uint StartRecoveryCategory,
    byte DmgClass,
    uint Attributes0,
    uint Attributes3,
    uint SpellClassSet,
    uint SpellClassMask0,
    uint SpellClassMask1,
    uint SpellClassMask2);

/// <summary>
/// The global cooldown a 3.3.5a server starts when it casts a spell, as AzerothCore's
/// <c>Spell::TriggerGlobalCooldown</c> computes it.
/// </summary>
/// <remarks>
/// The proxy needs it to queue the 3.4.3 client's next cast (see <see cref="SpellSystem"/>):
/// a native server holds a cast that arrives within the last 400 ms of the global cooldown,
/// a 3.3.5a server rejects it as not ready. The formula is AzerothCore's:
/// <list type="bullet">
/// <item>no StartRecoveryTime, no global cooldown;</item>
/// <item>only a StartRecoveryTime of 1000-1500 ms is modified, and the result stays in that range;</item>
/// <item>SPELLMOD_GLOBAL_COOLDOWN modifiers apply as <c>base * (1 + pct/100) + flat</c>;</item>
/// <item>cast speed (haste) applies to category 133 spells of exactly 1500 ms that are not
/// melee, ranged, ranged-slot or ability spells.</item>
/// </list>
/// The modifier totals are what the server sends per class-mask bit (SMSG_SET_FLAT/PCT_SPELL_MODIFIER),
/// summed over the bits the spell's mask has. AzerothCore sums per modifier instead, so a single
/// modifier whose mask matches several of a spell's bits counts once there and once per bit
/// here; no 3.3.5a global cooldown modifier has such a mask. Its script-only exceptions
/// (a percent modifier that applies only together with a cast-time one, Backdraft) are not
/// modelled; when the result is off, the server's own answer reaches the client unchanged.
/// </remarks>
public static class GlobalCooldown
{
    public const byte SpellModOpGlobalCooldown = 21;
    public const uint MinGcd = 1000;
    public const uint MaxGcd = 1500;
    public const uint StandardCategory = 133;

    private const byte DmgClassMelee = 2;
    private const byte DmgClassRanged = 3;
    private const uint Attr0UsesRangedSlot = 0x00000002;
    private const uint Attr0IsAbility = 0x00000010;
    private const uint Attr3IgnoreCasterModifiers = 0x20000000;

    /// <summary>Milliseconds of global cooldown the server starts for <paramref name="spell"/>; 0 for none.</summary>
    public static int Compute(in SpellGcdData spell, float modCastSpeed, Class playerClass,
        IReadOnlyDictionary<byte, int>? flatMods, IReadOnlyDictionary<byte, int>? pctMods)
    {
        if (spell.StartRecoveryTime == 0)
            return 0;

        int gcd = (int)spell.StartRecoveryTime;
        if (spell.StartRecoveryTime < MinGcd || spell.StartRecoveryTime > MaxGcd)
            return gcd;

        if ((spell.Attributes3 & Attr3IgnoreCasterModifiers) == 0 && spell.SpellClassSet == SpellFamilyOf(playerClass))
        {
            int flat = SumMatchingBits(spell, flatMods);
            int pct = SumMatchingBits(spell, pctMods);
            if (flat != 0 || pct != 0)
                gcd = (int)(gcd * (1.0f + pct / 100.0f)) + flat;
        }

        if (spell.StartRecoveryCategory == StandardCategory && spell.StartRecoveryTime == MaxGcd &&
            spell.DmgClass != DmgClassMelee && spell.DmgClass != DmgClassRanged &&
            (spell.Attributes0 & (Attr0UsesRangedSlot | Attr0IsAbility)) == 0)
        {
            gcd = (int)(gcd * modCastSpeed);
        }

        return Math.Clamp(gcd, (int)MinGcd, (int)MaxGcd);
    }

    private static int SumMatchingBits(in SpellGcdData spell, IReadOnlyDictionary<byte, int>? mods)
    {
        if (mods == null || mods.Count == 0)
            return 0;

        int total = 0;
        foreach (var (bit, value) in mods)
        {
            uint word = (bit / 32) switch
            {
                0 => spell.SpellClassMask0,
                1 => spell.SpellClassMask1,
                2 => spell.SpellClassMask2,
                _ => 0,
            };
            if ((word & (1u << (bit % 32))) != 0)
                total += value;
        }
        return total;
    }

    /// <summary>The SpellClassSet (SpellFamilyName) of a class's own spells, which its modifiers match.</summary>
    public static uint SpellFamilyOf(Class playerClass) => playerClass switch
    {
        Class.Mage => 3,
        Class.Warrior => 4,
        Class.Warlock => 5,
        Class.Priest => 6,
        Class.Druid => 7,
        Class.Rogue => 8,
        Class.Hunter => 9,
        Class.Paladin => 10,
        Class.Shaman => 11,
        Class.Deathknight => 15,
        _ => 0,
    };
}
