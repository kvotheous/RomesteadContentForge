using Candide;                                                  // Globals (GraphicsDevice)
using Candide.Data;                                             // VfxSpacAnimationData
using Candide.Database;                                         // SpacDataBase, VfxSpacAnimationDataBase
using Candide.Entities.Animations;                              // PlayerSpacState, SpacAnimationArguments
using Candide.Entities.PlayerState;                             // PlayerState (protected Controller/Timer), PlayerController
using Candide.Entities.PlayerState.PlayerStates;                // AttackState, Sword/Dagger/SledgehammerAttackState
using Candide.GameModels.Managers;                              // PlayerSkinManager
using Candide.Graphics.VFX;                                     // SpacRenderingArgs
using CandideCreator.Shared.Graphics;                           // SpriteSheet
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac, SpacDirectionsAnimation, SpacAnimation, SpacFrame
using CandideCreator.Shared.SpacSystem;                         // SpacAnimationState (_baseSpeed)
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;                         // Texture2D
using System;
using System.Collections.Generic;
using System.IO;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Attack tuning PLUS — two features that extend AttackTuning.cs:
//
// FEATURE A — PER-COMBO-STEP TUNING (`weapon.comboSteps`): an array where index = combo step, each entry
// `{ damageMultiplier, arcDegrees, swingTimerScale }`. Vanilla combo mechanics (all verified in decompile):
//   * The combo index is `AttackState.AttackStanceState` (protected int, rdll:157349). It advances in each
//     state's Update when the swing Timer expires (sword: rdll:162182 `AttackStanceState++`, wraps at >2, or
//     >3 with the sword_whirlwind perk which FORCES stance 3), and resets to 0 in AttackState.OnEnter when
//     the 0.5s combo window (`Controller.AttackNewStateTimer`) lapsed (rdll:157384).
//   * Per-stance DAMAGE lives in the protected virtual `GetDamageMultiplier()` consumed by AttackState.Cast()
//     (rdll:157433 -> SharedWeaponSpellHelper.CreateConeAttackSpell(..., GetDamageMultiplier(), ...)).
//     Sword override (rdll:162207): stance <2 -> 1.0, else 1.25 (the "3rd-hit 1.25x"). Sledgehammer override
//     keys on its private `_isFullAttack` (charged smash 1.5x), NOT the stance. Dagger has NO override (base
//     1.0) and NO stance use — its combo counter is the private `_attackCounter`, ++ed in Cast() (each
//     attack = 2 stabs: one cast in OnEnter, one at windup end rdll:158442) and reset on a fresh OnEnter
//     unless chained (`_skipCounterReset`, rdll:158451).
//   * Per-stance ARC lives in `GetExtraAngularWidth()` (sword stance 2 -> -2pi/5 thrust; whirlwind stance 3
//     -> +3.7699 full circle) — already postfixed flat by AttackTuningPatch; we coordinate (see below).
//   * SWING DURATION is `PlayerState.Timer` (protected float), set in AttackState.OnEnter AFTER Cast()
//     (rdll:157394) from the weapon's SwingTimer; SledgehammerAttackState.OnEnter overwrites it again after
//     base.OnEnter (rdll:160896); DaggerAttackState additionally derives its private `_attackTimer` (stab
//     cadence + 1.5x cooldown) after base.OnEnter (rdll:158364). All three are postfix-scalable, so
//     swingTimerScale IS implemented (cheap), with the dagger documented as approximate.
//
// STEP INDEX per class: Sword = AttackStanceState (0/1/2; 3 only exists with the whirlwind perk). Dagger =
// (_attackCounter-1) MOD steps.Length — daggers chain endlessly, so the author's array CYCLES per stab.
// Sledgehammer = always step 0 (it has no real stances; `_ = AttackStanceState` is dead code rdll:160891) —
// and the CHARGED smash keeps its vanilla 1.5x (we skip when _isFullAttack). Spears: line attack, no cone
// stances — comboSteps is silently inert, same stance AttackTuning takes for attackArcDegrees.
//
// COMPOSITION with AttackTuning's flat arc: we postfix the SAME GetExtraAngularWidth methods with
// [HarmonyPriority(Priority.Low)] — Harmony runs postfixes in DESCENDING priority, so ours run AFTER
// AttackTuningPatch's (Priority.Normal) and the per-step arc overwrites the flat attackArcDegrees whenever
// the current step sets arcDegrees > 0; steps with arcDegrees 0 fall through to the flat arc / vanilla.
// The whirlwind perk's 360 spin is left alone (same __result > 3 guard as AttackTuning).
//
// Client-side only: melee hit detection runs on the attacker's client (Cast() fires with
// sendOnlineSync:false) — same trust model as vanilla melee and AttackTuning.
//
// FEATURE B — CUSTOM SPACS (`pack.spacs`) + PLAYER ANIMATION REMIX (`pack.playerAnimations`):
//   * spacs: register NEW Spac entries by cloning a vanilla one (the exact EnsureTrailSpac pattern,
//     Plugin.cs: Spac.GetCopyWithOtherSpriteSheets + SpacDataBase.SpacMap[newId]) with swapped sprite
//     sheets, optionally retimed (`frameDurationScale` multiplies every SpacFrame.TickLength — durations
//     are per-frame floats in ticks, ccshared:13707; aux VFX triggers live ON the frames so they retime
//     with them) and/or resequenced (`frames` = indices into the base frame list, applied to every
//     animation/direction; meant for single-animation vfx spacs). The clone id is ALSO mirrored into
//     VfxSpacAnimationDataBase.AnimationDataMap, because "spac ids" taken by weapon hitVfx / spell vfx
//     fields are actually vfx-animation ids (Id+SpacId+AnimationName) — mirroring both maps makes a pack
//     spac id resolvable everywhere a vanilla spac id is accepted. Registration runs in a postfix on
//     PlayerSkinManager.Setup — the game calls it right after SpacDataBase.LoadSpacs (rdll:10912-10913),
//     so the SpacMap is freshly populated and the GraphicsDevice exists.
//   * playerAnimations: retime one of the 47 player animation states WITHOUT touching shared spac data.
//     Every state is a PlayerSpacState whose inner SpacAnimationState advances time as
//     `dt * TimeScale * _baseSpeed` (ccshared:4861) — `_baseSpeed` is a private per-INSTANCE float, so a
//     constructor postfix multiplying it by 1/frameDurationScale retimes exactly one state with zero
//     shared mutation (the PlayerSpacState ctor's well-known mutation of the shared spac — RESEARCH §7 —
//     is vanilla behavior we deliberately do NOT add to). Frame-INDEX couplings (spear casts at frame>=2,
//     rdll:161289; per-frame VfxTriggers) survive because indices are unchanged — only the wall-clock
//     moment each index is reached moves, which is the point of a retime. Visual-only; applies to every
//     player rendered on this client (all PlayerSpacStates are built through the same ctor).
// ---------------------------------------------------------------------------------------------------------

