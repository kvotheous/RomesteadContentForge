using Candide;
using Candide.Data;
using Candide.Database;
using Candide.Graphics;
using Candide.Particles;
using CandideCreator.Shared;
using CandideCreator.Shared.Graphics;
using CandideCreator.Shared.Models.Particle;
using CandideCreator.Shared.Models.Particle.SpawnArea;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom particle emitters: a pack defines Particles (one sprite + per-particle physics/fade/light) and
// Emitters (which particles to spew, how often, from what area, with what velocity). Both registries are
// plain public maps (ParticleDataBase / ParticleEmitterDataBase), so we mirror the vanilla construction in
// ClientDataSetup.SetupParticles / LoadParticleEmitters and append our entries.
//
// Hook = postfix on ParticleEmitterDataBase.LoadParticleEmitters, which covers both moments the data exists:
//   - initial load (inside the game's content-loading Task), and
//   - the `reloadparticles` terminal command (clears BOTH maps, re-runs vanilla setup, then this method) —
//     so our particles must be (re)added here too, not only the emitters.
// Particles are registered BEFORE emitters: an emitter snapshots each particle's ParticleData at build time
// (GetParticleData maps a fresh copy), so the particle must already be in the map when the emitter is built.
// ---------------------------------------------------------------------------------------------------------

[HarmonyPatch(typeof(ParticleEmitterDataBase), nameof(ParticleEmitterDataBase.LoadParticleEmitters))]
internal static class ParticleLoadPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            ParticlesFeature.Apply();
        }
        catch (Exception e) { Plugin.Log.LogError($"Particle injection failed: {e}"); }
    }
}

internal static class ParticlesFeature
{
    // Particles are BUILT once (their sprite sheets are GPU textures) and cached here, then re-ADDED to the
    // live map on every LoadParticleEmitters — a hot reload clears ParticleDataMap and rebuilds only vanilla,
    // which would otherwise drop our ids. Emitters are cheap (no textures), so they're rebuilt fresh each
    // time from whatever is currently in the particle map.
    private static readonly List<Particle> _particles = new();
    private static bool _particlesBuilt;

