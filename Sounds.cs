using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Raw audio in packs: an author drops `ding.wav` / `.ogg` / `.mp3` / `.flac` next to the pack json, maps it
// under `"sounds": { "ding": "ding.wav" }`, and the file is playable as `cf:sound:<namespace>:ding` anywhere
// the game takes an FMOD event id (castSound, controllerParams sfx, map music/ambience one-shots, ...).
// No FMOD Studio project needed — this is the zero-tooling sibling of the .bank loader in Plugin.cs.
//
// How: EVERY one-shot in the game funnels through two methods on Candide.Sound.SoundPlayer —
//   public  PlayEventOneShot(string, float, float)                                  (2D / UI)
//   private Play3DEventOneShot(string, Vector3, string?, float, float, float)       (all 3D paths, incl.
//     the public Play3DEventOneShot overloads, EmitSoundOneShot, AbstractSpell.PlayCastSound)
// We prefix both: ids starting with "cf:sound:" are played through the embedded FMOD CORE API
// (StudioSystem.getCoreSystem) and the original is skipped (return false). That skip matters — the original
// would hit EventDescriptionCache.GetValidated, which THROWS for ids the banks don't know. Non-cf ids are
// untouched.
//
// Volume: channels are routed into the "sound effects" bus' channel group, so the user's SFX slider applies.
// GOTCHA: Bus.getChannelGroup returns ERR_STUDIO_NOT_LOADED unless lockChannelGroup() was processed first —
// hence the flushCommands() between the two.
// 3D: the game maps world px -> FMOD units as (x, y - z) * 0.0625 (FmodVectorExtension.ToFmodScreenSpace);
// we do the same so our sounds sit in the same space as the Studio listener (SoundPlayer.Update sets it).
// Lifetime: SoundPlayer.UnloadAndRelease releases the WHOLE studio system (e.g. leaving to the main menu),
// which invalidates every core sound — a prefix frees our handles while the system is still alive and drops
// the cached core/bus so the next play lazily re-initializes against the new system (same re-init concern
// as the _banksLoaded bank loader).
// ---------------------------------------------------------------------------------------------------------

internal static class SoundsFeature
{
    public const string IdPrefix = "cf:sound:";

    // Same scale the game uses for FMOD space (SoundPlayer.DistanceFactor). Min/max are in FMOD units:
    // full volume within 16 px, attenuated to silence by roughly a screen and a half (~640 px).
    private const float DistanceFactor = 0.0625f;
    private const float MinDistance3D = 1f;
    private const float MaxDistance3D = 40f;

    private static readonly Dictionary<string, string> _files = new();       // id -> absolute audio file path
    private static readonly Dictionary<string, FMOD.Sound> _loaded = new();  // id -> loaded core sound (cached)
    private static readonly HashSet<string> _warned = new();                 // ids already complained about

    private static FMOD.System _core;              // core system behind the game's StudioSystem
    private static FMOD.ChannelGroup _sfxGroup;    // "sound effects" bus group (default(0) = master fallback)
    private static bool _coreReady;

    // Called at pack parse: id is already fully qualified ("cf:sound:<ns>:<key>"), path is absolute.
    public static void Register(string id, string path) => _files[id] = path;