// =========================================================================================================
// FEATURE A — per-combo-step tuning
// =========================================================================================================

// Shared state + step resolution. Parses nothing itself: ContentLoader fills ByItemId from
// weapon.comboSteps while reading packs (same pattern as AttackTuningFeature.ByItemId).
internal static class ComboTuningFeature
{
    // damageMult <= 0, arcRad <= 0, timerScale == 1 each mean "leave vanilla" for that step.
    internal struct Step { public float DamageMult; public float ArcRad; public float TimerScale; }

    // weapon item id -> combo steps (index = stance; dagger cycles modulo length).
    internal static readonly Dictionary<string, Step[]> ByItemId = new();

    // Called by ContentLoader per weapon (degrees -> radians here so patches stay allocation-free).
    internal static void Register(string itemId, List<ComboStepDef> defs)
    {
        if (string.IsNullOrWhiteSpace(itemId) || defs == null || defs.Count == 0) return;
        var steps = new Step[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            ComboStepDef d = defs[i];
            steps[i] = new Step
            {
                DamageMult = d != null && d.DamageMultiplier > 0f ? d.DamageMultiplier : 0f,
                ArcRad = d != null && d.ArcDegrees > 0f ? d.ArcDegrees * (float)Math.PI / 180f : 0f,
                TimerScale = d != null && d.SwingTimerScale > 0f ? d.SwingTimerScale : 1f,
            };
        }
        ByItemId[itemId] = steps;
    }