    public static void Apply()
    {
        BuildParticles();

        // Skip ids that already exist: keeps re-runs idempotent and never stomps a vanilla id (AddParticles
        // would warn about the duplicate, then overwrite it).
        var fresh = _particles.Where(p => !ParticleDataBase.ParticleDataMap.ContainsKey(p.Id)).ToList();
        if (fresh.Count > 0) ParticleDataBase.AddParticles(fresh);

        int added = 0;
        foreach (EmitterDef e in ContentLoader.EmitterDefs)
        {
            try
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Id)) { Plugin.Log.LogWarning("emitter skipped (needs id)."); continue; }
                if (ParticleEmitterDataBase.ParticleEmitterDataMap.ContainsKey(e.Id)) continue;   // already registered
                ParticleEmitterData data = BuildEmitter(e);
                if (data == null) continue;
                ParticleEmitterDataBase.ParticleEmitterDataMap[e.Id] = data;
                added++;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"emitter '{e?.Id}' failed: {ex.Message}"); }
        }

        if (fresh.Count > 0 || added > 0)
            Plugin.Log.LogInfo($"Content Forge: {fresh.Count} custom particle(s), {added} emitter(s) registered.");
    }

    // Build each pack particle into a game Particle (once). Runs on the game's content-loading Task — the
    // same Task later runs PlayerSkinManager.Setup, where ApplyVfx/ApplyCustomSkins already create textures,
    // so Texture2D.FromStream is safe here too.
    private static void BuildParticles()
    {
        if (_particlesBuilt || ContentLoader.ParticleDefs.Count == 0) return;
        if (Globals.GraphicsDevice == null) return;   // not ready yet; retry on the next load

        foreach ((ParticleDef def, string packDir) in ContentLoader.ParticleDefs)
        {
            try
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Id))
                { Plugin.Log.LogWarning("particle skipped (needs id)."); continue; }
                SpriteSheet sheet = ResolveSheet(def, packDir);
                if (sheet == null) continue;
                _particles.Add(BuildParticle(def, sheet));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"particle '{def?.Id}' failed: {e.Message}"); }
        }
        _particlesBuilt = true;
        Plugin.Log.LogInfo($"Content Forge: {_particles.Count} custom particle(s) built.");
    }

    // The particle's sprite: either a vanilla sheet by content path (free reuse of the game's art, e.g.
    // "media/particles/dust3") or the author's own PNG shipped next to the pack.
    private static SpriteSheet ResolveSheet(ParticleDef def, string packDir)
    {
        int fw = def.FrameWidth < 1 ? 8 : def.FrameWidth;
        int fh = def.FrameHeight < 1 ? 8 : def.FrameHeight;
        if (!string.IsNullOrWhiteSpace(def.VanillaSheet))
            return Content.SpriteSheet(def.VanillaSheet, fw, fh);
        if (string.IsNullOrWhiteSpace(def.Texture))
        { Plugin.Log.LogWarning($"particle '{def.Id}': needs texture or vanillaSheet."); return null; }
        string path = Path.Combine(packDir, def.Texture);
        if (!File.Exists(path)) { Plugin.Log.LogWarning($"particle '{def.Id}': texture not found: {path}"); return null; }
        using var fs = File.OpenRead(path);
        var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        return new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, fw, fh);
    }

    // Mirrors the vanilla "dust:small" construction. The Mask must name every component the def actually
    // uses, or the simulation skips it. Velocity+Acceleration are always on: the emitter writes both arrays
    // for every spawned particle (SetParticleProperties), and without the flags its velocity/acceleration
    // spans would silently do nothing. (Base 0x27 + Animated = 47u — exactly vanilla dust:small.)
    private static Particle BuildParticle(ParticleDef def, SpriteSheet sheet)
    {
        ParticleMasks mask = ParticleMasks.Drawable | ParticleMasks.Velocity | ParticleMasks.Acceleration;
        var p = new Particle
        {
            Id = def.Id,
            Drawable = new Drawable
            {
                Sprite = sheet,
                Frame = 0,
                Color = Vec4(def.Color, Vector4.One),
                Emissiveness = def.Emissiveness,
                Vertical = def.Vertical,
            },
            General = new General
            {
                TimeScale = 1f,
                MaxLifeLength = def.MaxLife > 0f ? def.MaxLife : 1.5f,
            },
        };

        if (def.Frames != null && def.Frames.Length > 0)
        {
            mask |= ParticleMasks.Animated;
            p.Animation = new ParticleAnimation
            {
                TimeScale = 1f,
                FrameAnimation = new FrameAnimation(def.Frames, def.FrameDuration > 0f ? def.FrameDuration : 0.06f, def.Loop),
                RemoveOnFinish = def.RemoveOnFinish,
            };
        }
        if (def.Fade != null)
        {
            mask |= ParticleMasks.Fade;
            if (!Enum.TryParse(def.Fade.Type, ignoreCase: true, out Fade.FadeType ft)) ft = Fade.FadeType.FadeOut;
            p.Fade = new Fade { Type = ft, FadeTime = def.Fade.Time > 0f ? def.Fade.Time : 0.5f };
        }
        if (def.Gravity != 0f)
        {
            mask |= ParticleMasks.Gravity;
            p.Gravity = new Gravity { GravityScale = def.Gravity };
        }
        if (def.Friction != null)
        {
            mask |= ParticleMasks.Friction;
            p.Friction = new Friction { GroundFriction = def.Friction.Ground, AirFriction = def.Friction.Air };
        }
        if (def.Light != null)
        {
            mask |= ParticleMasks.Lightsource;
            p.LightSourceComponent = new LightSourceComponent
            {
                Radius = def.Light.Radius,
                Color = Vec3(def.Light.Color, Vector3.One),
            };
        }
        if (def.HslDrift != null)
        {
            mask |= ParticleMasks.Hsldrift;
            p.HslDrift = new HslDrift
            {
                H = def.HslDrift.H, S = def.HslDrift.S, L = def.HslDrift.L,
                HDrift = def.HslDrift.HDrift, SDrift = def.HslDrift.SDrift, LDrift = def.HslDrift.LDrift,
            };
        }
        p.Mask = mask;
        return p;
    }

    // Mirrors LoadParticleEmitters' own construction: resolve each (particle id, weight) pair against the
    // live particle map, then the 12-arg ParticleEmitterData ctor. SpawnFrequency is the SECONDS BETWEEN
    // spawn ticks (ParticleEmitter stores it as TimeBetweenSpawns; <= 0 spawns every frame).
    private static ParticleEmitterData BuildEmitter(EmitterDef e)
    {
        var weighted = new List<WeightedParticle>();
        if (e.Particles != null)
            foreach (ParticleWeightDef pw in e.Particles)
            {
                if (pw == null || string.IsNullOrWhiteSpace(pw.Id)) continue;
                ParticleData pd = ParticleDataBase.GetParticleDataOrNull(pw.Id);
                if (pd == null) { Plugin.Log.LogWarning($"emitter '{e.Id}': particle '{pw.Id}' not found."); continue; }
                weighted.Add(new WeightedParticle(pd, pw.Weight < 1 ? 1 : pw.Weight));
            }
        if (weighted.Count == 0)
        { Plugin.Log.LogWarning($"emitter '{e.Id}' skipped (no valid particles)."); return null; }

        // Spans are nullable on purpose — null lets the emitter fall back to its own defaults (no rotation,
        // no velocity, no acceleration). Angles come in as degrees; the game wants radians. Z is up.
        RotationSpan? rot = null;
        if (e.Rotation != null)
            rot = new RotationSpan
            {
                RadsMinAngle = MathHelper.ToRadians(e.Rotation.MinDeg),
                RadsMaxAngle = MathHelper.ToRadians(e.Rotation.MaxDeg),
            };
        VelocitySpan? vel = null;
        if (e.Velocity != null)
            vel = new VelocitySpan(e.Velocity.Min, e.Velocity.Max, Vec3(e.Velocity.Direction, new Vector3(0f, 0f, 1f)));
        AccelerationSpan? acc = null;
        if (e.Acceleration != null)
            acc = new AccelerationSpan
            {
                MinAcceleration = e.Acceleration.Min,
                MaxAcceleration = e.Acceleration.Max,
                Direction = Vec3(e.Acceleration.Direction, new Vector3(0f, 0f, 1f)),
            };

        return new ParticleEmitterData(weighted.ToArray(), Vec3(e.Offset, Vector3.Zero),
            e.SpawnFrequency, e.EmitterLifetime, e.AmountSpawned < 1 ? 1 : e.AmountSpawned,
            BuildSpawnArea(e), rot, vel, acc, null, null, null);
    }

    // Where (relative to the emitter) each particle appears. Sizes are in world pixels; cube/line use
    // size [x, y, z(height)], cylinder uses radius + size[2] as its height, sphere just the radius.
    private static ISpawnArea BuildSpawnArea(EmitterDef e)
    {
        SpawnAreaDef a = e.SpawnArea;
        if (a == null) return new PointSpawnArea();
        float sx = a.Size != null && a.Size.Length > 0 ? a.Size[0] : 0f;
        float sy = a.Size != null && a.Size.Length > 1 ? a.Size[1] : 0f;
        float sz = a.Size != null && a.Size.Length > 2 ? a.Size[2] : 0f;
        switch (a.Type?.ToLowerInvariant())
        {
            case null: case "": case "point": return new PointSpawnArea();
            case "cube": return new CubeSpawnArea(new Vector3(sx, sy, sz), RectanglePositioning.Center);
            case "cylinder": return new CylinderSpawnArea(a.Radius, 0f, sz, 0f);
            case "line": return new LineSpawnArea { Length = sx, Width = sy, Height = sz };
            case "sphere": return new SphereSpawnArea(a.Radius, 0f);
            default:
                Plugin.Log.LogWarning($"emitter '{e.Id}': unknown spawnArea type '{a.Type}' (point/cube/cylinder/line/sphere) — using point.");
                return new PointSpawnArea();
        }
    }

    private static Vector3 Vec3(float[] v, Vector3 fallback)
        => v != null && v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : fallback;

    private static Vector4 Vec4(float[] v, Vector4 fallback)
        => v != null && v.Length >= 4 ? new Vector4(v[0], v[1], v[2], v[3]) : fallback;
}
