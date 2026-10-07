using System.Runtime.CompilerServices;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Client;

/// <summary>
/// Legacy → modern spline flag predicates, shared by <c>SMSG_ON_MONSTER_MOVE</c> and the
/// CreateObject spline block so the two paths cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>Smooth path.</b> Every pre-3.4 client treats a bare <c>Flying</c> flag as "interpolate this
/// path": AzerothCore <c>MoveSplineFlag.h</c> has <c>Mask_CatmullRom = Flying | Catmullrom</c> and
/// <c>isSmooth()</c> tests that mask. The 3.4.3 client dropped the alias — TrinityCore
/// wotlk_classic <c>MoveSplineFlag.h</c> <c>isSmooth()</c> tests <c>Catmullrom</c> alone — so a
/// legacy flying path reaches the modern client as a linear one unless <c>CatmullRom</c> is added
/// on translation. Native 3.4.3 sets Fly + Smooth + Uncompressed on its own taxi spline
/// (<c>FlightPathMovementGenerator::DoReset</c>), which is what the translation reproduces.
/// </para>
/// <para>
/// <b>Server flight.</b> Taxi starts used to be recognised by an exact <c>WalkMode | Flying</c>
/// match. That only holds when the flight happens to start from a walking player:
/// <c>MoveSplineInit</c>'s constructor mixes the unit's current walk state into the spline, and
/// neither AzerothCore's nor cMaNGOS's <c>FlightPathMovementGenerator</c> sets <c>WalkMode</c> or
/// <c>Catmullrom</c> explicitly — both call <c>SetFly()</c> and <c>SetVelocity()</c> and nothing
/// else. On AzerothCore the taxi spline is therefore <c>Flying | UncompressedPath</c>, the exact
/// match never fired, and the flight ran without the proxy ever noticing it had started (#301).
/// </para>
/// </remarks>
internal static class SplineFlagTranslation
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSmoothPath(SplineFlagVanilla flags) =>
        (flags & SplineFlagVanilla.Flying) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSmoothPath(SplineFlagTBC flags) =>
        (flags & SplineFlagTBC.Flying) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSmoothPath(SplineFlagWotLK flags) =>
        (flags & (SplineFlagWotLK.Flying | SplineFlagWotLK.CatmullRom)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsServerFlight(SplineFlagVanilla flags) =>
        (flags & SplineFlagVanilla.Flying) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsServerFlight(SplineFlagTBC flags) =>
        (flags & SplineFlagTBC.Flying) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsServerFlight(SplineFlagWotLK flags) =>
        (flags & SplineFlagWotLK.Flying) != 0;

    /// <summary>
    /// The modern-only flags a native 3.4.3 server sets on a move into or out of a vehicle seat.
    /// </summary>
    /// <remarks>
    /// A 3.3.5a server sends such a move as <c>TransportEnter</c> or <c>TransportExit</c> alone. In a
    /// native capture every one of them, for players and for a mount's vendor passengers, also
    /// carried <c>SmoothGroundPath | CanSwim</c>. Natively <c>CanSwim</c> follows the unit; the legacy
    /// wire does not say, and every seated unit captured so far could swim.
    /// </remarks>
    /// <summary>
    /// WotLK spline flags to their modern counterparts, bit by bit.
    /// </summary>
    /// <remarks>
    /// Casting by name misses every flag that was renamed (Trajectory is Parabolic, Knockback is
    /// OrientationFixed, AnimationTier is Animation, OrientationInverted is Backward) and maps the
    /// WotLK <c>Animation</c> bit, which is something else, onto the modern one.
    /// </remarks>
    internal static SplineFlagModern ToModern(SplineFlagWotLK flags)
    {
        // The anim tier sits in the low three bits on both.
        var result = (SplineFlagModern)(flags & (SplineFlagWotLK.AnimTierFly | SplineFlagWotLK.AnimTierSubmerged));
        if ((flags & SplineFlagWotLK.Done) != 0) result |= SplineFlagModern.Done;
        if ((flags & SplineFlagWotLK.Falling) != 0) result |= SplineFlagModern.Falling;
        if ((flags & SplineFlagWotLK.NoSpline) != 0) result |= SplineFlagModern.NoSpline;
        if ((flags & SplineFlagWotLK.Trajectory) != 0) result |= SplineFlagModern.Parabolic;
        if ((flags & SplineFlagWotLK.WalkMode) != 0) result |= SplineFlagModern.CanSwim;
        if ((flags & SplineFlagWotLK.Flying) != 0) result |= SplineFlagModern.Flying;
        if ((flags & SplineFlagWotLK.Knockback) != 0) result |= SplineFlagModern.OrientationFixed;
        if ((flags & SplineFlagWotLK.CatmullRom) != 0) result |= SplineFlagModern.CatmullRom;
        if ((flags & SplineFlagWotLK.Cyclic) != 0) result |= SplineFlagModern.Cyclic;
        if ((flags & SplineFlagWotLK.EnterCycle) != 0) result |= SplineFlagModern.EnterCycle;
        if ((flags & SplineFlagWotLK.AnimationTier) != 0) result |= SplineFlagModern.Animation;
        if ((flags & SplineFlagWotLK.Frozen) != 0) result |= SplineFlagModern.Frozen;
        if ((flags & SplineFlagWotLK.TransportEnter) != 0) result |= SplineFlagModern.TransportEnter;
        if ((flags & SplineFlagWotLK.TransportExit) != 0) result |= SplineFlagModern.TransportExit;
        if ((flags & SplineFlagWotLK.Unknown8) != 0) result |= SplineFlagModern.Unknown8;
        if ((flags & SplineFlagWotLK.OrientationInverted) != 0) result |= SplineFlagModern.Backward;
        if ((flags & SplineFlagWotLK.UncompressedPath) != 0) result |= SplineFlagModern.UncompressedPath;
        if ((flags & SplineFlagWotLK.Unknown10) != 0) result |= SplineFlagModern.Unknown10;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static SplineFlagModern SeatMoveFlags(SplineFlagWotLK flags) =>
        (flags & (SplineFlagWotLK.TransportEnter | SplineFlagWotLK.TransportExit)) != 0
            ? SplineFlagModern.SmoothGroundPath | SplineFlagModern.CanSwim
            : SplineFlagModern.None;
}