    // Non-public fields we read/scale (all verified in the decompile, see header).
    private static readonly AccessTools.FieldRef<PlayerState, PlayerController> ControllerRef =
        AccessTools.FieldRefAccess<PlayerState, PlayerController>("Controller");
    private static readonly AccessTools.FieldRef<AttackState, int> StanceRef =
        AccessTools.FieldRefAccess<AttackState, int>("AttackStanceState");
    private static readonly AccessTools.FieldRef<DaggerAttackState, int> DaggerCounterRef =
        AccessTools.FieldRefAccess<DaggerAttackState, int>("_attackCounter");
    internal static readonly AccessTools.FieldRef<SledgehammerAttackState, bool> IsFullAttackRef =
        AccessTools.FieldRefAccess<SledgehammerAttackState, bool>("_isFullAttack");
    internal static readonly AccessTools.FieldRef<PlayerState, float> TimerRef =
        AccessTools.FieldRefAccess<PlayerState, float>("Timer");
    internal static readonly AccessTools.FieldRef<DaggerAttackState, float> DaggerAttackTimerRef =
        AccessTools.FieldRefAccess<DaggerAttackState, float>("_attackTimer");

    // The step for the swing this state is currently casting. Runs a few times per swing, no allocation.
    internal static bool TryGetStep(AttackState state, out Step step)
    {
        step = default;
        if (ByItemId.Count == 0 || state == null) return false;
        int index;
        switch (state)
        {
            case SwordAttackState: index = StanceRef(state); break;       // 0/1/2 (3 = whirlwind perk)
            case DaggerAttackState d:                                      // counter was ++ed before Cast()
                int c = DaggerCounterRef(d); index = c > 0 ? c - 1 : 0; break;
            case SledgehammerAttackState: index = 0; break;                // no real stances
            default: return false;                                         // spears & co: unsupported (line attack)
        }
        string id = ControllerRef(state)?.WeaponAttackState?.Item?.BaseDataId;
        if (id == null || !ByItemId.TryGetValue(id, out Step[] steps)) return false;
        if (state is DaggerAttackState) index %= steps.Length;             // daggers chain endlessly -> cycle
        if (index >= steps.Length) return false;                           // sword whirlwind stance 3 with a
        step = steps[index];                                               // 3-step array -> vanilla
        return true;
    }
}

// The patches. Damage postfixes REPLACE the stance multiplier (author has absolute control per step);
// arc postfixes overwrite at Priority.Low so they win over AttackTuning's flat arc (see header); timing
// postfixes scale the freshly-set swing Timer.
internal static class ComboTuningPatch
{
    // sword_whirlwind's GetExtraAngularWidth returns +3.7699 (full circle); all other extras are <= 0.
    private const float WhirlwindThreshold = 3f;

