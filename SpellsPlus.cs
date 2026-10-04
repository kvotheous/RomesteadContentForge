using Microsoft.Xna.Framework;
using Shared.Combat;
using Shared.Combat.Spells.Parameters;
using Shared.Models.Spells;
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// SpellsPlus — exposes the engine's remaining registered spell behaviors as pack JSON blocks.
// Every spelltype here is dual-registered in vanilla (client SetupSpellTypes in Romestead.dll +
// SetupServerSpellTypes in CandideServer.dll) and has at least one vanilla spell using it, so we
// only ever fill in the SAME args classes vanilla fills in — no new behaviors are invented.
//
// Trust model (same as the already-shipped single_projectile spells): for aoe / explosion / cone /
// line / multiProjectile the server's DoCast is wrapped in `if (!context.CastByPlayer)` — the
// CASTER's client simulates the projectile/area and reports hits, exactly like vanilla player
// projectiles. heal / massHeal are the opposite: the SERVER applies the healing itself (no
// CastByPlayer skip), so those are server-authoritative.
//
// A SpellDef sets EXACTLY ONE of the new blocks (aoe / explosion / cone / line / multiProjectile /
// heal / massHeal). The block overrides the default projectile behavior; summon / applyAura keep
// their existing priority and are untouched.
internal static class SpellsPlus
{
    // Called by Plugin.TryBuildSpell right after the SpellDataModel is constructed. Returns false
    // (= skip this spell) only when a block is present but unusable; true when no block is set or
    // the block applied cleanly.
    internal static bool TryApply(SpellDef d, string pack, SpellDataModel sm)
    {
        // Summon / ApplyAura spells already got their args in TryBuildSpell — they win.
        if (d.Summon != null && !string.IsNullOrWhiteSpace(d.Summon.Creature)) return true;
        if (!string.IsNullOrEmpty(d.ApplyAura)) return true;

        int blocks = (d.Aoe != null ? 1 : 0) + (d.Explosion != null ? 1 : 0) + (d.Cone != null ? 1 : 0)
                   + (d.Line != null ? 1 : 0) + (d.MultiProjectile != null ? 1 : 0)
                   + (d.Heal != null ? 1 : 0) + (d.MassHeal != null ? 1 : 0);
        if (blocks == 0) return true;
        if (blocks > 1)
        {
            Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': set exactly ONE of aoe/explosion/cone/line/multiProjectile/heal/massHeal ({blocks} set) — skipped.");
            return false;
        }

        if (d.Aoe != null) return TryBuildAoe(d, pack, sm);
        if (d.Explosion != null) return TryBuildExplosion(d, pack, sm);
        if (d.Cone != null) return TryBuildCone(d, pack, sm);
        if (d.Line != null) return TryBuildLine(d, pack, sm);
        if (d.MultiProjectile != null) return TryBuildMultiProjectile(d, pack, sm);
        if (d.Heal != null) return TryBuildHeal(d, pack, sm);
        return TryBuildMassHeal(d, pack, sm);
    }