    public static bool Owns(string id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public static void Play2D(string id, float volume, float pitch)
    {
        try
        {
            if (!EnsureCore() || !TryGetSound(id, out FMOD.Sound snd)) return;
            if (_core.playSound(snd, _sfxGroup, paused: true, out FMOD.Channel ch) != FMOD.RESULT.OK) return;
            ch.setMode(FMOD.MODE._2D);   // sounds load as 3D; this channel ignores the listener (UI-style)
            ch.setVolume(volume);
            ch.setPitch(pitch);
            ch.setPaused(false);
        }
        catch (Exception e) { WarnOnce(id, $"playing '{id}' failed: {e.Message}"); }
    }

    public static void Play3D(string id, Vector3 worldPos, float volume, float pitch)
    {
        try
        {
            if (!EnsureCore() || !TryGetSound(id, out FMOD.Sound snd)) return;
            if (_core.playSound(snd, _sfxGroup, paused: true, out FMOD.Channel ch) != FMOD.RESULT.OK) return;
            // Same world->FMOD mapping as the game's ToFmodScreenSpace (height z folds into y, then scaled).
            var pos = new FMOD.VECTOR
            {
                x = worldPos.X * DistanceFactor,
                y = (worldPos.Y - worldPos.Z) * DistanceFactor,
                z = 0f,
            };
            FMOD.VECTOR vel = default;
            ch.set3DAttributes(ref pos, ref vel);
            ch.setVolume(volume);
            ch.setPitch(pitch);
            ch.setPaused(false);
        }
        catch (Exception e) { WarnOnce(id, $"playing '{id}' failed: {e.Message}"); }
    }

    // The studio system is gone (or about to be) — free our core handles while it's still alive and forget
    // the cached core/bus. Registered file paths and the warn-once set survive; sounds reload on next play.
    public static void ReleaseAll()
    {
        foreach (FMOD.Sound s in _loaded.Values)
            try { s.release(); } catch { /* a dead handle just returns an error code */ }
        _loaded.Clear();
        _coreReady = false;
    }

    // Lazy init on first cf:sound play (the studio system must exist by then — something asked us to make
    // noise). Fetches the core system + the SFX bus' channel group once, then caches until ReleaseAll.
    private static bool EnsureCore()
    {
        if (_coreReady) return true;
        object boxed = AccessTools.Field(AccessTools.TypeByName("Candide.Sound.SoundPlayer"), "StudioSystem")?.GetValue(null);
        if (boxed is not FMOD.Studio.System studio || !studio.isValid()) return false;
        if (studio.getCoreSystem(out _core) != FMOD.RESULT.OK) return false;

        _sfxGroup = default;   // handle 0 = master group; sound still plays if the bus dance fails
        if (studio.getBus("bus:/Audio premaster/sound effects", out FMOD.Studio.Bus bus) == FMOD.RESULT.OK)
        {
            bus.lockChannelGroup();
            studio.flushCommands();                        // the lock must be processed before the getter works
            if (bus.getChannelGroup(out FMOD.ChannelGroup g) == FMOD.RESULT.OK) _sfxGroup = g;
        }
        _coreReady = true;
        return true;
    }

    private static bool TryGetSound(string id, out FMOD.Sound snd)
    {
        if (_loaded.TryGetValue(id, out snd)) return true;
        if (!_files.TryGetValue(id, out string path))
        { WarnOnce(id, $"'{id}' is not in any pack's \"sounds\" map."); return false; }
        if (!File.Exists(path))
        { WarnOnce(id, $"'{id}': file not found: {path}"); return false; }
        // 3D + compressed-in-memory sample (oggs/mp3s stay small; wavs fall back to a plain sample).
        FMOD.RESULT r = _core.createSound(path, FMOD.MODE._3D | FMOD.MODE.CREATECOMPRESSEDSAMPLE | FMOD.MODE.LOOP_OFF, out snd);
        if (r != FMOD.RESULT.OK)
        { WarnOnce(id, $"'{id}': FMOD couldn't load '{Path.GetFileName(path)}': {r}"); return false; }
        snd.set3DMinMaxDistance(MinDistance3D, MaxDistance3D);
        _loaded[id] = snd;
        return true;
    }

    private static void WarnOnce(string id, string msg)
    {
        if (_warned.Add(id)) Plugin.Log.LogWarning("Content Forge sounds: " + msg);
    }
}

// The 3D chokepoint: the PRIVATE 6-arg overload every 3D one-shot funnels through (public overloads,
// EmitSoundOneShot, castSound). Resolved by name + exact arg list to pick it over the public 4-arg one.
// FMOD event parameters (parameterName/Value) don't exist on raw files, so they're ignored for cf ids.
[HarmonyPatch]
internal static class RawSound3DPatch
{
    private static MethodBase TargetMethod()
        => AccessTools.Method(AccessTools.TypeByName("Candide.Sound.SoundPlayer"), "Play3DEventOneShot",
            new[] { typeof(string), typeof(Vector3), typeof(string), typeof(float), typeof(float), typeof(float) });

    [HarmonyPrefix]
    private static bool Prefix(string eventName, Vector3 sourceRegularPosition, float volume, float pitch)
    {
        if (!SoundsFeature.Owns(eventName)) return true;
        if (volume > 0f) SoundsFeature.Play3D(eventName, sourceRegularPosition, volume, pitch);
        return false;   // never let cf ids reach EventDescriptionCache.GetValidated (throws on unknown ids)
    }
}

// The 2D chokepoint (UI clicks, chat pings, menu stingers).
[HarmonyPatch]
internal static class RawSound2DPatch
{
    private static MethodBase TargetMethod()
        => AccessTools.Method(AccessTools.TypeByName("Candide.Sound.SoundPlayer"), "PlayEventOneShot",
            new[] { typeof(string), typeof(float), typeof(float) });

    [HarmonyPrefix]
    private static bool Prefix(string eventName, float volume, float pitch)
    {
        if (!SoundsFeature.Owns(eventName)) return true;
        if (volume > 0f) SoundsFeature.Play2D(eventName, volume, pitch);
        return false;
    }
}

// SoundPlayer.UnloadAndRelease ends with StudioSystem.release(), which invalidates every core sound we
// created. Prefix (not postfix) so our handles are freed while the system is still alive.
[HarmonyPatch]
internal static class RawSoundUnloadPatch
{
    private static MethodBase TargetMethod()
        => AccessTools.Method(AccessTools.TypeByName("Candide.Sound.SoundPlayer"), "UnloadAndRelease");

    [HarmonyPrefix]
    private static void Prefix()
    {
        try { SoundsFeature.ReleaseAll(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Content Forge sounds: release failed: {e.Message}"); }
    }
}