    // ---- damage multiplier ------------------------------------------------------------------------------
    // Base declaration covers DAGGERS (no override). Spear's Cast also consults the base method, but
    // TryGetStep's type switch rejects spear states, so this is inert there.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AttackState), "GetDamageMultiplier")]
    private static void BaseDamage(AttackState __instance, ref float __result) => ApplyDamage(__instance, ref __result);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SwordAttackState), "GetDamageMultiplier")]
    private static void SwordDamage(AttackState __instance, ref float __result) => ApplyDamage(__instance, ref __result);

    // Sledge: only the NORMAL swing is step 0; the fully-charged smash keeps its vanilla 1.5x identity.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SledgehammerAttackState), "GetDamageMultiplier")]
    private static void SledgeDamage(SledgehammerAttackState __instance, ref float __result)
    {
        try
        {
            if (ComboTuningFeature.IsFullAttackRef(__instance)) return;
            ApplyDamage(__instance, ref __result);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"comboSteps sledge damage failed: {e.Message}"); }
    }

    private static void ApplyDamage(AttackState state, ref float result)
    {
        try
        {
            if (ComboTuningFeature.TryGetStep(state, out var s) && s.DamageMult > 0f)
                result = s.DamageMult;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"comboSteps damage failed: {e.Message}"); }
    }

    // ---- swing arc (must run AFTER AttackTuningPatch's postfixes: Priority.Low < Normal) -----------------
    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(AttackState), "GetExtraAngularWidth")]
    private static void BaseArc(AttackState __instance, ref float __result) => ApplyArc(__instance, ref __result);

    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(DaggerAttackState), "GetExtraAngularWidth")]
    private static void DaggerArc(AttackState __instance, ref float __result) => ApplyArc(__instance, ref __result);

    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(SwordAttackState), "GetExtraAngularWidth")]
    private static void SwordArc(AttackState __instance, ref float __result)
    {
        if (__result > WhirlwindThreshold) return; // whirlwind perk's 360 spin stays vanilla (same as AttackTuning)
        ApplyArc(__instance, ref __result);
    }

    private static void ApplyArc(AttackState state, ref float extra)
    {
        try
        {
            if (ComboTuningFeature.TryGetStep(state, out var s) && s.ArcRad > 0f)
                extra = s.ArcRad - AttackTuningFeature.VanillaBaseArc; // final cone == this step's arc
        }
        catch (Exception e) { Plugin.Log.LogWarning($"comboSteps arc failed: {e.Message}"); }
    }

    // ---- swing timing ------------------------------------------------------------------------------------
    // AttackState.OnEnter sets Timer right after Cast(), so the stance read here is the one just swung.
    // base.OnEnter() calls from overrides execute the patched body, so this fires for every cone class.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AttackState), nameof(AttackState.OnEnter))]
    private static void BaseTiming(AttackState __instance) => ScaleTimer(__instance);

    // Sledge re-sets Timer AFTER base.OnEnter — rescale the final value (the base scaling got overwritten).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SledgehammerAttackState), nameof(SledgehammerAttackState.OnEnter))]
    private static void SledgeTiming(SledgehammerAttackState __instance) => ScaleTimer(__instance);

    // Dagger: also scale `_attackTimer` (stab cadence; its 1.5x becomes the cooldown), set after base.OnEnter.
    // Approximation: a dagger "attack" is two stabs sharing this timer, both scaled by the entry stab's step.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DaggerAttackState), nameof(DaggerAttackState.OnEnter))]
    private static void DaggerTiming(DaggerAttackState __instance)
    {
        try
        {
            if (ComboTuningFeature.TryGetStep(__instance, out var s) && s.TimerScale != 1f)
                ComboTuningFeature.DaggerAttackTimerRef(__instance) *= s.TimerScale;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"comboSteps dagger timing failed: {e.Message}"); }
    }

    private static void ScaleTimer(AttackState state)
    {
        try
        {
            if (ComboTuningFeature.TryGetStep(state, out var s) && s.TimerScale != 1f)
                ComboTuningFeature.TimerRef(state) *= s.TimerScale;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"comboSteps timing failed: {e.Message}"); }
    }
}

// =========================================================================================================
// FEATURE B — custom spacs from packs
// =========================================================================================================

// Builds pack-defined Spac clones (reskin + retime + resequence) and registers them under new ids in BOTH
// SpacDataBase.SpacMap and VfxSpacAnimationDataBase.AnimationDataMap. ContentLoader fills Defs at parse.
internal static class SpacForgeFeature
{
    private static readonly List<(SpacDef Def, string Dir, string Pack)> Defs = new();
    private static readonly HashSet<string> Built = new();   // ids we finished (or permanently failed)

    internal static void Register(SpacDef def, string packDir, string packName) => Defs.Add((def, packDir, packName));

    // Runs on PlayerSkinManager.Setup (right after SpacDataBase.LoadSpacs; GraphicsDevice exists by then).
    internal static void Apply()
    {
        if (Defs.Count == 0 || Globals.GraphicsDevice == null) return;
        foreach ((SpacDef def, string dir, string pack) in Defs)
        {
            if (Built.Contains(def.Id)) continue;
            try { BuildOne(def, dir, pack); }
            catch (Exception e) { Plugin.Log.LogError($"[{pack}] spac '{def.Id}' build failed: {e.Message}"); }
            Built.Add(def.Id);   // never retry a throwing def every Setup
        }
    }