    // ---- area_of_effect: a lingering damage circle on the ground (vanilla "aoe_fire:0", the
    // vulcan scroll's fire pool, chained off the player fireball at Target.TargetGround). The
    // circle ticks AttackSpellArgs damage every tickSeconds and can apply an aura per tick.
    // Ground visuals = projectileDisplayId (vanilla ground circles: "aoe_fire", "aoe_poison")
    // + the spell's particleTrail reused as the area's emitter (vanilla: "flame_aoe").
    private static bool TryBuildAoe(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            AoeDef a = d.Aoe;
            sm.SpellTypeId = "spelltype:area_of_effect";
            sm.SpellTypeArgs = new AreaOfEffectSpellArgs
            {
                RelativeCastPosition = new Vector3(0f, 0f, 1f),          // mirrors vanilla aoe_fire:0
                Radius = a.Radius <= 0f ? 48f : a.Radius,
                Height = a.Height <= 0f ? 16f : a.Height,
                TimeBetweenTicks = a.TickSeconds <= 0f ? 0.5f : a.TickSeconds,
                ParticleEmitterId = string.IsNullOrEmpty(d.ParticleTrail) ? null : d.ParticleTrail,
                ApplyAuraId = string.IsNullOrEmpty(a.ApplyAura) ? null : a.ApplyAura,
                HasLightsource = a.Lightsource,
                CanAffectEnvironment = a.AffectsEnvironment,
                ExplosiveKnockback = a.ExplosiveKnockback,
            };
            SuppressDefaultProjectileDisplay(sm);  // authors set "aoe_fire"/"aoe_poison" explicitly
            BumpMaxTargets(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "aoe", e); }
    }

    // ---- explosion: an instant blast at the cast/chain target point (vanilla player usage:
    // "item:fireball_explode:0", chained off the fireball tome). The client draws a built-in
    // visual explosion (sparks/dirt/crack/light) sized to Radius, plus an optional spac VFX
    // ("vfx_explosion1"). Best used as a chain step (projectile -> OnAnyHit -> explosion).
    private static bool TryBuildExplosion(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            ExplosionDef x = d.Explosion;
            sm.SpellTypeId = "spelltype:explosion";
            sm.SpellTypeArgs = new ExplosionSpellArgs
            {
                Radius = x.Radius <= 0f ? 32f : x.Radius,
                RelativeCastPosition = Vector3.Zero,                     // mirrors item:fireball_explode:0
                SpacVfx = string.IsNullOrEmpty(x.SpacVfx) ? null : x.SpacVfx,
            };
            SuppressDefaultProjectileDisplay(sm);  // "no_vfx" here disables ALL explosion visuals
            BumpMaxTargets(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "explosion", e); }
    }

    // ---- cone_attack: a melee-style cone in front of the caster, attached to them for the
    // spell's duration (the engine's player melee swings and the owl boss's rush both use this).
    // Range scales with the caster's attack-range stat. NOTE: the cone itself draws nothing —
    // give the spell a castSound and damage VFX comes from the hits.
    private static bool TryBuildCone(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            ConeDef c = d.Cone;
            sm.SpellTypeId = "spelltype:cone_attack";
            sm.SpellTypeArgs = new ConeAttackSpellArgs
            {
                RelativeCastPosition = Vector3.Zero,
                Range = c.Range <= 0f ? 48f : c.Range,
                AngularWidth = MathHelper.ToRadians(c.AngleDegrees <= 0f ? 90f : c.AngleDegrees),
                DistanceOffset = c.DistanceOffset,
            };
            // Player melee cones attach to the caster and ignore terrain (CreateConeAttackSpell).
            sm.ProjectileBaseArgs = new ProjectileBaseArgs
            {
                AttachProjectileToEntity = true,
                IgnoreTerrain = true,
                MinimumTimeBetweenHits = c.TickSeconds,   // 0 = each target hit once per swing
            };
            SuppressDefaultProjectileDisplay(sm);
            BumpMaxTargets(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "cone", e); }
    }

    // ---- line_attack: a damaging line/beam in the aim direction (vanilla "phoenixboss_firebreath").
    // Visuals come from a particle emitter spread along the line (vanilla: "flame_aoe"); reuses the
    // spell's particleTrail field. growSeconds > 0 makes it extend from 0 to its full length
    // (firebreath-style) — that REQUIRES the emitter (the client's grow update reads it).
    private static bool TryBuildLine(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            LineDef l = d.Line;
            float length = l.Length <= 0f ? 128f : l.Length;
            bool hasEmitter = !string.IsNullOrEmpty(d.ParticleTrail);
            bool grow = l.GrowSeconds > 0f;
            if (grow && !hasEmitter)
            {
                Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': line.growSeconds needs particleTrail set (the growing line is drawn by its emitter) — growth disabled.");
                grow = false;
            }
            sm.SpellTypeId = "spelltype:line_attack";
            sm.SpellTypeArgs = new LineAttackSpellArgs
            {
                RelativeCastPosition = Vector3.Zero,                     // firebreath uses (0,0,0)
                Range = length,
                Width = l.Width <= 0f ? 25f : l.Width,
                Offset = Vector2.Zero,
                DamageSourceType = DamageSourceType.DamagingArea,        // mirrors phoenixboss_firebreath
                GrowOverTime = grow,
                GrowthTime = l.GrowSeconds,
                StartLength = 0f,
                EndLength = length,
                UseEmitter = hasEmitter,
                ParticleEmitterId = d.ParticleTrail,
            };
            // firebreath: not attached, respects terrain, re-hits every 0.6s while you stand in it.
            sm.ProjectileBaseArgs = new ProjectileBaseArgs
            {
                MinimumTimeBetweenHits = l.TickSeconds <= 0f ? 0.6f : l.TickSeconds,
            };
            SuppressDefaultProjectileDisplay(sm);
            BumpMaxTargets(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "line", e); }
    }

    // ---- multiple_projectile_attack: a fan of count projectiles spread over spreadDegrees,
    // centered on the aim direction (vanilla player usage: the bow's multishot, AnglesDeg
    // [-12,0,12] relative to caster direction). Velocity / trail / collision radius / display id
    // reuse the SpellDef's existing projectile fields, so it drops in for any projectile spell.
    private static bool TryBuildMultiProjectile(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            MultiProjectileDef m = d.MultiProjectile;
            int count = m.Count < 1 ? 3 : m.Count;
            float spread = m.SpreadDegrees < 0f ? 0f : m.SpreadDegrees;
            var angles = new List<float>(count);
            if (count == 1) angles.Add(0f);
            else for (int i = 0; i < count; i++) angles.Add(-spread / 2f + spread * i / (count - 1));
            sm.SpellTypeId = "spelltype:multiple_projectile_attack";
            sm.SpellTypeArgs = new MultipleProjectileSpellArgs
            {
                RelativeCastPosition = new Vector3(0f, 0f, 1f),
                ProjectileVelocity = d.ProjectileVelocity,
                UseParticleTrail = !string.IsNullOrEmpty(d.ParticleTrail),
                ParticleTrailEmitterId = d.ParticleTrail,
                ProjectileCollisionCircleRadius = d.CollisionRadius,
                DirectionsAreRelativeToCasterDirection = true,           // mirrors the bow multishot
                AnglesDeg = angles,
            };
            // keep the default arrow_spell display — these ARE projectiles
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "multiProjectile", e); }
    }

    // ---- heal: instant heal + optional regen-over-time on the CASTER (vanilla player template
    // "item:heal_scroll:regen": SlowHeal=20 over SlowHealTime=10, CanHit=Caster, cast with
    // Target.Self). Server-authoritative: ServerHealSpell applies the heal itself and scales it
    // by the caster's HealingDoneModifier stat; the regen becomes the game's own slow-heal aura.
    private static bool TryBuildHeal(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            HealDef h = d.Heal;
            if (h.Amount <= 0f && (h.RegenAmount <= 0f || h.RegenSeconds <= 0f))
            {
                Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': heal needs amount > 0 and/or regenAmount + regenSeconds > 0 — skipped.");
                return false;
            }
            sm.SpellTypeId = "spelltype:heal";
            sm.SpellTypeArgs = new HealSpellArgs
            {
                InstantHeal = h.Amount,
                SlowHeal = h.RegenAmount,
                SlowHealTime = h.RegenSeconds,
            };
            sm.CanHit = ProjectileHitFlags.Caster;                       // heals need a target = self
            SuppressDefaultProjectileDisplay(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "heal", e); }
    }

    // ---- mass_heal: heals every PLAYER within radius of the cast point (vanilla player template
    // "item:mass_heal:tier_0": 25% of max health, radius 80, CanHit=FriendlyAoE). Either a flat
    // amount or a percentage of each player's max health. Server-authoritative. Players only —
    // it will NOT heal summons or NPCs (the server iterates GetPlayersInWorld).
    private static bool TryBuildMassHeal(SpellDef d, string pack, SpellDataModel sm)
    {
        try
        {
            MassHealDef mh = d.MassHeal;
            if (mh.Amount <= 0f && mh.PercentOfMaxHealth <= 0f)
            {
                Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': massHeal needs amount or percentOfMaxHealth > 0 — skipped.");
                return false;
            }
            sm.SpellTypeId = "spelltype:mass_heal";
            sm.SpellTypeArgs = new MassHealSpellArgs
            {
                DoPercentageHealing = mh.PercentOfMaxHealth > 0f,
                PercentageOfMaxHealth = mh.PercentOfMaxHealth,
                Amount = mh.Amount,
                Radius = mh.Radius <= 0f ? 80f : mh.Radius,
            };
            sm.CanHit = ProjectileHitFlags.FriendlyAoE;                  // mirrors item:mass_heal:tier_0
            SuppressDefaultProjectileDisplay(sm);
            return true;
        }
        catch (Exception e) { return Fail(pack, d, "massHeal", e); }
    }

    // SpellDef's projectileDisplayId defaults to "arrow_spell" — right for projectiles, wrong for
    // areas/cones/heals (it would draw an arrow sprite on the ground). Drop the default; anything
    // the author set EXPLICITLY (e.g. "aoe_fire", or "no_vfx" to mute an explosion) is kept.
    private static void SuppressDefaultProjectileDisplay(SpellDataModel sm)
    {
        if (sm.ProjectileDisplayId == "arrow_spell") sm.ProjectileDisplayId = null;
    }

    // AttackDef defaults maxTargets to 1 — a silent trap for area damage (vanilla areas use
    // 99-9999). Only bump the untouched default; an explicit author value > 1 stays.
    private static void BumpMaxTargets(SpellDataModel sm)
    {
        if (sm.AttackSpellArgs != null && sm.AttackSpellArgs.MaxTargets <= 1)
            sm.AttackSpellArgs.MaxTargets = 99;
    }

    private static bool Fail(string pack, SpellDef d, string block, Exception e)
    {
        Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': {block} block failed: {e.Message}");
        return false;
    }
}
