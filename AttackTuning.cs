using Candide.Entities.PlayerState;                 // PlayerState (protected Controller field), PlayerController
using Candide.Entities.PlayerState.PlayerStates;    // AttackState, DaggerAttackState, SwordAttackState
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Attack tuning — per-weapon melee swing ARC + cast offset as pack data: `weapon.attackArcDegrees` /
// `weapon.attackArcOffsetPx`. When set, the weapon's native swing cone uses the author's arc/offset instead
// of its class default (e.g. a 300° cleaver that swipes almost all around you).
//
// HOW THE CONE IS BUILT IN VANILLA: every cone-melee attack (sword/dagger/sledgehammer swings) goes through
// AttackState.Cast(), which constructs a ConeAttackSpellArgs with
//     AngularWidth   = π*4/5 (144°) + GetExtraAngularWidth()
//     DistanceOffset = GetAttackDistanceOffset()
// Both getters are protected virtuals on AttackState returning constants — base 0/0, DaggerAttackState
// −91π/142 & −24px (tight close-range stab), SwordAttackState stance-dependent (−2π/5 thrust stance; the
// `sword_whirlwind` perk returns +3.7699115 ≈ a full 2π circle). We postfix the getters and OVERRIDE the
// vanilla return for our items: extra = authorRad − π*4/5, so the final cone equals exactly the author's
// degrees in EVERY swing stance. The whirlwind perk's 360° spin is deliberately left alone (it's a perk
// effect, and the author's arc would only shrink it).
//
// SPEARS ARE SKIPPED: SpearAttackState/SpearSpecialState override Cast() entirely and build a
// LineAttackSpellArgs (a stab rectangle — Range/Width/Offset, no AngularWidth, no DistanceOffset), so a
// cone arc is meaningless there; their own GetExtraAngularWidth overrides are never consumed by the line
// path either. attackArcDegrees/attackArcOffsetPx on a Spear-class weapon are silently inert.
//
// Client-side only: melee hit detection runs on the ATTACKER'S client (Cast() fires the cone spell with
// sendOnlineSync:false), so these postfixes need no netcode — same trust model as vanilla melee.
// ---------------------------------------------------------------------------------------------------------

// Shared state + the id resolution both patches use. Parses nothing itself: ContentLoader fills ByItemId
// while reading packs (same pattern as HitVfxByItemId — the equipped item's BaseDataId IS the pack item id).
internal static class AttackTuningFeature
{
    // weapon item id -> (swing-cone width in RADIANS [<=0 = leave vanilla], cone-origin forward offset in
    // px [0 = leave vanilla]). Populated by ContentLoader from attackArcDegrees/attackArcOffsetPx.
    internal static readonly Dictionary<string, (float ArcRad, float OffsetPx)> ByItemId = new();

    // The width every cone Cast() starts from: AngularWidth = π*4/5 + GetExtraAngularWidth().
    internal const float VanillaBaseArc = (float)Math.PI * 4f / 5f;

    // PlayerState.Controller is protected readonly — the only non-public link in the weapon-id chain
    // Controller.WeaponAttackState.Item.BaseDataId (everything past it is public).
    private static readonly AccessTools.FieldRef<PlayerState, PlayerController> ControllerRef =
        AccessTools.FieldRefAccess<PlayerState, PlayerController>("Controller");

    // Tuning for the weapon driving this attack state, keyed by its item id. Runs once per swing (Cast),
    // not per frame; no allocation — a field deref chain + one dictionary lookup.
    internal static bool TryGetTuning(AttackState state, out (float ArcRad, float OffsetPx) tuning)
    {
        tuning = default;
        if (ByItemId.Count == 0 || state == null) return false;
        string id = ControllerRef(state)?.WeaponAttackState?.Item?.BaseDataId;
        return id != null && ByItemId.TryGetValue(id, out tuning);
    }
}

// Postfix every DECLARED GetExtraAngularWidth/GetAttackDistanceOffset in the cone-melee hierarchy (Harmony
// patches methods, not virtual slots — the base patch only covers states that don't override). Spear
// overrides are intentionally NOT patched (line attack, see header).
internal static class AttackTuningPatch
{
    // sword_whirlwind returns +3.7699115 extra (full 2π circle); every other vanilla extra is <= 0.
    private const float WhirlwindThreshold = 3f;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AttackState), "GetExtraAngularWidth")]
    private static void BaseArc(AttackState __instance, ref float __result) => ApplyArc(__instance, ref __result);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DaggerAttackState), "GetExtraAngularWidth")]
    private static void DaggerArc(AttackState __instance, ref float __result) => ApplyArc(__instance, ref __result);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SwordAttackState), "GetExtraAngularWidth")]
    private static void SwordArc(AttackState __instance, ref float __result)
    {
        if (__result > WhirlwindThreshold) return; // whirlwind perk's 360° spin stays vanilla
        ApplyArc(__instance, ref __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AttackState), "GetAttackDistanceOffset")]
    private static void BaseOffset(AttackState __instance, ref float __result) => ApplyOffset(__instance, ref __result);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DaggerAttackState), "GetAttackDistanceOffset")]
    private static void DaggerOffset(AttackState __instance, ref float __result) => ApplyOffset(__instance, ref __result);

    private static void ApplyArc(AttackState state, ref float extra)
    {
        try
        {
            if (AttackTuningFeature.TryGetTuning(state, out var t) && t.ArcRad > 0f)
                extra = t.ArcRad - AttackTuningFeature.VanillaBaseArc; // final cone == author's arc, any stance
        }
        catch (Exception e) { Plugin.Log.LogWarning($"attackArc failed: {e.Message}"); }
    }

    private static void ApplyOffset(AttackState state, ref float offset)
    {
        try
        {
            if (AttackTuningFeature.TryGetTuning(state, out var t) && t.OffsetPx != 0f)
                offset = t.OffsetPx;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"attackArcOffset failed: {e.Message}"); }
    }
}