    private static void BuildOne(SpacDef def, string dir, string pack)
    {
        if (SpacDataBase.SpacMap.ContainsKey(def.Id))
        {
            Plugin.Log.LogWarning($"[{pack}] spac id '{def.Id}' already exists (vanilla or another pack) — skipped. " +
                                  "To reskin a vanilla effect use the `vfx` section instead.");
            return;
        }
        Spac baseSpac = SpacDataBase.GetSpacOrNull(def.Base);
        if (baseSpac == null || baseSpac.SpriteSheets == null || baseSpac.SpriteSheets.Length == 0)
        {
            Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': base spac '{def.Base}' not found — skipped.");
            return;
        }

        // 1) Sheets: clone the array, swap each entry whose texture name matches a key (basename or full
        //    path) for the author's PNG — keeping the base normal/metal maps + frame grid (EnsureTrailSpac
        //    pattern). Unmatched keys log the available names so authors can discover them.
        var sheets = (SpriteSheet[])baseSpac.SpriteSheets.Clone();
        if (def.Sheets != null)
            foreach (KeyValuePair<string, string> kv in def.Sheets)
            {
                int idx = FindSheet(sheets, kv.Key);
                string png = Path.Combine(dir, kv.Value ?? "");
                if (idx < 0 || !File.Exists(png))
                {
                    Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': " +
                        (idx < 0 ? $"no sheet named '{kv.Key}' on '{def.Base}' (has: {SheetNames(sheets)})."
                                 : $"sheet file not found: {png}"));
                    continue;
                }
                using var fs = File.OpenRead(png);
                var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                SpriteSheet b = sheets[idx];
                sheets[idx] = new SpriteSheet(tex, b.NormalMap, b.MetalMap, b.SpriteWidth, b.SpriteHeight);
                Plugin.Log.LogInfo($"[{pack}] spac '{def.Id}': swapped sheet '{kv.Key}' " +
                                   $"(frame {b.SpriteWidth}x{b.SpriteHeight}; your sheet {tex.Width}x{tex.Height}).");
            }

        Spac clone = baseSpac.GetCopyWithOtherSpriteSheets(sheets);
        clone.Name = def.Id;

        // 2) Retime / resequence: deep-copy the animation arrays first — GetCopyWithOtherSpriteSheets shares
        //    SpritesAnimations by reference, and SpacFrame is a struct, so cloning the frame arrays gives us
        //    private TickLengths while the (never-mutated-by-us) Sprites/Triggers arrays stay shared.
        if ((def.FrameDurationScale > 0f && def.FrameDurationScale != 1f) || (def.Frames != null && def.Frames.Length > 0))
            clone.SpritesAnimations = RebuildAnimations(baseSpac.SpritesAnimations, def.FrameDurationScale, def.Frames, def.Id, pack);

        SpacDataBase.SpacMap[def.Id] = clone;

        // 3) Mirror into the vfx-animation database so the id works wherever "spac ids" are taken (weapon
        //    hitVfx, spell spacVfx, ...) — those fields are vfx-animation ids, resolved via AnimationDataMap.
        VfxSpacAnimationData baseVfx = VfxSpacAnimationDataBase.GetAnimationOrNull(def.Base);
        VfxSpacAnimationDataBase.AnimationDataMap[def.Id] = new VfxSpacAnimationData
        {
            Id = def.Id,
            SpacId = def.Id,
            AnimationName = baseVfx?.AnimationName
                ?? (clone.SpritesAnimations is { Length: > 0 } sa ? sa[0].AnimationName : "variant1"),
            SpacRenderingArgs = baseVfx?.SpacRenderingArgs ?? SpacRenderingArgs.Default,
            TimeScale = baseVfx?.TimeScale,
        };
        Plugin.Log.LogInfo($"[{pack}] registered spac '{def.Id}' (base '{def.Base}').");
    }

    private static SpacDirectionsAnimation[] RebuildAnimations(
        SpacDirectionsAnimation[] src, float durScale, int[] order, string spacId, string pack)
    {
        var result = new SpacDirectionsAnimation[src.Length];
        for (int a = 0; a < src.Length; a++)
        {
            SpacDirectionsAnimation da = src[a];                       // struct copy
            var dirs = new SpacAnimation[da.SpritesAnimations.Length];
            for (int d = 0; d < dirs.Length; d++)
            {
                SpacFrame[] baseFrames = da.SpritesAnimations[d].Frames;
                SpacFrame[] frames;
                if (order != null && order.Length > 0)
                {
                    var picked = new List<SpacFrame>(order.Length);
                    foreach (int i in order)
                        if (i >= 0 && i < baseFrames.Length) picked.Add(baseFrames[i]);   // struct copy
                        else Plugin.Log.LogWarning($"[{pack}] spac '{spacId}': frame index {i} out of range " +
                                                   $"(animation '{da.AnimationName}' has {baseFrames.Length}) — skipped.");
                    frames = picked.Count > 0 ? picked.ToArray() : (SpacFrame[])baseFrames.Clone();
                }
                else frames = (SpacFrame[])baseFrames.Clone();

                if (durScale > 0f && durScale != 1f)
                    for (int f = 0; f < frames.Length; f++) frames[f].TickLength *= durScale;
                dirs[d] = new SpacAnimation { Frames = frames };
            }
            da.SpritesAnimations = dirs;
            result[a] = da;
        }
        return result;
    }

    private static int FindSheet(SpriteSheet[] sheets, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return -1;
        for (int i = 0; i < sheets.Length; i++)
        {
            string name = sheets[i]?.Texture?.Name;
            if (name == null) continue;
            if (name == key || name.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase)
                            || Path.GetFileName(name).Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string SheetNames(SpriteSheet[] sheets)
    {
        var names = new List<string>(sheets.Length);
        foreach (SpriteSheet s in sheets) names.Add(s?.Texture?.Name ?? "?");
        return string.Join(", ", names);
    }
}

// Registration hook: the game calls SpacDataBase.LoadSpacs() immediately before PlayerSkinManager.Setup()
// on its loading thread (rdll:10912-10913), so this postfix sees a populated SpacMap + live GraphicsDevice.
// Idempotent (Built set), like ApplyVfx on the same hook.
internal static class SpacForgePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSkinManager), nameof(PlayerSkinManager.Setup))]
    private static void Postfix()
    {
        try { SpacForgeFeature.Apply(); }
        catch (Exception e) { Plugin.Log.LogError($"spac inject failed: {e}"); }
    }
}

// =========================================================================================================
// FEATURE B2 — player animation remix (retime one of the 47 player animation states)
// =========================================================================================================

// State name -> speed multiplier (1 / frameDurationScale). ContentLoader fills this from
// pack.playerAnimations. NO shared spac data is touched: the multiplier lands on the per-instance private
// `_baseSpeed` of each freshly-constructed state (see header).
internal static class PlayerAnimRemixFeature
{
    internal static readonly Dictionary<string, float> SpeedMultByState = new();

    internal static void Register(string stateName, PlayerAnimDef def, string pack)
    {
        if (string.IsNullOrWhiteSpace(stateName) || def == null) return;
        if (def.FrameDurationScale <= 0f || def.FrameDurationScale == 1f)
        {
            Plugin.Log.LogWarning($"[{pack}] playerAnimations '{stateName}': frameDurationScale must be > 0 and != 1 — skipped.");
            return;
        }
        SpeedMultByState[stateName] = 1f / def.FrameDurationScale;
        Plugin.Log.LogInfo($"[{pack}] player animation '{stateName}' retimed x{def.FrameDurationScale} duration.");
    }
}

internal static class PlayerAnimRemixPatch
{
    // SpacAnimationState advances time as dt * TimeScale * _baseSpeed (ccshared:4861); _baseSpeed is the
    // per-instance knob the 47-state table already feeds (e.g. run = 0.0125f, rdll:153559).
    private static readonly AccessTools.FieldRef<SpacAnimationState, float> BaseSpeedRef =
        AccessTools.FieldRefAccess<SpacAnimationState, float>("_baseSpeed");

    // Every player animation state is built through this exact ctor (AnimationStateMachine, rdll:195793).
    // States that share a name ("lifting_idle" x3, "spear" x2) all retime together — intended.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSpacState), MethodType.Constructor, typeof(Spac), typeof(SpacAnimationArguments))]
    private static void Postfix(PlayerSpacState __instance, SpacAnimationArguments args)
    {
        try
        {
            if (PlayerAnimRemixFeature.SpeedMultByState.Count == 0 || args?.AnimationName == null) return;
            if (!PlayerAnimRemixFeature.SpeedMultByState.TryGetValue(args.AnimationName, out float mult)) return;
            if (__instance.SpacState is SpacAnimationState sas)
                BaseSpeedRef(sas) *= mult;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"playerAnimations retime failed: {e.Message}"); }
    }
}
