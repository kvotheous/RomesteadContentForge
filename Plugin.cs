using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Candide;
using Candide.Data;
using Candide.Database;
using Candide.GameModels;
using Candide.GameModels.Helpers;
using Candide.GameModels.Managers;
using Candide.Graphics.VFX;
using Candide.Multiplayer;
using CandideCreator.Shared;
using CandideCreator.Shared.Graphics;
using CandideCreator.Shared.Models.SpritesAnimationCollections;
using CandideCreator.Shared.Models.VFX;
using FMOD.Studio;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Shared.Entity;
using Shared.Entity.Base;          // AbstractController (creature AI swap)
using System.Globalization;
using Microsoft.Xna.Framework.Graphics;
using Shared.Combat;
using Shared.Combat.Projectiles;
using Shared.Combat.Spells;
using Shared.Combat.Spells.Parameters;
using Shared;
using Shared.Data.DataModels;
using Shared.Aura.Args;
using Shared.Models.DropTables;
using CandideServer.Database;
using CandideServer;
using CandideServer.ServerManagers;
using CandideServer.ServerControllers;
using CandideServer.ServerSystems;
using CandideServer.ServerServices;
using CandideServer.Entities;
using Candide.Database.Doodad;
using Candide.Entities.Bridge;
using CandideServer.MessageModels;
using CandideServer.MessageModels.Inventories;
using Shared.Models.Construction;
using Shared.Models.Player;
using Shared.Models.Skill;
using Shared.Models.Shops;
using CandideCreator.Shared.Serialization;
using Shared.Entity.Components;
using Shared.Data;
using Shared.Helpers;
using Shared.Models.Items;
using Shared.Models.Spells;
using Shared.Models.Stats;
using Shared.Models.Quests;
using Shared.Models.Quests.Objectives;
using Shared.Text;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RomesteadContentForge;

// Romestead Content Forge (v0.1) — injects custom items defined in JSON content packs into the game's
// item database. The game is data-driven: SharedDataSetup.Setup() fills the databases at load (and again
// on world reload after ClearSharedDataBases), so we postfix it and append our own entries each time.
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static string PacksDir;

    public override void Load()
    {
        Log = base.Log;
        PacksDir = Path.Combine(Paths.ConfigPath, "RomesteadContentForge", "packs");
        Directory.CreateDirectory(PacksDir);
        ContentLoader.WriteSampleIfEmpty(PacksDir);

        ForgeConfig.Bind(Config); // [Notifications] banner/toast style + banner color/position/duration
        StationsFeature.Init();   // register the forge_station controller preset
        CreatureAiFeature.Init(); // register the forge_guard/forge_coward/forge_ranged creature-AI presets
        CreatureAiPresetsFeature.Init(); // register forge_patrol/forge_pack/forge_summoner/forge_healer/forge_turret
        CompositeFeature.Init();  // register the forge_composite controller pair (multi-part placeables)
        var h = new Harmony(MyPluginInfo.PLUGIN_GUID);
        h.PatchAll(typeof(SetupPatch));
        h.PatchAll(typeof(LocalizationPatch));
        h.PatchAll(typeof(WeaponTrailTintPatch));
        h.PatchAll(typeof(SkinSetupPatch));
        h.PatchAll(typeof(OnUseDoubleCastPatch));
        h.PatchAll(typeof(SoundBankPatch));
        h.PatchAll(typeof(DropTablesPatch));
        h.PatchAll(typeof(SummonFactionPatch));
        h.PatchAll(typeof(SummonClientSpawnFactionPatch));
        h.PatchAll(typeof(SummonCastFactionPatch));
        h.PatchAll(typeof(CompanionPatch));
        h.PatchAll(typeof(PlaceableRecipePatch));
        h.PatchAll(typeof(CreatureServerPatch));
        h.PatchAll(typeof(SummonOwnerStampPatch));   // CreatureFixes: cf-owner stamp for summon kill attribution
        h.PatchAll(typeof(CreatureDropsPatch));      // CreatureFixes: drops[].creature -> prototype drop component
        h.PatchAll(typeof(CreatureClientPatch));
        h.PatchAll(typeof(NpcBodyServerPatch));
        h.PatchAll(typeof(NpcBodyClientPatch));
        h.PatchAll(typeof(KnownRecipePatch));
        h.PatchAll(typeof(SummonProtectPatch));
        h.PatchAll(typeof(UnequipDismissPatch));
        h.PatchAll(typeof(SummonClientProtectPatch));
        h.PatchAll(typeof(SummonDamageMessagePatch));
        h.PatchAll(typeof(MapFilesPatch));
        h.PatchAll(typeof(MapRoomPatch));
        h.PatchAll(typeof(MapPoiPatch));
        h.PatchAll(typeof(RetroPoiPatch));
        h.PatchAll(typeof(HitVfxPatch));
        h.PatchAll(typeof(ParticleLoadPatch));
        h.PatchAll(typeof(CropServerPatch));
        h.PatchAll(typeof(CropClientPatch));
        h.PatchAll(typeof(InteractiveControllerMapServerPatch));
        h.PatchAll(typeof(InteractiveControllerMapClientPatch));
        h.PatchAll(typeof(InteractiveServerPatch));
        h.PatchAll(typeof(InteractiveClientPatch));
        h.PatchAll(typeof(NpcFilesPatch));
        h.PatchAll(typeof(NpcStringsPatch));
        h.PatchAll(typeof(ForgeYarnCommandsPatch));
        h.PatchAll(typeof(IndestructiblePatch));
        h.PatchAll(typeof(SummonRestorePatch));
        h.PatchAll(typeof(SkillBackfillPatch));
        h.PatchAll(typeof(SkillGrantGuardPatch));
        h.PatchAll(typeof(SpellRequiresPatch));
        h.PatchAll(typeof(PvpCastSpellPatch));
        h.PatchAll(typeof(PvpClientHitPatch));
        h.PatchAll(typeof(PvpGravePatch));
        h.PatchAll(typeof(PvpDamageGatePatch));
        h.PatchAll(typeof(PvpDeathEventPatch));
        h.PatchAll(typeof(PvpExactKillPatch));
        h.PatchAll(typeof(HandshakeServerPatch));
        h.PatchAll(typeof(HandshakeClientPatch));
        h.PatchAll(typeof(WerewolfRespawnPatch));     // Werewolf: ghost respawn -> spectator pen
        h.PatchAll(typeof(WerewolfGhostChatPatch));   // Werewolf: dead players chat only with the dead
        h.PatchAll(typeof(WerewolfClientHookPatch));  // Werewolf: client cf-ww-* subscriptions on join
        h.PatchAll(typeof(WerewolfClientPumpPatch));  // Werewolf: client inbox pump (UI thread)
        h.PatchAll(typeof(AttackTuningPatch));
        h.PatchAll(typeof(RawSound3DPatch));
        h.PatchAll(typeof(RawSound2DPatch));
        h.PatchAll(typeof(RawSoundUnloadPatch));
        h.PatchAll(typeof(PlayerRigPatch));
        h.PatchAll(typeof(FavourStripOnSavePatch));
        h.PatchAll(typeof(FavourRegrantPatch));
        h.PatchAll(typeof(AtmosphereTracksPatch));
        h.PatchAll(typeof(NpcBodyRetintPatch));
        h.PatchAll(typeof(EventsPlusKillPatch));
        h.PatchAll(typeof(QuestRewardInjectPatch));
        h.PatchAll(typeof(ComboTuningPatch));
        h.PatchAll(typeof(SpacForgePatch));
        h.PatchAll(typeof(ScratchSpacPatch));
        h.PatchAll(typeof(PlayerAnimRemixPatch));
        h.PatchAll(typeof(WorkRecipeGroupsPatch));
        h.PatchAll(typeof(CitizenDataBasePatch));
        h.PatchAll(typeof(CitizenCreatePatch));
        h.PatchAll(typeof(CitizenWildRecruitPatch));
        h.PatchAll(typeof(CitizenSpawnParamPatch));
        h.PatchAll(typeof(CitizenClientLookPatch));
        h.PatchAll(typeof(StationSetupPatch));
        h.PatchAll(typeof(StationStringsPatch));
        h.PatchAll(typeof(TwoHandedEnforcePatch));
        h.PatchAll(typeof(TwoHandedLoginPatch));
        h.PatchAll(typeof(DoodadServerPatch));
        h.PatchAll(typeof(DoodadClientPatch));
        h.PatchAll(typeof(TiledObjectsServerDbPatch));
        h.PatchAll(typeof(TiledObjectsClientDbPatch));
        h.PatchAll(typeof(ForgeTabPatch));
        h.PatchAll(typeof(ForgeFavourScreenTabPatch));   // "Pack Favours" button -> custom favour screen
        h.PatchAll(typeof(BannerPumpPatch));             // center-screen announcement banners
        h.PatchAll(typeof(ChatToastPatch));          // Toasts: event chat lines also pop top-screen notifications
        h.PatchAll(typeof(CreatureAnimClientMapPatch));
        h.PatchAll(typeof(CreatureAiControllerMapPatch));
        h.PatchAll(typeof(CreatureAiPresetsControllerMapPatch));
        h.PatchAll(typeof(CompositeServerPatch));    // AFTER InteractiveServerPatch: postfix order = patch order
        h.PatchAll(typeof(CompositeClientPatch));    // (the composite pass edits the parent proto Interactive registers)
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded. Packs: {PacksDir}");
    }
}

// Inject items after every Setup() — the item database is cleared and rebuilt on world reload, which
// would otherwise drop our entries. AddItems just sets DataMap[id], so re-running is idempotent.
[HarmonyPatch(typeof(SharedDataSetup), nameof(SharedDataSetup.Setup))]
internal static class SetupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            ContentLoader.ApplyIcons();     // register custom icons before items reference them
            ContentLoader.ApplyDisplayData(); // register composed held looks
            ContentLoader.InjectSpells();   // register spells before scrolls/tomes reference them
            ContentLoader.InjectItems();
            ContentLoader.InjectRecipes();  // recipes reference crafted item ids
            ContentLoader.InjectClasses();  // playable classes (character creation)
            ContentLoader.InjectQuests();   // world quests
            ContentLoader.InjectConstructions(); // placeable furniture
            TownBuildingsFeature.Register();     // town job-buildings + work recipes (after vanilla buildings exist)
            ContentLoader.InjectShops();    // market stock
            ContentLoader.InjectSkills();   // new player skills
            FavoursFeature.Register();      // favour-tree nodes (FavourDataBase + grid are rebuilt each Setup)
            CitizensFeature.Register();     // citizen types (CitizenDataBase persists across reloads; re-apply is idempotent)
            ContentLoader.ApplyStrings();   // in case localization already loaded
        }
        catch (Exception e) { Plugin.Log.LogError($"Item injection failed: {e}"); }
    }
}

// Re-apply our custom strings after the game (re)loads a language. SetLocalization clears
// StringDefinitions.Strings and repopulates it from the language file, which drops our keys — so we
// must add them back here. Runs at startup and on every in-game language change.
[HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.SetLocalization))]
internal static class LocalizationPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            ContentLoader.ApplyStrings();
        }
        catch (Exception e) { Plugin.Log.LogError($"String injection failed: {e}"); }
    }
}

// Inject custom held-weapon skins after the skin system builds its registry (it (re)creates Parts here).
[HarmonyPatch(typeof(PlayerSkinManager), nameof(PlayerSkinManager.Setup))]
internal static class SkinSetupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try { ContentLoader.ApplyPalettes(); ContentLoader.ApplyVfx(); ContentLoader.ApplyCustomSkins(); PlayerSkinsFeature.Apply(); }
        catch (Exception e) { Plugin.Log.LogError($"Custom skin/palette/vfx inject failed: {e}"); }
    }
}

// Customises a weapon's swing-trail VFX for the local player's equipped Content Forge weapon. The slash
// VFX is hardcoded per weapon class (vfx_swordslash etc.) with TintColor = white; we can change two things:
//   - trailColor: override args.TintColor (recolour the vanilla slash shape).
//   - trailSheet: swap spacId to a clone of the vanilla spac whose sprite sheet is the author's repaint
//     (custom slash SHAPE). The clone reuses the vanilla animation timing/offsets/direction data, so any
//     attack variant (slash/slash2/whirlwind/stab/…) just works. Built lazily per (weapon, base spac).
// spacId and SpacRenderingArgs (a struct) are taken by ref so we can swap/tint them.
[HarmonyPatch]
internal static class WeaponTrailTintPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(VfxPlayer), nameof(VfxPlayer.PlaySpacAnimation),
        new[] { typeof(string), typeof(string), typeof(EntityWrapper), typeof(SpacRenderingArgs), typeof(float) })]
    private static bool Prefix(ref string spacId, EntityWrapper target, ref SpacRenderingArgs args)
    {
        try
        {
            if (spacId == null || !spacId.StartsWith("vfx_")) return true;
            if (ContentLoader.TrailColors.Count == 0 && ContentLoader.TrailSheets.Count == 0
                && ContentLoader.HideTrails.Count == 0) return true;
            if (!PlayerHelper.IsLocalPlayer(target)) return true;
            string id = PlayerEquipmentHelper.GetEquippedItemForType(EquipmentType.Weapon)?.Data?.Id;
            if (id == null) return true;
            if (ContentLoader.HideTrails.Contains(id)) return false;   // staff/wand: no swing trail at all
            if (ContentLoader.TrailColors.TryGetValue(id, out Vector4 col)) args.TintColor = col;
            if (ContentLoader.TrailSheets.ContainsKey(id)) spacId = ContentLoader.EnsureTrailSpac(id, spacId);
        }
        catch { }
        return true;
    }
}

// An On-Use weapon (staff) fires its projectile twice per click: the player state machine enters the attack
// state twice — once directly (TrySetState) and once 0.2s later from a buffered input transition
// (DoBufferedTransitions) — so the On-Use cast runs twice. We de-dupe: for our On-Use spell ids, drop a second
// cast by the same caster within 0.3s of the first. The buffered repeat is always ~0.2s out (dropped); the
// weapon's swing timer (≥0.4s, the firestaff's is 0.6s) gates real re-fires well outside the window (kept).
[HarmonyPatch]
internal static class OnUseDoubleCastPatch
{
    private const double WindowSeconds = 0.3;
    private static readonly Dictionary<string, double> _lastCast = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SpellManager), nameof(SpellManager.CastSpell))]
    private static bool Prefix(string spellId, EntityWrapper castingEntity)
    {
        if (spellId == null || !ContentLoader.OnUseSpellIds.Contains(spellId)) return true;
        try
        {
            double now = Globals.GameTime.TotalGameTime.TotalSeconds;
            string key = (castingEntity != null ? castingEntity.Id.ToString() : "?") + ":" + spellId;
            if (_lastCast.TryGetValue(key, out double t) && now - t < WindowSeconds) return false; // buffered repeat — drop
            _lastCast[key] = now;
        }
        catch { }
        return true;
    }
}

// Load custom FMOD .bank files after the game finishes building its audio system (SoundPlayer.Initialize).
// SoundPlayer is internal, so the target method is resolved by name.
[HarmonyPatch]
internal static class SoundBankPatch
{
    private static System.Reflection.MethodBase TargetMethod()
        => AccessTools.Method("Candide.Sound.SoundPlayer:Initialize");

    [HarmonyPostfix]
    private static void Postfix()
    {
        try { ContentLoader.LoadSoundBanks(); }
        catch (Exception e) { Plugin.Log.LogError($"Sound bank load failed: {e}"); }
    }
}

// The spawn chokepoint for summons. Does three things for any entity carrying our summon params:
//   1) faction — join the summoner's faction (the game only does this for ServerJumpingSkullController, so other
//      creatures would spawn hostile and attack the player; we generalize it to every summon).
//   2) count   — spawn the rest (summon.count) here, so it works regardless of cast path (tome or on-use weapon).
//   3) cap     — keep at most summon.maxActive of a summon alive; the oldest is despawned when a new one spawns.
[HarmonyPatch]
internal static class SummonFactionPatch
{
    [System.ThreadStatic] private static bool _busy;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.SpawnEntity))]
    private static void Postfix(SpawnEntityArgs args, ServerEntityModel __result)
    {
        try
        {
            if (__result == null || args?.Parameters == null) return;

            // ours? (the cf-summon marker carries the summon spell's id)
            bool isOurs = args.Parameters.TryGetValue("cf-summon", out string spellId);
            // ALLY summons (entityType PlayerSummon/Friendly) join the summoner's faction. Hostile-intent
            // summons (entityType Hostile etc.) keep their base creature's faction — the game stamps
            // summoner-faction on EVERY summon, but applying it to a player-cast HOSTILE summon would make
            // it Player faction: unhittable by the player and blind to him in the threat system.
            bool ally = !isOurs || ContentLoader.AllySummonSpells.Contains(spellId);
            if (ally && args.Parameters.TryGetValue("summoner-faction", out string fs)
                && Enum.TryParse(fs, ignoreCase: true, out FactionMask fm) && fm != FactionMask.None)
            {
                var w = __result.EntityWrapper;
                if (w != null) w.FactionMask = fm;
            }

            if (!isOurs)
            {
                // No marker — but a skeleton's death spawns a JumpingSkull REUSING the dead skeleton's Guid
                // (hostile, empty params). If that Guid was one of our ALLY summons, keep the skull on our side.
                if (ContentLoader.AllySummons.Contains(__result.Id))
                {
                    var sw = __result.EntityWrapper;
                    if (sw != null) sw.FactionMask = FactionMask.Player;
                }
                return;
            }

            ContentLoader.AllSummons.Add(__result.Id);
            if (ally) ContentLoader.AllySummons.Add(__result.Id);

            // GROUND THE SUMMON. Summon spells spawn at the spell's CAST height (Z ≈ 10) and creatures
            // carry no Movable component, so gravity never lands them — and collision only applies when
            // Z-intervals overlap (IntervalOverlap, sh:82330), so a summon floating at Z=10 clears every
            // wall/rock collision band and walks straight through the world. Snap it to the floor once.
            try
            {
                var gw = __result.EntityWrapper;
                if (gw != null && gw.Position.Z > 0.01f)
                {
                    gw.System.UpdateFloorHeight(gw.Eid);
                    var gp = gw.Position;
                    gp.Z = gw.System.General[gw.Eid].FloorHeight;
                    gw.Position = gp;
                }
            }
            catch (Exception ge) { Plugin.Log.LogWarning($"summon grounding failed: {ge.Message}"); }

            // companion? remember it so CompanionPatch teleport-leashes it to the player
            if (ContentLoader.CompanionSpells.Contains(spellId)) ContentLoader.CompanionSummons.Add(__result.Id);

            // 3) cap — keep at most `cap` ALIVE; despawn the oldest. (Skeletons that died in combat must be
            // pruned from the tally, or the cap counts ghosts and the living count grows without limit.)
            // Also tracked (without a cap) for dismiss-on-unequip spells, so UnequipDismissPatch can find them.
            bool capped = ContentLoader.SummonCaps.TryGetValue(spellId, out int cap) && cap > 0;
            if (capped || ContentLoader.DismissSpells.ContainsKey(spellId))
            {
                var q = ContentLoader.ActiveSummons(spellId);
                q.Enqueue(__result.Id);
                var alive = q.Where(id => ServerGameState.Entities.ContainsKey(id)).ToList();
                while (capped && alive.Count > cap)
                {
                    EntityServerManager.RemoveEntity(alive[0], new EntityRemoveInfo { RemoveType = EntityRemoveType.Unloaded });
                    alive.RemoveAt(0);
                }
                q.Clear();
                foreach (var id in alive) q.Enqueue(id);
            }

            // 2) count — spawn the remaining copies (guarded so the extras don't recurse the count)
            if (!_busy && ContentLoader.SummonCounts.TryGetValue(spellId, out int count) && count > 1)
            {
                _busy = true;
                try
                {
                    for (int i = 1; i < count; i++)
                        EntityServerManager.SpawnEntity(new SpawnEntityArgs
                        {
                            Id = Guid.NewGuid(), BaseId = args.BaseId, WorldId = args.WorldId,
                            Position = args.Position + new Vector3(i * 0.7f, 0f, 0f),
                            Velocity = args.Velocity, Direction = args.Direction,
                            Parameters = args.Parameters, EntityType = args.EntityType, DynamicSpawn = args.DynamicSpawn,
                        });
                }
                finally { _busy = false; }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon post-spawn failed: {e.Message}"); }
    }
}

// Ally summons join the player's faction on the CLIENT too. The game syncs summoner-faction only as a
// controller parameter, and vanilla applies it client-side solely in JumpingSkullController — every other
// summon keeps its base creature's faction on clients. Projectile hit detection runs CLIENT-side against
// client factions, so an ally skeleton (client faction still Undead) firing an arrow THROUGH its owner
// (Player) registers a valid hit: your own summons damage you. Two chokepoints fix every path:
//   1) the network entity-spawn funnel (a remote co-op client receiving the summon),
//   2) the CastSpell constructor as a lazy self-heal (the host's local entity systems, and summons loaded
//      from a save, never pass the funnel — but the faction is corrected before any of their casts can hit).
internal static class SummonAllyFactionSync
{
    internal static void Apply(EntityWrapper w)
    {
        try
        {
            var p = w?.Controller?.Parameters;
            string spell = p?.GetString("cf-summon", null);
            if (string.IsNullOrEmpty(spell) || !ContentLoader.AllySummonSpells.Contains(spell)) return;
            FactionMask fm = p.GetEnum("summoner-faction", FactionMask.None);
            if (fm != FactionMask.None && w.FactionMask != fm) w.FactionMask = fm;
        }
        catch { }
    }
}

internal static class SummonClientSpawnFactionPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Candide.GameModels.Controllers.EntityController),
        nameof(Candide.GameModels.Controllers.EntityController.SpawnEntity), new[] { typeof(ServerEntityModel) })]
    private static void Postfix(EntityWrapper __result) => SummonAllyFactionSync.Apply(__result);
}

internal static class SummonCastFactionPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Shared.Combat.Spells.Casting.CastSpell), MethodType.Constructor, new[] {
        typeof(ProjectileSystem), typeof(EntityWrapper), typeof(Shared.Combat.Spells.Casting.SpellCastingContext),
        typeof(Shared.Combat.Projectiles.Models.SpellData), typeof(CastSpellArgs), typeof(DamageSourceType),
        typeof(Action<Shared.Combat.Projectiles.Models.ProjectileHitData, Shared.Combat.Spells.Casting.CastSpell>),
        typeof(Shared.Combat.Spells.Casting.CastSpell.OnUpdateDelegate), typeof(ProjectileHitFlags) })]
    private static void Prefix(EntityWrapper owner) => SummonAllyFactionSync.Apply(owner);
}

// "Companion" summons teleport back to you when they have no enemy nearby and you've wandered off. No continuous
// walk-steering (that fought the creature's own animation state machine = the "thrashing"); just a clean blink to
// your side when idle. Their own AI fights whatever is near them — which, after a teleport, is whatever's near YOU.
[HarmonyPatch]
internal static class CompanionPatch
{
    private const float HuntR2 = 280f * 280f;   // if an enemy is this close, leave the summon to fight (don't teleport)
    private const float LeashR2 = 340f * 340f;  // teleport to you only once you're at least this far away
    private const float PlaceDist = 48f;        // how far to your side it reappears

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityServerSystem), nameof(EntityServerSystem.Update))]
    private static void Postfix(float dt)
    {
        EventsFeature.Tick(dt);   // arena matches share the server tick (internally guarded; no-op with 0 arenas)
        EventsPlusFeature.Tick(dt);   // wave/boss/hunt/raid events share the server tick (no-op with 0 events)
        EventsCompetitiveFeature.Tick(dt);  // teamBattle/koth/race share the server tick (no-op with 0 events)
        WerewolfFeature.Tick(dt);  // werewolf social-deduction matches share the server tick (no-op with 0 events)
        var set = ContentLoader.CompanionSummons;
        if (set.Count == 0) return;
        try
        {
            // The player to leash to (first player found — single-player has one).
            ServerEntityModel player = null;
            foreach (var kv in ServerGameState.Entities)
                if (kv.Value.EntityWrapper != null && PlayerServerManager.TryGetPlayerFromEntity(kv.Value.EntityWrapper, out _))
                { player = kv.Value; break; }
            if (player == null) return;
            Vector2 pPos = player.EntityWrapper.Position2;
            FactionMask pFaction = player.EntityWrapper.FactionMask;
            Guid world = player.WorldId;

            // Gather this player's live companions (and drop dead ones); note if any has strayed past the leash.
            var comps = new List<ServerEntityModel>();
            List<Guid> dead = null;
            bool anyFar = false;
            foreach (var guid in set)
            {
                if (!ServerGameState.Entities.TryGetValue(guid, out var sem) || sem.EntityWrapper == null)
                { (dead ??= new List<Guid>()).Add(guid); continue; }
                if (sem.WorldId != world) continue;
                comps.Add(sem);
                if (Vector2.DistanceSquared(sem.EntityWrapper.Position2, pPos) > LeashR2) anyFar = true;
            }
            if (dead != null) foreach (var g in dead) set.Remove(g);
            if (!anyFar || comps.Count == 0) return;

            // Don't yank them off a fight: if any enemy is near YOU, let them keep fighting where they are.
            foreach (var kv in ServerGameState.Entities)
            {
                var ts = kv.Value; if (ts.WorldId != world) continue;
                var tw = ts.EntityWrapper;
                if (tw == null || !tw.Mask.HasFlags(Component.Faction) || !tw.Mask.HasFlags(Component.Health)) continue;
                if (!tw.FactionMask.HasAnyFlags(pFaction) && Vector2.DistanceSquared(pPos, tw.Position2) < HuntR2)
                    return;
            }

            // Blink them ALL at once, in a ring around you. (Facing is left to the creature's own AI
            // controller — forcing Direction here just gets overwritten next tick and fights its animation.)
            int n = comps.Count;
            for (int i = 0; i < n; i++)
            {
                var me = comps[i].EntityWrapper;
                float ang = (n == 1) ? 0f : i * (MathHelper.TwoPi / n);
                var to = player.Position + new Vector3(MathF.Cos(ang) * PlaceDist, MathF.Sin(ang) * PlaceDist, 0f);
                me.Velocity = Vector3.Zero;
                EntitySController.UpdateEntityPosition(comps[i], to);
                CreatureFixes.FaceToward(me, player.EntityWrapper);   // face the player once at the blink (replicated per tick)
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Companion teleport failed: {e.Message}"); }
    }
}

// Auto-unlock our furniture build-recipes. The login response snapshots ServerRunState.UnlockedConstructionRecipes
// (ConstructionRecipes = ...ToArray()), so we add our ids in a PREFIX — before that snapshot is taken — to make the
// furniture appear in every player's build menu without an in-game unlock step.
internal static class PlaceableRecipePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ServerGameStateService), nameof(ServerGameStateService.GetFullGameStateForLogin))]
    private static void Prefix()
    {
        try
        {
            foreach (string id in ContentLoader.PlaceableRecipeIds)
                if (!ServerRunState.UnlockedConstructionRecipes.Contains(id))
                    ConstructionRecipesServerManager.AddRecipe(id, sendToNetwork: false);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Furniture recipe unlock failed: {e.Message}"); }
    }
}

// dismissOnUnequip: every server-side inventory mutation funnels through Send_NEWInventoryChanged. When a
// player's EQUIPMENT inventory changes, re-check each dismiss-enabled summon spell — if none of the items that
// cast it remain equipped, despawn its summons. (Weapon-set swaps physically move items between the main and
// secondary equipment inventories, so swapping away also dismisses — "active set" is always the main one.)
internal static class UnequipDismissPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SimpleInventoryServerService), nameof(SimpleInventoryServerService.Send_NEWInventoryChanged))]
    private static void Postfix(InventoryListenerUpdate update)
    {
        try
        {
            if (ContentLoader.DismissSpells.Count == 0) return;
            if (!ServerGameState.TryGetInventory(update.InventoryId, out SharedInventoryModel inv)) return;
            if (inv.InventoryType != InventoryType.Equipment) return;

            foreach (var kv in ContentLoader.DismissSpells)
            {
                bool equipped = inv.InventorySlots.Any(s => s != null && kv.Value.Contains(s.BaseDataId));
                if (equipped) continue;
                var q = ContentLoader.ActiveSummons(kv.Key);
                while (q.Count > 0)
                {
                    Guid id = q.Dequeue();
                    ContentLoader.AllSummons.Remove(id);
                    ContentLoader.AllySummons.Remove(id);
                    ContentLoader.CompanionSummons.Remove(id);
                    if (ServerGameState.Entities.ContainsKey(id))
                        EntityServerManager.RemoveEntity(id, new EntityRemoveInfo { RemoveType = EntityRemoveType.Unloaded });
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Unequip dismiss failed: {e.Message}"); }
    }
}

// Suppress the client-side PREDICTED hit on your own summons (the floating damage number, the local health
// drop, hit sounds): the client calculates and displays damage before the server ever sees it, so the server
// guard alone leaves cosmetic numbers. IgnoreDoDamage aborts the whole client-side hit at the source.
internal static class SummonClientProtectPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClientEntitySystemBridge), nameof(ClientEntitySystemBridge.IgnoreDoDamage))]
    private static void Postfix(DamageInfo damageInfo, ref bool __result)
    {
        try
        {
            if (__result || damageInfo?.Victim == null) return;
            if (ContentLoader.AllySummons.Contains(damageInfo.Victim.Id) && PlayerHelper.IsLocalPlayer(damageInfo.SourceEntity))
                __result = true;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon client protect failed: {e.Message}"); }
    }
}

// Spell casting requirements: gate the player's weapon/tome casts of a pack spell behind skill level,
// favour, finished quest, or class. CastPlayerWeaponSpell is the player's casting entry (chains and enemy
// casts go through CastSpell and are deliberately NOT gated — the parent cast already was).
internal static class SpellRequiresPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SpellManager), nameof(SpellManager.CastPlayerWeaponSpell))]
    private static bool Prefix(SpellDataModel spell)
    {
        try
        {
            if (spell == null || !ContentLoader.SpellRequirements.TryGetValue(spell.Id, out SpellRequiresDef req)) return true;
            var ch = GameState.LocalPlayer?.Character?.Character;
            if (ch == null) return true;

            string deny = null;
            if (deny == null && !string.IsNullOrWhiteSpace(req.Skill))
            {
                ch.Skills.CharacterSkills.TryGetValue(req.Skill, out var cs);
                if (cs == null || cs.Level < req.SkillLevel)
                    deny = $"Requires {StringDefinitions.GetString(req.Skill + "*skills:name")} {req.SkillLevel}";
            }
            if (deny == null && !string.IsNullOrWhiteSpace(req.Favour) && !ch.Skills.Favours.ContainsKey(req.Favour))
                deny = "Requires a favour you have not learned";
            if (deny == null && !string.IsNullOrWhiteSpace(req.Quest)
                && !GameState.FinishedWorldQuests.Contains(req.Quest) && !ch.FinishedPersonalQuests.Contains(req.Quest))
                deny = "Requires an unfinished deed";
            if (deny == null && !string.IsNullOrWhiteSpace(req.Class))
            {
                int idx = 0, want = -1;
                foreach (string cid in PlayerClassDataBase.DataMap.Keys)
                { if (cid == req.Class) { want = idx; break; } idx++; }
                if (want < 0 || ch.Appearance.Class != want)
                    deny = "Your calling cannot wield this";
            }
            if (deny == null) return true;

            if (!string.IsNullOrWhiteSpace(req.Message)) deny = req.Message;
            Guid meId = GameState.LocalPlayer?.EntityId ?? Guid.Empty;
            if (meId != Guid.Empty && GameState.Entities.TryGetValue(meId, out EntityWrapper meEnt) && meEnt != null)
                Candide.Graphics.FloatingTextSystem.AddNewFloatText(deny, meEnt.Position2, Color.OrangeRed, "cf-req", 1, 0.5f);
            return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Spell requirement check failed: {e.Message}"); return true; }
    }
}

// Custom skills exist on a CHARACTER only if its skill dictionary contains them — vanilla fills it once at
// character creation, so EXISTING characters never get new skills (and the XP-grant API crashes on a missing
// key). Backfill on the server character setup (runs for every joining player incl. the host; the model is
// what the client receives) and defensively before any XP grant.
internal static class SkillBackfillPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCharacterServerManager), nameof(PlayerCharacterServerManager.SetupCharacter))]
    private static void Postfix(PlayerCharacterModel playerCharacter)
    {
        try
        {
            foreach (string id in ContentLoader.CustomSkillIds)
                playerCharacter?.Skills.CharacterSkills?.TryAdd(id, new CharacterSkill(id));
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Skill backfill failed: {e.Message}"); }
    }
}

internal static class SkillGrantGuardPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SkillsManager), nameof(SkillsManager.AddExperienceToSkillType))]
    private static void Prefix(PlayerCharacterModel playerCharacter, string skillId)
    {
        try
        {
            if (!string.IsNullOrEmpty(skillId) && ContentLoader.CustomSkillIds.Contains(skillId))
                playerCharacter?.Skills.CharacterSkills?.TryAdd(skillId, new CharacterSkill(skillId));
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Skill grant guard failed: {e.Message}"); }
    }
}

// Weapon hitVfx: play a pack-defined VFX at the hit position when one of our weapons lands a hit. The game
// derives impact VFX from damage-type enums only (no per-weapon id exists), so we resolve the attacker's
// equipped weapon client-side and play additively through the same VfxPlayer calls vanilla uses. Works for
// remote players' hits too (this method also runs for network-relayed damage).
internal static class HitVfxPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClientEntitySystemBridge), nameof(ClientEntitySystemBridge.OnEntityTookDamage))]
    private static void Postfix(EntityWrapper entity, DamageInfo damageInfo, bool countAsHit)
    {
        try
        {
            if (!countAsHit || ContentLoader.HitVfxByItemId.Count == 0) return;
            if (damageInfo?.SourceEntity == null || damageInfo.DamageSourceType != DamageSourceType.Impact) return;

            var slots = PlayerHelper.GetPlayerEquipment(damageInfo.SourceEntity)?.Model?.InventorySlots;
            if (slots == null) return;
            foreach (var slot in slots)
            {
                if (slot == null || !ContentLoader.HitVfxByItemId.TryGetValue(slot.BaseDataId, out var fx)) continue;
                Vector3 pos = HitPositionHelper.GetHitPosition(entity, damageInfo);
                var sprites = VfxSpritesAnimationDataBase.GetVfxSpritesAnimationOrNull(fx.Vfx);
                if (sprites != null) { VfxPlayer.PlayAnimation(sprites, pos, 0f, fx.Scale); return; }
                var spac = VfxSpacAnimationDataBase.GetAnimationOrNull(fx.Vfx);
                if (spac != null) { VfxPlayer.PlaySpacAnimation(spac, pos, 0f); return; }
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"hitVfx failed: {e.Message}"); }
    }
}

// Co-op hardening: the server's DamageEntity also BROADCASTS the hit to other clients even when ApplyDamage is
// blocked, so without this gate other players would still see damage on someone's summons. Swallow the whole
// message when a player damages any mod summon.
internal static class SummonDamageMessagePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.DamageEntity))]
    private static bool Prefix(DealDamageMessage message)
    {
        try
        {
            if (message == null || !ContentLoader.AllySummons.Contains(message.VictimId)) return true;
            if (message.OwnerId.HasValue && ServerGameState.Entities.TryGetValue(message.OwnerId.Value, out ServerEntityModel src)
                && PlayerServerManager.TryGetPlayerFromEntity(src.EntityWrapper, out _))
                return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon damage gate failed: {e.Message}"); }
        return true;
    }
}

// Summon tracking SURVIVES world reloads: companion-teleport, dismiss-on-unequip, caps and the friendly-fire
// guard all key off in-memory Guid sets that die with the process — but the summons themselves persist in the
// save (their controller Parameters, including our cf-summon marker, round-trip through it). After a world
// loads, rescan every live entity and rebuild the sets, so summons from a previous session keep behaving.
internal static class SummonRestorePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerDirectorSystem), nameof(ServerDirectorSystem.AfterWorldGameStateLoaded))]
    private static void Postfix()
    {
        try
        {
            int restored = 0;
            foreach (var kv in ServerGameState.Entities)
            {
                var p = kv.Value?.EntityWrapper?.Controller?.Parameters;
                string spell = p?.GetString("cf-summon", null);
                if (string.IsNullOrEmpty(spell)) continue;
                ContentLoader.AllSummons.Add(kv.Key);
                if (ContentLoader.AllySummonSpells.Contains(spell)) ContentLoader.AllySummons.Add(kv.Key);
                if (ContentLoader.CompanionSpells.Contains(spell)) ContentLoader.CompanionSummons.Add(kv.Key);
                if (ContentLoader.SummonCaps.ContainsKey(spell) || ContentLoader.DismissSpells.ContainsKey(spell))
                {
                    var q = ContentLoader.ActiveSummons(spell);
                    if (!q.Contains(kv.Key)) q.Enqueue(kv.Key);
                }
                restored++;
            }
            if (restored > 0) Plugin.Log.LogInfo($"Restored tracking for {restored} summon(s) from the save.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon restore failed: {e.Message}"); }
    }
}

// Friendly-fire guard: a player can't damage their own (mod-summoned) creatures. All server damage funnels
// through EntityServerManager.ApplyDamage; if the victim is one of our summons and the source resolves to a
// player, the hit is swallowed. Enemy damage (non-player source) still lands, so summons can still die in combat.
internal static class SummonProtectPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.ApplyDamage))]
    private static bool Prefix(DamageInfo damageInfo)
    {
        try
        {
            var victim = damageInfo?.Victim;
            if (victim == null || !ContentLoader.AllySummons.Contains(victim.Id)) return true;
            if (PlayerServerManager.TryGetPlayerFromEntity(damageInfo.SourceEntity, out _)) return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon protect failed: {e.Message}"); }
        return true;
    }
}

// Make our crafting recipes VISIBLE. The crafting window lists GameState.ItemRecipes (the player's known
// recipes — a hardcoded starter list built here), NOT the recipe database; a recipe only in the DB never shows.
// We append ours each time the state is built — except quest-reward recipes, which stay locked until their
// quest completes (the quest system surfaces those itself).
internal static class KnownRecipePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameState), nameof(GameState.SetupNewGameState))]
    private static void Postfix()
    {
        try
        {
            foreach (string id in ContentLoader.AutoKnownRecipeIds)
                GameState.ItemRecipes.Add(id);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Known-recipe injection failed: {e.Message}"); }
    }
}

// Register custom creatures into the SERVER entity registry, right after the vanilla doodad DB loads (so the base
// prototypes we clone are present). The server simulates the creature from here.
internal static class CreatureServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { ContentLoader.RegisterCreatures(ServerEntityDataManager.EntitySystems, ServerGlobals.Reflection); }
        catch (Exception e) { Plugin.Log.LogWarning($"Server creature registration failed: {e.Message}"); }
    }
}

// Register custom creatures into the CLIENT entity registry (renders/predicts them). Same Guid as the server side.
internal static class CreatureClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DoodadDatabaseManager), nameof(DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { ContentLoader.RegisterCreatures(DoodadDatabaseManager.EntitySystems, Globals.Reflection); }
        catch (Exception e) { Plugin.Log.LogWarning($"Client creature registration failed: {e.Message}"); }
    }
}

// Inject custom loot after the server builds its drop tables (server-authoritative; runs in-process even in
// single-player). Re-applies idempotently if the server rebuilds the tables.
[HarmonyPatch]
internal static class DropTablesPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerDropTablesDataBase), nameof(ServerDropTablesDataBase.AddDropTables))]
    private static void Postfix()
    {
        try { ContentLoader.ApplyDrops(); }
        catch (Exception e) { Plugin.Log.LogError($"Drop inject failed: {e}"); }
    }
}

internal static class ContentLoader
{
    // Weapon item id -> swing-trail tint (set by weapon.trailColor).
    public static readonly Dictionary<string, Vector4> TrailColors = new();
    // Weapon item id -> absolute path of a custom slash sheet (set by weapon.trailSheet). Spac clones are
    // built on first swing (one per base spac the weapon's class uses) and cached in the game's SpacMap.
    public static readonly Dictionary<string, string> TrailSheets = new();
    // Weapon item ids whose swing-trail VFX is suppressed entirely (set by weapon.hideTrail) — e.g. a staff.
    public static readonly HashSet<string> HideTrails = new();
    // Spell ids cast by an On-Use weapon (staff). These route through the generic SpellManager.CastSpell, which
    // double-spawns in single-player; OnUseDoubleCastPatch forces local-only casting for these.
    public static readonly HashSet<string> OnUseSpellIds = new();
    // Summon spell id -> how many creatures to spawn per cast (summon.count) and the max kept alive (summon.maxActive).
    public static readonly Dictionary<string, int> SummonCounts = new();
    public static readonly Dictionary<string, int> SummonCaps = new();
    public static readonly HashSet<string> CompanionSpells = new();   // summon spell ids that teleport-leash to you
    public static readonly HashSet<Guid> CompanionSummons = new();    // live companion entity ids
    private static readonly Dictionary<string, Queue<Guid>> _activeSummons = new();
    public static Queue<Guid> ActiveSummons(string spellId)
    {
        if (!_activeSummons.TryGetValue(spellId, out var q)) { q = new Queue<Guid>(); _activeSummons[spellId] = q; }
        return q;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Parsed-once cache. Packs are read from disk a single time (first hook to fire), then the built
    // items/strings are re-applied cheaply on each later hook (world reload re-adds items, language
    // reload re-adds strings).
    private static bool _parsed;
    private static readonly List<ItemData> _items = new();
    private static readonly Dictionary<string, string> _strings = new();   // key -> fallback text
    // key -> { languageCode -> text }. ApplyStrings picks the current game language, falling back to _strings.
    private static readonly Dictionary<string, Dictionary<string, string>> _locStrings = new();
    private static readonly Dictionary<string, string> _icons = new();   // iconId -> absolute .png path
    private static bool _iconsBuilt;
    private static readonly List<SpellDataModel> _spells = new();
    private static readonly List<ItemRecipe> _recipes = new();
    private static readonly List<PlayerClassModel> _classes = new();
    private static readonly List<QuestData> _quests = new();
    private static readonly List<ConstructionModel> _placeables = new();
    // construction ids whose build-recipe we auto-unlock on login (so the furniture shows in the build menu)
    internal static readonly List<string> PlaceableRecipeIds = new();
    private static readonly List<CreatureDef> _creatureDefs = new();
    // custom-creature id -> its stable Guid (resolved by TryResolveCreature; registered into both entity registries)
    internal static readonly Dictionary<string, Guid> CustomCreatureGuids = new();
    // recipe ids a quest rewards — these stay LOCKED until the quest completes (everything else auto-knows)
    private static readonly HashSet<string> _questRewardRecipeIds = new();
    // every entity our summon spells spawned (any behavior). Lets us re-faction the JumpingSkull the game
    // spawns when a skeleton dies (it reuses the dead skeleton's Guid), so it doesn't turn on its owner.
    internal static readonly HashSet<Guid> AllSummons = new();
    // summon spell ids whose creatures fight FOR the player (entityType PlayerSummon/Friendly). Only these
    // join the summoner's faction and get the friendly-fire protections; hostile-intent summons stay hittable.
    internal static readonly HashSet<string> AllySummonSpells = new();
    // live entity ids of ally summons (the AllySummonSpells subset of AllSummons)
    internal static readonly HashSet<Guid> AllySummons = new();
    // dismissOnUnequip: summon spell id -> the equippable item id(s) that cast it. When none of those items
    // remain in a player's equipment, the spell's summons despawn (UnequipDismissPatch).
    internal static readonly Dictionary<string, HashSet<string>> DismissSpells = new();
    // custom maps: (def, the pack's folder — where its .tmx lives). Consumed by MapsFeature.
    internal static readonly List<(MapDef Def, string PackDir)> Maps = new();
    private static readonly List<Shop> _shops = new();
    private static readonly List<ShopAddDef> _shopAdds = new();
    // weapon item id -> (hitVfx id, scale): extra impact VFX played client-side when that weapon lands a hit
    internal static readonly Dictionary<string, (string Vfx, float Scale)> HitVfxByItemId = new();
    // custom particles/emitters: (def, the pack's folder — where its PNG lives). Consumed by ParticlesFeature.
    internal static readonly List<(ParticleDef Def, string PackDir)> ParticleDefs = new();
    internal static readonly List<EmitterDef> EmitterDefs = new();
    // custom crops: (def, the pack's folder — where a display PNG lives). Consumed by CropsFeature.
    internal static readonly List<(CropDef Def, string PackDir)> CropDefs = new();
    // dialogue NPCs: (def, the pack's folder — where its .yarn lives). Consumed by NpcsFeature.
    internal static readonly List<(NpcDef Def, string PackDir)> NpcDefs = new();
    private static readonly List<SkillData> _skills = new();
    internal static readonly List<string> CustomSkillIds = new();
    // spell id -> casting requirements (checked client-side at cast; this game's casting is client-driven)
    internal static readonly Dictionary<string, SpellRequiresDef> SpellRequirements = new();
    // talking furniture: synthetic dialogue defs (compiled through the same NPC pipeline, no doodad placed)
    internal static readonly List<(NpcDef Def, string PackDir)> PlaceableDialogues = new();
    // recipes the player should always see in the crafting menu (registered recipes minus quest rewards)
    internal static IEnumerable<string> AutoKnownRecipeIds =>
        _recipes.Where(r => !_questRewardRecipeIds.Contains(r.Id)).Select(r => r.Id);
    private static readonly List<CharacterDisplayData> _displayData = new();
    // Custom held-weapon skins: requests (tag, name, png path, frame w/h) parsed from packs, and the built
    // parts (textures loaded once; re-inserted into PlayerSkinManager.Parts after each Setup clears it).
    private static readonly List<(int tag, string name, string path, int w, int h)> _customSkins = new();
    private static readonly List<(int tag, string name, PlayerSkinPart part)> _skinParts = new();
    private static bool _customSkinsBuilt;
    private static bool _palettesBuilt;
    // Custom sprite-animation VFX parsed from packs (id, sheet path, frame w/h, cell order, ticks/frame, scale).
    private static readonly List<(string id, string path, int fw, int fh, int[] frames, int dur, float scale)> _vfx = new();
    private static bool _vfxBuilt;
    private static bool _banksLoaded;
    // Custom loot: add an item to an existing drop table (table id, item id, %chance, min, max, aura).
    private static readonly List<(string table, string item, float chance, int min, int max, string aura)> _drops = new();

    // One installed pack's identity (for the load summary + co-op cross-check; players compare these lines).
    public readonly struct LoadedPack
    {
        public readonly string Id, Namespace, Author, Version, File;
        public LoadedPack(string id, string ns, string author, string version, string file)
        { Id = id; Namespace = ns; Author = author; Version = version; File = file; }
    }
    public static readonly List<LoadedPack> LoadedPacks = new();

    // Custom palettes parsed from packs: paletteId -> absolute .png path (seeded into the game on first use).
    private static readonly Dictionary<string, string> _palettes = new();

    // Read every pack once into the cache. Errors are logged per-pack/per-item and skipped — one bad pack
    // never blocks the others. Packs declare an identity (id/namespace/version) and dependencies so many
    // authors' packs coexist: deps are resolved (missing dep -> skip with a warning), packs build in
    // dependency order, and every content id is checked to start with the pack's namespace.
    public static void EnsureParsed(string dir)
    {
        if (_parsed) return;
        _parsed = true;

        // Pass 1: read + deserialize every pack file. Content is built in pass 2 (in dependency order).
        var loaded = new List<(string file, string name, ContentPack pack)>();
        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            string name = Path.GetFileName(file);
            ContentPack pack;
            try { pack = JsonSerializer.Deserialize<ContentPack>(File.ReadAllText(file), JsonOpts); }
            catch (Exception e) { Plugin.Log.LogError($"Pack '{name}' parse error: {e.Message}"); continue; }
            if (pack == null) continue;
            if (string.IsNullOrWhiteSpace(pack.Id)) pack.Id = Path.GetFileNameWithoutExtension(name);
            loaded.Add((file, name, pack));
        }

        // Resolve dependencies: drop packs whose declared deps aren't installed; order so deps build first.
        var present = new HashSet<string>(loaded.Select(t => t.pack.Id));
        var ordered = OrderByDependencies(loaded, present);

        // Pass 2: build content, in order.
        int skipped = 0;
        foreach (var (file, name, pack) in ordered)
        {
            string ns = string.IsNullOrWhiteSpace(pack.Namespace) ? pack.Id : pack.Namespace;
            BuildPack(dir, name, pack, ns, ref skipped);
            LoadedPacks.Add(new LoadedPack(pack.Id, ns, pack.Author, pack.Version, name));
        }

        Plugin.Log.LogInfo($"Content Forge: loaded {LoadedPacks.Count} pack(s), {_items.Count} item(s), " +
            $"{_spells.Count} spell(s), {_strings.Count} string(s)" + (skipped > 0 ? $", {skipped} skipped" : "") + ".");
        foreach (var p in LoadedPacks)
            Plugin.Log.LogInfo($"  - {p.Id} v{p.Version}" + (string.IsNullOrWhiteSpace(p.Author) ? "" : $" by {p.Author}") +
                $" [ns:{p.Namespace}]");
    }

    // Topological order so a pack builds after the packs it depends on. Packs with a missing dependency (or
    // depending on one that itself got dropped) are skipped with a warning. Dependency cycles are broken
    // (and warned) rather than looping forever.
    private static List<(string file, string name, ContentPack pack)> OrderByDependencies(
        List<(string file, string name, ContentPack pack)> loaded, HashSet<string> present)
    {
        var byId = loaded.ToDictionary(t => t.pack.Id, t => t);
        var result = new List<(string, string, ContentPack)>();
        var done = new HashSet<string>();
        var dropped = new HashSet<string>();

        bool Visit(string id, HashSet<string> stack)
        {
            if (done.Contains(id)) return true;
            if (dropped.Contains(id)) return false;
            if (!byId.TryGetValue(id, out var entry)) return false;
            if (stack.Contains(id)) { Plugin.Log.LogWarning($"Pack '{id}': dependency cycle — loading anyway."); return true; }
            stack.Add(id);
            var deps = entry.pack.Dependencies;
            if (deps != null)
                foreach (var dep in deps)
                {
                    if (!present.Contains(dep) || !Visit(dep, stack))
                    {
                        Plugin.Log.LogWarning($"Pack '{id}' skipped: dependency '{dep}' " +
                            (present.Contains(dep) ? "failed to load." : "is not installed."));
                        dropped.Add(id); stack.Remove(id); return false;
                    }
                }
            stack.Remove(id);
            if (done.Add(id)) result.Add(entry);
            return true;
        }

        foreach (var t in loaded) Visit(t.pack.Id, new HashSet<string>());
        return result;
    }

    // Build one pack's content into the caches. Each content id is checked to start with the pack's
    // namespace so two authors can't collide (a warning, not a hard error — ids stay author-controlled).
    private static void BuildPack(string dir, string name, ContentPack pack, string ns, ref int skipped)
    {
        void NsCheck(string id, string kind)
        {
            if (!string.IsNullOrWhiteSpace(id) && !id.StartsWith(ns + ":"))
                Plugin.Log.LogWarning($"[{name}] {kind} id '{id}' is not namespaced '{ns}:...' — risks colliding with other packs.");
        }

        if (pack.Strings != null)
            foreach (var kv in pack.Strings) _strings[kv.Key] = kv.Value;

        if (pack.Icons != null)
            foreach (var kv in pack.Icons) _icons[kv.Key] = Path.Combine(dir, kv.Value);

        // Raw audio: "sounds": { "ding": "ding.wav" } -> playable anywhere as "cf:sound:<ns>:ding".
        if (pack.Sounds != null)
            foreach (var kv in pack.Sounds)
                if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    SoundsFeature.Register("cf:sound:" + ns + ":" + kv.Key, Path.Combine(dir, kv.Value));

        // Custom spacs: WITH `base` = clone/reskin/retime a vanilla spac; WITHOUT `base` = brand-new
        // from-scratch spac built from your own PNG sheets + frame definitions.
        if (pack.Spacs != null)
            foreach (SpacDef sp in pack.Spacs)
                if (string.IsNullOrWhiteSpace(sp?.Id))
                    Plugin.Log.LogWarning($"[{name}] spac skipped (needs id).");
                else if (!string.IsNullOrWhiteSpace(sp.Base))
                { NsCheck(sp.Id, "spac"); SpacForgeFeature.Register(sp, dir, name); }
                else if (sp.Sheets is { Count: > 0 } && sp.Animations is { Count: > 0 })
                { NsCheck(sp.Id, "spac"); ScratchSpacFeature.Register(sp.Id, sp.Directions, sp.Sheets, sp.FrameWidth, sp.FrameHeight, sp.Animations, dir, name); }
                else Plugin.Log.LogWarning($"[{name}] spac '{sp.Id}' skipped (needs `base`, OR `sheets` + `animations` for a from-scratch spac).");

        // Player animation remixes: retime one of the player's 47 animation states (visual-only).
        if (pack.PlayerAnimations != null)
            foreach (var kv in pack.PlayerAnimations)
                PlayerAnimRemixFeature.Register(kv.Key, kv.Value, name);

        if (pack.Palettes != null)
            foreach (PaletteDef p in pack.Palettes)
                if (!string.IsNullOrWhiteSpace(p?.Id) && !string.IsNullOrWhiteSpace(p.Sheet))
                { NsCheck(p.Id, "palette"); _palettes[p.Id] = Path.Combine(dir, p.Sheet); }

        // Custom VFX. No namespace check — registering under a VANILLA id to reskin it is intended.
        if (pack.Vfx != null)
            foreach (VfxDef v in pack.Vfx)
                if (!string.IsNullOrWhiteSpace(v?.Id) && !string.IsNullOrWhiteSpace(v.Sheet) && v.Frames != null && v.Frames.Length > 0)
                    _vfx.Add((v.Id, Path.Combine(dir, v.Sheet), v.FrameWidth, v.FrameHeight, v.Frames,
                        v.FrameDuration <= 0 ? 1 : v.FrameDuration, v.Scale <= 0f ? 1f : v.Scale));

        // Custom loot drops. Table is a vanilla id; item may be yours or vanilla — so no namespace check.
        if (pack.Drops != null)
            foreach (DropDef dr in pack.Drops)
                if (!string.IsNullOrWhiteSpace(dr?.Table) && !string.IsNullOrWhiteSpace(dr.Item))
                {
                    int min = dr.Min < 1 ? 1 : dr.Min;
                    _drops.Add((dr.Table, dr.Item, dr.Chance <= 0f ? 5f : dr.Chance, min, dr.Max < min ? min : dr.Max, dr.Aura));
                }

        // Custom creatures parse BEFORE spells: a summon spell may reference a creature id from this pack,
        // and TryResolveCreature consults CustomCreatureGuids at spell-build time.
        if (pack.Creatures != null)
            foreach (CreatureDef c in pack.Creatures)
                if (c != null && !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Base))
                {
                    NsCheck(c.Id, "creature");
                    CustomCreatureGuids[c.Id] = GuidFromId(c.Id);
                    _creatureDefs.Add(c);
                }
                else Plugin.Log.LogWarning($"[{name}] creature skipped (needs id + base).");

        if (pack.Spells != null)
            foreach (SpellDef s in pack.Spells)
                if (TryBuildSpell(s, name, out SpellDataModel sm))
                {
                    NsCheck(s.Id, "spell");
                    // The game shows a spell's name via "<id>*spell:name" (AddData rewrites DisplayName to it).
                    if (s.Id != null)
                    {
                        _strings[s.Id + "*spell:name"] = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Id : s.DisplayName;
                        if (s.DisplayNameLocalized != null && s.DisplayNameLocalized.Count > 0)
                            _locStrings[s.Id + "*spell:name"] = s.DisplayNameLocalized;
                    }
                    _spells.Add(sm);
                }

        if (pack.Items != null)
            foreach (ItemDef def in pack.Items)
            {
                if (TryBuildItem(def, name, out ItemData data))
                {
                    NsCheck(def.Id, "item");
                    // Register the item's display text under the game's "<id>*item:name/description" keys.
                    _strings[def.Id + "*item:name"] = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name;
                    if (def.NameLocalized != null && def.NameLocalized.Count > 0)
                        _locStrings[def.Id + "*item:name"] = def.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(def.Description))
                        _strings[def.Id + "*item:description"] = def.Description;
                    if (def.DescriptionLocalized != null && def.DescriptionLocalized.Count > 0)
                        _locStrings[def.Id + "*item:description"] = def.DescriptionLocalized;
                    // dismissOnUnequip binding: remember which equippable item casts which summon spell
                    string castId = def.Equippable?.SpellTome?.SpellId ?? def.Equippable?.Weapon?.CastSpell?.SpellId;
                    if (castId != null && DismissSpells.TryGetValue(castId, out var owners)) owners.Add(def.Id);
                    // hitVfx binding: this weapon plays an extra impact VFX on hit (resolved client-side)
                    if (!string.IsNullOrWhiteSpace(def.Equippable?.Weapon?.HitVfx))
                        HitVfxByItemId[def.Id] = (def.Equippable.Weapon.HitVfx,
                            def.Equippable.Weapon.HitVfxScale <= 0f ? 1f : def.Equippable.Weapon.HitVfxScale);
                    // attack tuning: per-weapon swing arc/offset (cone melee only; applied client-side at cast)
                    if (def.Equippable?.Weapon != null &&
                        (def.Equippable.Weapon.AttackArcDegrees > 0f || def.Equippable.Weapon.AttackArcOffsetPx != 0f))
                        AttackTuningFeature.ByItemId[def.Id] = (def.Equippable.Weapon.AttackArcDegrees * (float)Math.PI / 180f,
                            def.Equippable.Weapon.AttackArcOffsetPx);
                    // two-handed: this weapon keeps the offhand slot empty while equipped (host-side enforcement)
                    if (def.Equippable?.Weapon?.TwoHanded == true)
                        TwoHandedFeature.TwoHandedItemIds.Add(def.Id);
                    // per-combo-step tuning: damage/arc/timing per hit of the chain (cone melee only)
                    if (def.Equippable?.Weapon?.ComboSteps is { Count: > 0 })
                        ComboTuningFeature.Register(def.Id, def.Equippable.Weapon.ComboSteps);
                    _items.Add(data);
                }
                else skipped++;
            }

        if (pack.Recipes != null)
            foreach (RecipeDef rd in pack.Recipes)
                if (TryBuildRecipe(rd, name, out ItemRecipe rec)) { NsCheck(rd.Id, "recipe"); _recipes.Add(rec); }

        if (pack.Classes != null)
            foreach (ClassDef c in pack.Classes)
                if (!string.IsNullOrWhiteSpace(c?.Id))
                {
                    NsCheck(c.Id, "class");
                    // The picker shows the class via "<id>*player_class:name" (AddPlayerClass rewrites DisplayName to it).
                    _strings[c.Id + "*player_class:name"] = string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name;
                    if (c.NameLocalized != null && c.NameLocalized.Count > 0) _locStrings[c.Id + "*player_class:name"] = c.NameLocalized;
                    _classes.Add(new PlayerClassModel
                    {
                        Id = c.Id,
                        DisplayName = c.Name ?? c.Id,
                        BonusSkill = string.IsNullOrWhiteSpace(c.BonusSkill) ? "skill:swords" : c.BonusSkill,
                        StartingClothes = c.StartingClothes ?? new List<string>(),
                        StartingInventory = (c.StartingInventory ?? new()).Select(kv => new ItemAmount(kv.Key, kv.Value)).ToList(),
                        SkillBonuses = c.SkillBonuses?.Select(kv => (kv.Key, kv.Value)).ToList(),
                        StartingFavourPoints = c.StartingFavourPoints,
                    });
                }

        if (pack.Quests != null)
            foreach (QuestDef q in pack.Quests)
                if (TryBuildQuest(q, name, out QuestData qd))
                {
                    NsCheck(q.Id, "quest");
                    _strings[q.Id + "*quest:name"] = string.IsNullOrWhiteSpace(q.Name) ? q.Id : q.Name;
                    if (q.NameLocalized != null && q.NameLocalized.Count > 0) _locStrings[q.Id + "*quest:name"] = q.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(q.Description)) _strings[q.Id + "*quest:description"] = q.Description;
                    if (q.DescriptionLocalized != null && q.DescriptionLocalized.Count > 0) _locStrings[q.Id + "*quest:description"] = q.DescriptionLocalized;
                    if (q.RewardRecipes != null) foreach (string rid in q.RewardRecipes) _questRewardRecipeIds.Add(rid);
                    _quests.Add(qd);
                }

        // `placeables` (canonical) / `furniture` / `structures` are one section (same DTO) — merge them.
        if (pack.Placeables != null) (pack.Furniture ??= new List<PlaceableDef>()).AddRange(pack.Placeables);
        if (pack.Structures != null) (pack.Furniture ??= new List<PlaceableDef>()).AddRange(pack.Structures);
        if (pack.Furniture != null)
            foreach (PlaceableDef f in pack.Furniture)
            {
                // COMPOSITE: a `parts` list makes it a multi-part forge_composite entity (Composite.cs).
                // Must run before the yarn/station defaults — composite owns the controller slot.
                CompositeFeature.PreparePlaceable(f, name, dir);
                // TALKING furniture: a yarn file makes it a dialogue entity. Default the controller, inject
                // the dialogue parameters, and compile the yarn through the NPC pipeline (no doodad placed).
                if (f != null && !string.IsNullOrWhiteSpace(f.Yarn))
                {
                    if (string.IsNullOrWhiteSpace(f.Controller)) f.Controller = "forge_dialogue";
                    f.ControllerParams ??= new Dictionary<string, string>();
                    f.ControllerParams.TryAdd("yarn-path", "cf/" + NpcsFeature.SafeId(f.Id));
                    f.ControllerParams.TryAdd("yarn-node", string.IsNullOrWhiteSpace(f.StartNode) ? "Start" : f.StartNode);
                    PlaceableDialogues.Add((new NpcDef
                    {
                        Id = f.Id, Yarn = f.Yarn,
                        StartNode = f.StartNode, Actors = f.Actors, ActorsLocalized = f.ActorsLocalized,
                    }, dir));
                }
                // CRAFTING STATION: a non-empty `station` defaults the controller to forge_station and rides
                // the id in as the "station" controller parameter (window name/icon registered from f.Name/f.Icon).
                StationsFeature.PreparePlaceable(f, name);
                if (TryBuildPlaceable(f, name, dir, out ConstructionModel cm))
                {
                    NsCheck(f.Id, "furniture");
                    // AddConstructions rewrites Name/Description to "<id>*construction:name/description" loc keys.
                    _strings[f.Id + "*construction:name"] = string.IsNullOrWhiteSpace(f.Name) ? f.Id : f.Name;
                    if (f.NameLocalized != null) _locStrings[f.Id + "*construction:name"] = f.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(f.Description)) _strings[f.Id + "*construction:description"] = f.Description;
                    if (f.DescriptionLocalized != null) _locStrings[f.Id + "*construction:description"] = f.DescriptionLocalized;
                    _placeables.Add(cm);
                    PlaceableRecipeIds.Add(f.Id);
                }
            }

        if (pack.Maps != null)
            foreach (MapDef m in pack.Maps)
                if (m != null && !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Tmx))
                {
                    NsCheck(m.Id, "map");
                    // AddDungeonRoomData rewrites DisplayName to "<id>*dungeon_room:name" — register the text.
                    _strings[m.Id + "*dungeon_room:name"] = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;
                    if (m.NameLocalized != null && m.NameLocalized.Count > 0) _locStrings[m.Id + "*dungeon_room:name"] = m.NameLocalized;
                    if (m.Pvp) PvpFeature.PvpMapIds.Add(m.Id);   // pvp zone — PvpFeature lifts the friendly-fire gates there
                    Maps.Add((m, dir));
                }
                else Plugin.Log.LogWarning($"[{name}] map skipped (needs id + tmx).");

        if (pack.Arenas != null)
            foreach (ArenaDef a in pack.Arenas)
                if (a != null && !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Map))
                { NsCheck(a.Id, "arena"); EventsFeature.RegisterArena(a); }
                else Plugin.Log.LogWarning($"[{name}] arena skipped (needs id + map).");

        if (pack.Events != null)
            foreach (EventDef ev in pack.Events)
                if (ev != null && !string.IsNullOrWhiteSpace(ev.Id) && !string.IsNullOrWhiteSpace(ev.Type))
                { NsCheck(ev.Id, "event"); if (!WerewolfFeature.RegisterEvent(ev) && !EventsCompetitiveFeature.RegisterEvent(ev)) EventsPlusFeature.RegisterEvent(ev); }
                else Plugin.Log.LogWarning($"[{name}] event skipped (needs id + type).");

        if (pack.Shops != null)
            foreach (ShopDef s in pack.Shops)
                if (s != null && !string.IsNullOrWhiteSpace(s.Id) && s.Items != null && s.Items.Count > 0)
                {
                    NsCheck(s.Id, "shop");
                    _shops.Add(new Shop
                    {
                        Id = s.Id,
                        ItemIds = s.Items.ToArray(),
                        MerchantLevel = s.MerchantLevel < 0 ? 0 : s.MerchantLevel,
                        BiomeCategory = string.IsNullOrWhiteSpace(s.BiomeCategory) ? null : s.BiomeCategory,
                        ExtraGoodsWorship = s.ExtraGoodsWorship,
                    });
                }
                else Plugin.Log.LogWarning($"[{name}] shop skipped (needs id + items).");

        if (pack.ShopAdditions != null)
            foreach (ShopAddDef a in pack.ShopAdditions)
                if (a != null && !string.IsNullOrWhiteSpace(a.Shop) && a.Items != null && a.Items.Count > 0)
                    _shopAdds.Add(a);
                else Plugin.Log.LogWarning($"[{name}] shopAddition skipped (needs shop + items).");

        if (pack.Particles != null)
            foreach (ParticleDef p in pack.Particles)
                if (p != null && !string.IsNullOrWhiteSpace(p.Id))
                { NsCheck(p.Id, "particle"); ParticleDefs.Add((p, dir)); }
                else Plugin.Log.LogWarning($"[{name}] particle skipped (needs id).");

        if (pack.Emitters != null)
            foreach (EmitterDef em in pack.Emitters)
                if (em != null && !string.IsNullOrWhiteSpace(em.Id))
                { NsCheck(em.Id, "emitter"); EmitterDefs.Add(em); }
                else Plugin.Log.LogWarning($"[{name}] emitter skipped (needs id).");

        if (pack.Crops != null)
            foreach (CropDef c in pack.Crops)
                if (c != null && !string.IsNullOrWhiteSpace(c.Id))
                {
                    NsCheck(c.Id, "crop");
                    CropDefs.Add((c, dir));
                    // The seed item rides the existing item pipeline (InjectItems + ApplyStrings).
                    ItemData seed = CropsFeature.BuildSeedItem(c);
                    if (seed != null)
                    {
                        NsCheck(c.Seed.Id, "seed item");
                        _strings[c.Seed.Id + "*item:name"] = string.IsNullOrWhiteSpace(c.Seed.Name) ? c.Seed.Id : c.Seed.Name;
                        if (c.Seed.NameLocalized != null && c.Seed.NameLocalized.Count > 0)
                            _locStrings[c.Seed.Id + "*item:name"] = c.Seed.NameLocalized;
                        if (!string.IsNullOrWhiteSpace(c.Seed.Description))
                            _strings[c.Seed.Id + "*item:description"] = c.Seed.Description;
                        if (c.Seed.DescriptionLocalized != null && c.Seed.DescriptionLocalized.Count > 0)
                            _locStrings[c.Seed.Id + "*item:description"] = c.Seed.DescriptionLocalized;
                        _items.Add(seed);
                    }
                }
                else Plugin.Log.LogWarning($"[{name}] crop skipped (needs id).");

        if (pack.Npcs != null)
            foreach (NpcDef n in pack.Npcs)
                if (n != null && !string.IsNullOrWhiteSpace(n.Id) && !string.IsNullOrWhiteSpace(n.Map)
                    && !string.IsNullOrWhiteSpace(n.Yarn) && n.Position != null && n.Position.Length >= 2)
                { NsCheck(n.Id, "npc"); NpcDefs.Add((n, dir)); }
                else Plugin.Log.LogWarning($"[{name}] npc skipped (needs id + map + yarn + position[2]).");

        if (pack.Skills != null)
            foreach (SkillDef sk in pack.Skills)
                if (sk != null && !string.IsNullOrWhiteSpace(sk.Id))
                {
                    // no NsCheck: skills follow the game's own "skill:" convention (like vfx reskins)
                    _strings[sk.Id + "*skills:name"] = string.IsNullOrWhiteSpace(sk.Name) ? sk.Id : sk.Name;
                    if (sk.NameLocalized != null && sk.NameLocalized.Count > 0) _locStrings[sk.Id + "*skills:name"] = sk.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(sk.Description)) _strings[sk.Id + "*skills:description"] = sk.Description;
                    if (sk.DescriptionLocalized != null && sk.DescriptionLocalized.Count > 0) _locStrings[sk.Id + "*skills:description"] = sk.DescriptionLocalized;
                    _skills.Add(new SkillData
                    {
                        Id = sk.Id,
                        Name = string.IsNullOrWhiteSpace(sk.Name) ? sk.Id : sk.Name,
                        Description = string.IsNullOrWhiteSpace(sk.Description) ? default : (StringId)sk.Description,
                        Icon = string.IsNullOrWhiteSpace(sk.Icon) ? "sam" : sk.Icon,
                        Value = sk.Value,
                        ExperienceGainFactor = sk.XpGainFactor <= 0f ? 1f : sk.XpGainFactor,
                    });
                    CustomSkillIds.Add(sk.Id);
                }
                else Plugin.Log.LogWarning($"[{name}] skill skipped (needs id).");

        if (pack.Favours != null)
            foreach (FavourDef fv in pack.Favours)
                if (fv != null && !string.IsNullOrWhiteSpace(fv.Id) && (fv.Screen || !string.IsNullOrWhiteSpace(fv.Parent))
                    && fv.Position != null && fv.Position.Length >= 2)
                {
                    NsCheck(fv.Id, "favour");
                    _strings[fv.Id + "*favour:name"] = string.IsNullOrWhiteSpace(fv.Name) ? fv.Id : fv.Name;
                    if (fv.NameLocalized != null && fv.NameLocalized.Count > 0) _locStrings[fv.Id + "*favour:name"] = fv.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(fv.Description)) _strings[fv.Id + "*favour:description"] = fv.Description;
                    if (fv.DescriptionLocalized != null && fv.DescriptionLocalized.Count > 0) _locStrings[fv.Id + "*favour:description"] = fv.DescriptionLocalized;
                    FavoursFeature.Defs.Add((fv, name));
                }
                else Plugin.Log.LogWarning($"[{name}] favour skipped (needs id + parent + position[2]).");

        // Custom citizen types + edits to existing ones. Consumed by CitizensFeature. A fixed `name`
        // becomes the "<id>*citizen:name" string (our convention — citizen types have no vanilla loc key).
        if (pack.Citizens != null)
            foreach (CitizenDef cz in pack.Citizens)
                if (cz != null && !string.IsNullOrWhiteSpace(cz.Id))
                {
                    NsCheck(cz.Id, "citizen");
                    if (!string.IsNullOrWhiteSpace(cz.Name))
                    {
                        _strings[cz.Id + "*citizen:name"] = cz.Name;
                        if (cz.NameLocalized != null && cz.NameLocalized.Count > 0) _locStrings[cz.Id + "*citizen:name"] = cz.NameLocalized;
                    }
                    CitizensFeature.Defs.Add((cz, name));
                }
                else Plugin.Log.LogWarning($"[{name}] citizen skipped (needs id).");

        if (pack.CitizenOverrides != null)
            foreach (var kv in pack.CitizenOverrides)
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    CitizensFeature.Overrides.Add((kv.Key, kv.Value, name));

        // Town job-buildings + work recipes. Consumed by TownBuildingsFeature (clone-from-base at Setup time).
        if (pack.TownBuildings != null)
            foreach (TownBuildingDef tb in pack.TownBuildings)
                if (tb != null && !string.IsNullOrWhiteSpace(tb.Id) && !string.IsNullOrWhiteSpace(tb.Base))
                {
                    NsCheck(tb.Id, "townBuilding");
                    // AddConstructions rewrites Name/Description to "<id>*construction:name/description" loc keys.
                    _strings[tb.Id + "*construction:name"] = string.IsNullOrWhiteSpace(tb.Name) ? tb.Id : tb.Name;
                    if (tb.NameLocalized != null) _locStrings[tb.Id + "*construction:name"] = tb.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(tb.Description)) _strings[tb.Id + "*construction:description"] = tb.Description;
                    if (tb.DescriptionLocalized != null) _locStrings[tb.Id + "*construction:description"] = tb.DescriptionLocalized;
                    TownBuildingsFeature.Defs.Add((tb, name));
                    PlaceableRecipeIds.Add(tb.Id);   // same login auto-unlock as furniture — shows in the Buildings tab
                }
                else Plugin.Log.LogWarning($"[{name}] townBuilding skipped (needs id + base).");

        if (pack.WorkRecipes != null)
            foreach (WorkRecipeDef wr in pack.WorkRecipes)
                if (wr != null && !string.IsNullOrWhiteSpace(wr.Id))
                {
                    // ids follow the game's own "recipe:" convention — namespace what comes after the prefix
                    NsCheck(wr.Id.StartsWith("recipe:") ? wr.Id.Substring(7) : wr.Id, "workRecipe");
                    // AddRecipes re-keys Name/Description to "<id>*work_recipe:name/description" ONLY when set
                    // (unset falls back to the output item's name/icon) — register text only for what we set.
                    if (!string.IsNullOrWhiteSpace(wr.Name))
                    {
                        _strings[wr.Id + "*work_recipe:name"] = wr.Name;
                        if (wr.NameLocalized != null) _locStrings[wr.Id + "*work_recipe:name"] = wr.NameLocalized;
                    }
                    if (!string.IsNullOrWhiteSpace(wr.Description))
                    {
                        _strings[wr.Id + "*work_recipe:description"] = wr.Description;
                        if (wr.DescriptionLocalized != null) _locStrings[wr.Id + "*work_recipe:description"] = wr.DescriptionLocalized;
                    }
                    TownBuildingsFeature.RecipeDefs.Add((wr, name));
                }
                else Plugin.Log.LogWarning($"[{name}] workRecipe skipped (needs id).");

        // Player skins: reskins / new hair / new parts / worn looks. Consumed by PlayerSkinsFeature.
        if (pack.PlayerSkins != null)
        {
            var ps = pack.PlayerSkins;
            if (ps.Reskins != null) foreach (var r in ps.Reskins) PlayerSkinsFeature.ReskinDefs.Add((r, dir, name));
            if (ps.Hair != null) foreach (var h2 in ps.Hair) PlayerSkinsFeature.HairDefs.Add((h2, dir, name));
            if (ps.Parts != null) foreach (var p2 in ps.Parts) PlayerSkinsFeature.PartDefs.Add((p2, dir, name));
            if (ps.Rig != null) PlayerRigFeature.Register(ps.Rig, dir, name);
            if (ps.Looks != null)
                foreach (var l in ps.Looks)
                {
                    // Looks are DISPLAY ids and follow the game's "cdd:" convention (like held looks) —
                    // namespace check applies to what comes after the prefix.
                    if (l != null && !string.IsNullOrWhiteSpace(l.Id))
                        NsCheck(l.Id.StartsWith("cdd:") ? l.Id.Substring(4) : l.Id, "playerSkins.look");
                    PlayerSkinsFeature.LookDefs.Add((l, dir, name));
                }
        }
    }

    // Build a Tier-1 furniture construction: a decoration that reuses an existing vanilla decoration's art.
    private static bool TryBuildPlaceable(PlaceableDef f, string pack, string dir, out ConstructionModel cm)
    {
        cm = null;
        // A custom sprite alone also needs the entity path — give it the inert prop controller.
        if (f != null && string.IsNullOrWhiteSpace(f.Controller) && string.IsNullOrWhiteSpace(f.Yarn)
            && !string.IsNullOrWhiteSpace(f.Sprite))
            f.Controller = "forge_prop";
        // Interactive furniture: a controller makes this an ENTITY construction (Interactive.cs owns that path)
        if (!string.IsNullOrWhiteSpace(f?.Controller))
            return InteractiveFeature.TryBuildEntityConstruction(f, pack, dir, out cm);
        if (f == null || string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.SpawnedId))
        { Plugin.Log.LogWarning($"[{pack}] furniture skipped (needs id + spawnedId)."); return false; }
        int w = f.Width < 1 ? 1 : f.Width;
        int h = f.Height < 1 ? 1 : f.Height;
        cm = new ConstructionModel
        {
            Id = f.Id,
            Name = string.IsNullOrWhiteSpace(f.Name) ? f.Id : f.Name,                  // rewritten to loc key by AddConstructions
            Description = string.IsNullOrWhiteSpace(f.Description) ? (StringId?)null : f.Description,
            IconId = string.IsNullOrWhiteSpace(f.Icon) ? "sam" : f.Icon,
            UpgradeLevel = 0,
            RequiredConstructionProgress = 0f,
            ConstructionMaterialsRequirement = new ConstructionMaterialsAggregate
            {
                MaterialAmounts = f.Materials ?? new Dictionary<string, int>()
            },
            ConstructionSpaceInfo = new ConstructionSpaceInfo
            {
                Areas = new[]
                {
                    new ConstructionTileArea
                    {
                        TileBounds = new Rectangle(0, 0, w, h),
                        Requirements = ConstructionSpaceInfo.PlacementRequirements.FreeTilesAndNoFurniture
                    }
                },
                PlacingStyle = ConstructionSpaceInfo.PlacementStyle.Single
            },
            Type = ConstructionType.Decoration,
            SpawnedId = f.SpawnedId,
            ConstructionUiCategory = ConstructionUiCategory.Decorations
        };
        return true;
    }

    // A World quest: activates on its own (empty requirements), tracks the objectives, rewards crafting recipes.
    private static bool TryBuildQuest(QuestDef q, string pack, out QuestData qd)
    {
        qd = null;
        if (q == null || string.IsNullOrWhiteSpace(q.Id) || q.Objectives == null || q.Objectives.Count == 0)
        { Plugin.Log.LogWarning($"[{pack}] quest skipped (needs id + objectives)."); return false; }
        var objs = new List<(string, QuestObjective)>();
        int i = 0;
        foreach (ObjectiveDef o in q.Objectives)
            if (TryBuildObjective(o, out QuestObjective obj)) objs.Add(("obj" + i++, obj));
        if (objs.Count == 0) { Plugin.Log.LogWarning($"[{pack}] quest '{q.Id}': no valid objectives."); return false; }
        qd = new QuestData
        {
            Id = q.Id,
            Name = q.Name ?? q.Id,
            Description = string.IsNullOrWhiteSpace(q.Description) ? (StringId?)null : q.Description,
            IconId = string.IsNullOrEmpty(q.Icon) ? null : q.Icon,
            QuestType = QuestType.World,
            Requirements = new List<IQuestRequirement>(),
            Objectives = objs,
            RewardPlayerCraftingRecipeIds = (q.RewardRecipes != null && q.RewardRecipes.Count > 0) ? q.RewardRecipes.ToArray() : null,
            Repeatable = q.Repeatable,
            AutomaticTurnIn = true,
            AutoTrackWhenAdded = true,
        };
        QuestRewardsPlusFeature.RegisterQuestItems(q, qd);   // sets qd.RewardId = "cf:reward:<id>" + records items
        return true;
    }

    private static bool TryBuildObjective(ObjectiveDef o, out QuestObjective obj)
    {
        obj = null;
        if (o == null || string.IsNullOrWhiteSpace(o.Target)) return false;
        int amt = o.Amount < 1 ? 1 : o.Amount;
        switch (o.Type?.ToLowerInvariant())
        {
            case "kill":
                if (!TryResolveCreature(o.Target, out Guid g)) return false;
                obj = new KillEntityQuestObjective(g, amt, o.Target); break;
            case "collect": obj = new PlayerCollectItemsObjective(o.Target, amt); break;
            case "loot": obj = new PlayerLootItemObjective(o.Target, amt); break;
            case "craft": obj = new PlayerCraftItemObjective(o.Target, amt); break;
            case "harvest": obj = new PlayerHarvestCropObjective(o.Target, amt); break;
            case "handin": obj = new HandInItemObjective(o.Target, amt); break;          // deliver items to the quest
            case "chop": obj = new ChopTreeObjective(amt); break;                         // chop N trees (target = anything)
            case "build": obj = new ConstructBuildingObjective(o.Target, o.Target); break; // construct a building type
            case "equip":                                                                  // equip an item of a slot type
                if (!Enum.TryParse(o.Target, ignoreCase: true, out EquipmentType eqt)) return false;
                obj = new PlayerEquipItemTypeObjective(eqt); break;
            case "interact":                                                               // vanilla targets: TownCore/Altar/Citizen/Leatherworker/FoodStorage
                if (!Enum.TryParse(o.Target, ignoreCase: true, out PlayerObjectiveInteractionType pit)) return false;
                obj = new PlayerInteractObjective(pit); break;
            default: return false;
        }
        return true;
    }

    // Load custom .png icons into the icon database (once — textures are GPU resources). Each becomes an
    // IconData whose sprite wraps the loaded texture; items reference it by its icon id. Needs the
    // GraphicsDevice, which is ready by the time the world's data loads; we retry until it is.
    public static void ApplyIcons()
    {
        if (_iconsBuilt || _icons.Count == 0) return;
        if (Globals.GraphicsDevice == null) return;   // not ready yet; retry on a later hook

        var built = new List<IconData>();
        foreach (var kv in _icons)
        {
            try
            {
                if (IconDataBase.IconDataMap.ContainsKey(kv.Key)) continue;
                if (!File.Exists(kv.Value)) { Plugin.Log.LogWarning($"Icon file not found: {kv.Value}"); continue; }
                using var fs = File.OpenRead(kv.Value);
                var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, tex.Width, tex.Height);
                var icon = new Icon(sheet, 0);
                built.Add(new IconData
                {
                    Id = kv.Key,
                    Variations = new Dictionary<IconFlag, Icon>
                    {
                        { IconFlag.Small, icon }, { IconFlag.Medium, icon },
                        { IconFlag.Large, icon }, { IconFlag.Huge, icon },
                    },
                });
            }
            catch (Exception e) { Plugin.Log.LogError($"Icon '{kv.Key}' load failed: {e.Message}"); }
        }
        if (built.Count > 0) IconDataBase.AddIcons(built);
        _iconsBuilt = true;
        Plugin.Log.LogInfo($"Content Forge: {built.Count} custom icon(s) loaded.");
    }

    // Add our spells to the spell database (AddData uses TryAdd — re-running is safe).
    public static void InjectSpells()
    {
        if (_spells.Count > 0) SpellDataBase.AddData(_spells.ToList());
    }

    // Add our items to the item database (idempotent — AddItems just sets DataMap[id]).
    public static void InjectItems()
    {
        if (_items.Count > 0) ItemDataBase.AddItems(_items.ToList());
    }

    // Add our crafting recipes (makes items craftable at the chosen station / basic crafting).
    public static void InjectRecipes()
    {
        if (_recipes.Count > 0) ItemRecipeDataBase.AddItemRecipes(_recipes.ToList());
    }

    // Register custom playable classes (they appear in the character-creation profession picker, which reads the
    // class DB live). Re-applied on each SharedDataSetup.Setup (the DB is cleared + rebuilt there).
    public static void InjectClasses()
    {
        if (_classes.Count > 0) PlayerClassDataBase.AddPlayerClass(_classes.ToList());
    }

    public static void InjectQuests()
    {
        if (_quests.Count > 0) QuestsDataBase.AddQuests(_quests.ToList());
    }

    // Register custom placeable furniture (decorations buildable from the construction menu). The build DB is
    // cleared/rebuilt on Setup, so re-adding is idempotent. The recipe unlock is handled at login (PlaceableRecipePatch).
    public static void InjectConstructions()
    {
        if (_placeables.Count > 0) ConstructionDataBase.AddConstructions(_placeables.ToList());
    }

    // Register new player skills (the skills screen enumerates the character's skill dict — the backfill
    // patches put our skills into every character so they appear and can gain XP).
    public static void InjectSkills()
    {
        if (_skills.Count > 0) SkillsDataBase.AddData(_skills.ToList());
    }

    // Register market stock: new shop sections (the merchant offers every DB entry whose gates pass), and
    // append items into existing vanilla sections. Runs on both host and clients (UI resolves locally).
    public static void InjectShops()
    {
        if (_shops.Count > 0) ShopDataBase.AddData(_shops.ToList());
        foreach (ShopAddDef a in _shopAdds)
        {
            if (ShopDataBase.ShopDataMap.TryGetValue(a.Shop, out Shop shop))
            {
                var merged = (shop.ItemIds ?? Array.Empty<string>()).ToList();
                foreach (string id in a.Items) if (!merged.Contains(id)) merged.Add(id);
                shop.ItemIds = merged.ToArray();
            }
            else Plugin.Log.LogWarning($"shopAddition: unknown shop '{a.Shop}' (see vanilla ids like all:1, forest:2).");
        }
    }

    // Clone each custom creature from its base prototype into the given entity registry (called once for the
    // server registry and once for the client registry — both must have the new Guid to spawn + render it).
    // Idempotent: skips a creature already present this side. `protos` is read for the base AND written for ours.
    internal static void RegisterCreatures(Dictionary<Guid, EntitySystem> protos, CandideReflection reflection)
    {
        EnsureParsed(Plugin.PacksDir);
        foreach (CreatureDef c in _creatureDefs)
        {
            try
            {
                Guid guid = CustomCreatureGuids[c.Id];
                if (protos.ContainsKey(guid)) continue;
                if (!TryResolveVanillaCreature(c.Base, out Guid baseGuid) || !protos.TryGetValue(baseGuid, out EntitySystem src))
                { Plugin.Log.LogWarning($"creature '{c.Id}': base '{c.Base}' not found in entity registry."); continue; }

                var clone = new EntitySystem(1);
                int id = clone.NewEntity();   // slot 0 (keeps its own Wrapper/IncId, which DeepCopyEntity preserves)
                EntityCopyHelper.DeepCopyEntity(src, 0, clone, id, initializeControllers: false, ignoreControllers: true, reflection);

                clone.General[0].Id = guid;
                clone.General[0].BaseGuid = guid;
                string cname = c.Name;
                if (c.NameLocalized != null && Globals.LanguageCode != null
                    && c.NameLocalized.TryGetValue(Globals.LanguageCode, out string cloc) && !string.IsNullOrWhiteSpace(cloc))
                    cname = cloc;
                if (!string.IsNullOrWhiteSpace(cname)) clone.General[0].Name = cname;
                if (c.Color != null && c.Color.Length >= 3)
                    clone.General[0].Color = new Color(Clamp255(c.Color[0]), Clamp255(c.Color[1]), Clamp255(c.Color[2]));
                if (c.HealthMultiplier > 0f && c.HealthMultiplier != 1f)
                {
                    float mh = clone.HealthComponent[0].MaxHealth * c.HealthMultiplier;
                    clone.HealthComponent[0].MaxHealth = mh;
                    clone.HealthComponent[0].Health = mh;
                }
                // CUSTOM AI (server side only — the client keeps the base's controller, so visuals are
                // untouched): swap the prototype's SERVER controller instance for a registered one — the
                // exact Interactive.cs pattern. Base controller params are copied first (vanilla AI
                // controllers hard-require their .cdx params), then the pack's overrides go on top.
                bool creatureServerSide = ReferenceEquals(protos, ServerEntityDataManager.EntitySystems);
                if (creatureServerSide && !string.IsNullOrWhiteSpace(c.Controller))
                {
                    AbstractController cc2 = ServerEntityControllerLoader.CreateController(
                        c.Controller.ToLowerInvariant().Replace("server", "").Replace("controller", "").Trim(), clone.General[0].Wrapper);
                    if (cc2 == null)
                        Plugin.Log.LogWarning($"creature '{c.Id}': controller '{c.Controller}' unknown on the server — keeping the base AI.");
                    else
                    {
                        // DONOR params first: a cross-family AI (archer brain on a warrior body) hard-requires
                        // ITS OWN .cdx params (attack spell, ranges) which the base creature doesn't carry —
                        // find the vanilla prototype that natively runs this controller type and copy its params.
                        foreach (EntitySystem donor in protos.Values)
                        {
                            var dc = donor.ControllerComponent[0].Controller;
                            if (dc == null || dc.GetType() != cc2.GetType()) continue;
                            cc2.Parameters.AddWithoutOverwrite(dc.Parameters);
                            break;
                        }
                        var baseCtrl = clone.ControllerComponent[0].Controller;
                        if (baseCtrl != null) cc2.Parameters.AddWithoutOverwrite(baseCtrl.Parameters);
                        if (c.ControllerParams != null)
                            foreach (var kv in c.ControllerParams) cc2.Parameters.Set(kv.Key, kv.Value);
                        clone.ControllerComponent[0] = new ControllerComponent { Controller = cc2, Ai = clone.ControllerComponent[0].Ai };
                        Plugin.Log.LogInfo($"creature '{c.Id}': server AI -> '{c.Controller}'.");
                    }
                }
                // CUSTOM CLIENT VISUALS (client side only): swap the clone's CLIENT controller — the mirror
                // of the server AI swap above. "forge_creature_anim" + controllerParams (spac/anim-map/...)
                // animates a cross-family AI that would otherwise freeze the base's hardcoded animations.
                if (!creatureServerSide && !string.IsNullOrWhiteSpace(c.ClientController))
                    CreatureAnimFeature.ApplyClientController(clone, c.Id, c.ClientController, c.ControllerParams);
                RigSwap.Apply(clone, c, Plugin.PacksDir, creatureServerSide);
                protos[guid] = clone;
                Plugin.Log.LogInfo($"Registered custom creature '{c.Id}' (base {c.Base}).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"creature '{c?.Id}' failed: {e.Message}"); }
        }
    }

    private static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

    // Vanilla-only resolve (an EntityIds field name → Guid). Used for a creature's `base` so a custom id never
    // resolves a custom base into itself.
    private static bool TryResolveVanillaCreature(string name, out Guid id)
    {
        id = Guid.Empty;
        var f = AccessTools.Field(typeof(EntityIds), name);
        if (f == null || f.FieldType != typeof(Guid)) return false;
        id = (Guid)f.GetValue(null);
        return true;
    }

    // Register composed held-weapon looks (skin shape + palette) so items can reference them by DisplayId.
    public static void ApplyDisplayData()
    {
        if (_displayData.Count > 0) CharacterDisplayDataBase.AddCharacterDisplayData(_displayData.ToList());
    }

    // Build custom held-weapon skins from PNGs (once — GPU textures) and (re)insert them into the live
    // PlayerSkinManager.Parts registry. Called from a PlayerSkinManager.Setup postfix, which (re)creates the
    // per-tag dicts; we re-insert each time so a reload doesn't drop our skins.
    public static void ApplyCustomSkins()
    {
        EnsureParsed(Plugin.PacksDir);
        if (_customSkins.Count == 0) return;

        if (!_customSkinsBuilt && Globals.GraphicsDevice != null)
        {
            foreach (var (tag, name, path, w, h) in _customSkins)
            {
                try
                {
                    if (!File.Exists(path)) { Plugin.Log.LogWarning($"Skin sheet not found: {path}"); continue; }
                    using var fs = File.OpenRead(path);
                    var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                    var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, w, h);
                    var slice = new PlayerSkinSlice
                    {
                        SpacTag = tag, Layer = 0f, DepthOffset = 1f, HideBase = true,
                        SpriteSheet = new Dictionary<string, SpriteSheet> { { "", sheet } },
                        SpriteSheetPath = "",
                    };
                    _skinParts.Add((tag, name, new PlayerSkinPart
                    {
                        Id = 10000 + _skinParts.Count, Name = name,
                        Slices = new Dictionary<string, PlayerSkinSlice> { { "", slice } },
                    }));
                }
                catch (Exception e) { Plugin.Log.LogError($"Custom skin '{name}' failed: {e.Message}"); }
            }
            _customSkinsBuilt = true;
            Plugin.Log.LogInfo($"Content Forge: {_skinParts.Count} custom held-weapon skin(s) loaded.");
        }

        // Re-insert into the live registry (it gets cleared/rebuilt on each PlayerSkinManager.Setup).
        foreach (var (tag, name, part) in _skinParts)
            if (PlayerSkinManager.Parts.TryGetValue(tag, out var d)) d[name] = part;
    }

    // Register custom color palettes (dye ramps) so held looks can recolor through them. A palette swap reads
    // the image via the game's private RawImageCache keyed by path; the public Texture() loader only reads the
    // content pipeline (xnb), so we load the author's PNG ourselves and seed it into that cache by reflection,
    // then point PlayerSkinManager.PalettesMap[id] at the same key. (PalettesMap is rebuilt on each Setup, so we
    // re-register every call, like the skins.) Row 0 of the palette = the source/key colors; lower rows = variants.
    public static void ApplyPalettes()
    {
        if (_palettes.Count == 0) return;
        if (!_palettesBuilt && Globals.GraphicsDevice != null)
        {
            var rawCache = AccessTools.Field(typeof(Content), "RawImageCache")?.GetValue(null)
                as System.Collections.IDictionary;
            if (rawCache == null) { Plugin.Log.LogError("Palette cache field not found; custom palettes disabled."); _palettesBuilt = true; }
            else
            {
                foreach (var kv in _palettes)
                {
                    try
                    {
                        if (!File.Exists(kv.Value)) { Plugin.Log.LogWarning($"Palette file not found: {kv.Value}"); continue; }
                        using var fs = File.OpenRead(kv.Value);
                        var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                        var colors = new uint[tex.Width * tex.Height];
                        tex.GetData(colors);
                        rawCache[kv.Key] = new Content.CachedRawImage { Colors = colors, Width = tex.Width };
                    }
                    catch (Exception e) { Plugin.Log.LogError($"Palette '{kv.Key}' failed: {e.Message}"); }
                }
                _palettesBuilt = true;
                Plugin.Log.LogInfo($"Content Forge: {_palettes.Count} custom palette(s) loaded.");
            }
        }
        // Re-register into the live map (it gets cleared/rebuilt on each PlayerSkinManager.Setup).
        foreach (var kv in _palettes) PlayerSkinManager.PalettesMap[kv.Key] = kv.Key;
    }

    // Register custom sprite-animation VFX (once — GPU textures). Each becomes a VfxSpritesAnimationModel: one
    // SpriteSheet (the author PNG) + a frame list (each frame shows a cell for N ticks). Registering under a
    // vanilla id overrides/reskins it; a new id is available for future triggers (weapon hit-vfx etc.).
    public static void ApplyVfx()
    {
        if (_vfxBuilt || _vfx.Count == 0) return;
        if (Globals.GraphicsDevice == null) return;   // not ready yet; retry on a later Setup
        var models = new List<VfxSpritesAnimationModel>();
        foreach (var v in _vfx)
        {
            try
            {
                if (!File.Exists(v.path)) { Plugin.Log.LogWarning($"VFX sheet not found: {v.path}"); continue; }
                using var fs = File.OpenRead(v.path);
                var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, v.fw, v.fh);
                var frames = new VfxSpritesAnimationFrame[v.frames.Length];
                for (int i = 0; i < v.frames.Length; i++)
                    frames[i] = new VfxSpritesAnimationFrame
                    {
                        Duration = v.dur,
                        Sprites = new[] { new VfxSpritesAnimationSprite { Frame = v.frames[i], SpriteSheetIndex = 0, Opacity = 1f, Scale = v.scale } },
                    };
                models.Add(new VfxSpritesAnimationModel { Id = v.id, SpriteSheets = new[] { sheet }, Frames = frames });
            }
            catch (Exception e) { Plugin.Log.LogError($"VFX '{v.id}' failed: {e.Message}"); }
        }
        if (models.Count > 0) VfxSpritesAnimationDataBase.AddVfxSpriteAnimations(models);
        _vfxBuilt = true;
        Plugin.Log.LogInfo($"Content Forge: {models.Count} custom VFX animation(s) loaded.");
    }

    // Load custom FMOD .bank files (authored in FMOD Studio) from the packs dir — or a packs\banks\ subfolder —
    // right after the game loads its own banks. Their events then play as event:/yourmod/... anywhere a sound id
    // is taken (castSound, etc). StudioSystem is internal, so we fetch it by reflection; FMOD types are public.
    public static void LoadSoundBanks()
    {
        if (_banksLoaded) return;
        _banksLoaded = true;
        var files = new List<string>();
        foreach (var d in new[] { Plugin.PacksDir, Path.Combine(Plugin.PacksDir, "banks") })
            if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.bank"));
        if (files.Count == 0) return;
        // A ".strings.bank" must load before its matching ".bank" (it holds the event-path lookup).
        files.Sort((a, b) => (b.EndsWith(".strings.bank") ? 1 : 0) - (a.EndsWith(".strings.bank") ? 1 : 0));

        object boxed = AccessTools.Field(AccessTools.TypeByName("Candide.Sound.SoundPlayer"), "StudioSystem")?.GetValue(null);
        if (boxed is not FMOD.Studio.System sys)
        { Plugin.Log.LogWarning("FMOD StudioSystem not available; custom sound banks not loaded."); return; }

        int ok = 0;
        foreach (var f in files)
        {
            var r = sys.loadBankFile(f, LOAD_BANK_FLAGS.NORMAL, out Bank _);
            if (r == FMOD.RESULT.OK) ok++;
            else Plugin.Log.LogWarning($"Sound bank '{Path.GetFileName(f)}' load failed: {r}");
        }
        Plugin.Log.LogInfo($"Content Forge: loaded {ok}/{files.Count} custom sound bank(s).");
    }

    // Add custom items to existing loot tables (server-authoritative). Each drop appends an ItemDrop to the
    // table's drop list; the list field is readonly so we replace it via reflection. Idempotent (skips if our
    // item is already in that table) so it survives the server re-running its table setup.
    public static void ApplyDrops()
    {
        EnsureParsed(Plugin.PacksDir);
        if (_drops.Count == 0) return;
        var itemDropsField = AccessTools.Field(typeof(DropTableSystem), "_itemDrops");
        int added = 0, missing = 0;
        foreach (var d in _drops)
        {
            var sdt = ServerDropTablesDataBase.GetDropTableItemListOrNull(d.table);
            if (sdt == null) { missing++; Plugin.Log.LogWarning($"Drop: table '{d.table}' not found; '{d.item}' not added."); continue; }
            var sys = sdt.DropTable;
            var arr = sys._itemDrops ?? System.Array.Empty<ItemDrop>();
            if (arr.Any(x => x.ItemId == d.item)) continue;   // already injected
            var list = new List<ItemDrop>(arr)
            { new ItemDrop(d.chance, d.max, d.min, d.item, null, null, string.IsNullOrEmpty(d.aura) ? null : d.aura) };
            itemDropsField.SetValue(sys, list.ToArray());
            added++;
        }
        if (added > 0 || missing > 0)
            Plugin.Log.LogInfo($"Content Forge: applied {added} custom drop(s)" + (missing > 0 ? $", {missing} missing table(s)" : "") + ".");
    }

    // Build (once, then cache) a custom-shape swing-trail spac for a weapon. We clone the vanilla trail
    // spac the weapon's class plays (baseSpacId, e.g. vfx_swordslash) and swap its first sprite sheet for
    // the author's repaint, keeping all animation timing/offset/direction data. Returns the clone id to use
    // in place of baseSpacId, or baseSpacId unchanged if anything's missing (never throws / never crashes
    // the trail). The first build logs the exact vanilla sheet name + frame size so authors know what to
    // repaint. Returns the dedicated reusable list so the game's lookup keys on our id.
    public static string EnsureTrailSpac(string weaponId, string baseSpacId)
    {
        string newId = "cf_trail_" + weaponId + "__" + baseSpacId;
        if (SpacDataBase.SpacMap.ContainsKey(newId)) return newId;                 // already built
        if (Globals.GraphicsDevice == null) return baseSpacId;
        if (!TrailSheets.TryGetValue(weaponId, out string png) || !File.Exists(png))
        {
            if (png != null) Plugin.Log.LogWarning($"Trail sheet not found: {png}");
            return baseSpacId;
        }
        if (!SpacDataBase.SpacMap.TryGetValue(baseSpacId, out Spac baseSpac)
            || baseSpac.SpriteSheets == null || baseSpac.SpriteSheets.Length == 0)
            return baseSpacId;
        try
        {
            SpriteSheet[] bs = baseSpac.SpriteSheets;
            using var fs = File.OpenRead(png);
            var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
            var sheets = (SpriteSheet[])bs.Clone();                                 // keep any extra sheets as-is
            sheets[0] = new SpriteSheet(tex, bs[0].NormalMap, bs[0].MetalMap, bs[0].SpriteWidth, bs[0].SpriteHeight);
            Spac clone = baseSpac.GetCopyWithOtherSpriteSheets(sheets);
            clone.Name = newId;
            SpacDataBase.SpacMap[newId] = clone;
            Plugin.Log.LogInfo($"Content Forge: built custom trail '{newId}' from '{baseSpacId}' " +
                $"(vanilla sheet '{bs[0].Texture?.Name}', frame {bs[0].SpriteWidth}x{bs[0].SpriteHeight}; " +
                $"your sheet {tex.Width}x{tex.Height}).");
            return newId;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom trail '{newId}' build failed: {e.Message}");
            SpacDataBase.SpacMap[newId] = baseSpac;   // cache the fallback so we don't retry every swing
            return baseSpacId;
        }
    }

    // Build a projectile damage spell. Reuses a built-in SpellTypeId (default single_projectile_attack)
    // so no new spell behavior is needed; damage lives in AttackSpellArgs, visuals/sound in the display/
    // trail/sound ids (all reused vanilla ids — a missing one renders nothing, never crashes).
    private static bool TryBuildSpell(SpellDef d, string pack, out SpellDataModel sm)
    {
        sm = null;
        if (d == null || string.IsNullOrWhiteSpace(d.Id))
        {
            Plugin.Log.LogWarning($"[{pack}] spell with empty id skipped.");
            return false;
        }

        // Spell behavior: a Summon spell spawns a creature (necromancer); an ApplyAura spell (e.g. ignite) is an
        // instant single-target aura applier; otherwise it's a projectile. (If the author overrode spellTypeId, honor it.)
        bool isSummon = d.Summon != null && !string.IsNullOrWhiteSpace(d.Summon.Creature);
        bool isAura = !isSummon && !string.IsNullOrEmpty(d.ApplyAura);
        string spellTypeId = d.SpellTypeId;
        object typeArgs;
        if (isSummon)
        {
            if (!TryResolveCreature(d.Summon.Creature, out Guid baseId))
            {
                Plugin.Log.LogWarning($"[{pack}] spell '{d.Id}': unknown creature '{d.Summon.Creature}' (see reference/creature-ids.txt).");
                return false;
            }
            spellTypeId = "spelltype:summon_creature";
            Enum.TryParse(d.Summon.EntityType ?? "PlayerSummon", ignoreCase: true, out EntityType et);
            // Tag every spawn with the spell id so SummonFactionPatch can apply faction/count/cap to it.
            var summonParams = d.Summon.Parameters != null
                ? new Dictionary<string, string>(d.Summon.Parameters) : new Dictionary<string, string>();
            summonParams["cf-summon"] = d.Id;
            typeArgs = new SummonCreatureSpellArgs
            {
                SpawnBaseId = baseId,
                EntityType = et,
                IsDynamicSpawn = true,
                Parameters = summonParams,
            };
            if (d.Summon.Count > 1) SummonCounts[d.Id] = d.Summon.Count;
            if (d.Summon.MaxActive > 0) SummonCaps[d.Id] = d.Summon.MaxActive;
            if (et == EntityType.PlayerSummon || et == EntityType.Friendly) AllySummonSpells.Add(d.Id);
            if (string.Equals(d.Summon.Behavior, "companion", StringComparison.OrdinalIgnoreCase)) CompanionSpells.Add(d.Id);
            if (d.Summon.DismissOnUnequip) DismissSpells[d.Id] = new HashSet<string>();
        }
        else if (isAura)
        {
            if (spellTypeId == "spelltype:single_projectile_attack") spellTypeId = "spelltype:instant_single_target";
            typeArgs = new InstantSingleTargetSpellArgs
            {
                ApplyAura = new SpellApplyAuraContext { AuraId = d.ApplyAura },
            };
        }
        else
        {
            typeArgs = new SingleProjectileSpellArgs
            {
                RelativeCastPosition = new Vector3(0f, 0f, 1f),
                ProjectileVelocity = d.ProjectileVelocity,
                UseParticleTrail = !string.IsNullOrEmpty(d.ParticleTrail),
                ParticleTrailEmitterId = d.ParticleTrail,
                ProjectileCollisionCircleRadius = d.CollisionRadius,
            };
        }

        AttackSpellArgs attack = null;
        if (d.Attack != null)
        {
            Enum.TryParse(d.Attack.AttackType ?? "Magic", ignoreCase: true, out AttackType at);
            attack = new AttackSpellArgs
            {
                SkillTypeBonusDamage = string.IsNullOrWhiteSpace(d.Attack.SkillId) ? null : d.Attack.SkillId,
                BaseMinDamage = BuildDamage(d.Attack.MinDamage),
                BaseMaxDamage = BuildDamage(d.Attack.MaxDamage),
                DamageMultiplier = d.Attack.Multiplier <= 0f ? 1f : d.Attack.Multiplier,
                AttackType = at,
                CanCrit = d.Attack.CanCrit,
                MaxTargets = d.Attack.MaxTargets <= 0 ? 1 : d.Attack.MaxTargets,
                CanKnockback = d.Attack.Knockback > 0f,
                BaseKnockback = d.Attack.Knockback,
            };
        }

        sm = new SpellDataModel
        {
            Id = d.Id,
            DisplayName = string.IsNullOrEmpty(d.DisplayName) ? d.Id : d.DisplayName,
            SpellTypeId = spellTypeId,
            ProjectileDisplayId = string.IsNullOrEmpty(d.ProjectileDisplayId) ? null : d.ProjectileDisplayId,
            CastSoundEffect = string.IsNullOrEmpty(d.CastSound) ? null : d.CastSound,
            SpellDuration = d.Duration,
            SpellTypeArgs = typeArgs,
            AttackSpellArgs = attack,
            CanHit = ProjectileHitFlags.NoSharedFaction,
        };

        // SpellsPlus: aoe/explosion/cone/line/multiProjectile/heal/massHeal blocks override the
        // default projectile behavior (summon/applyAura win; exactly one block allowed).
        if (!SpellsPlus.TryApply(d, pack, sm)) return false;

        if (d.Requires != null) SpellRequirements[d.Id] = d.Requires;

        // Chain spells: when this spell's projectile hits (or ends), cast another spell at a target — e.g. an
        // arrow that spawns lightning on impact, or a fireball that ignites. Each chained spell is just another
        // spell (yours or vanilla).
        if (d.Chain != null && d.Chain.Count > 0)
        {
            var chains = new List<(SpellEvent, string, Target)>();
            foreach (ChainDef c in d.Chain)
            {
                if (string.IsNullOrWhiteSpace(c?.Spell)) continue;
                Enum.TryParse(c.Event ?? "OnAnyHit", ignoreCase: true, out SpellEvent ev);
                Enum.TryParse(c.Target ?? "Target", ignoreCase: true, out Target tg);
                chains.Add((ev, c.Spell, tg));
            }
            if (chains.Count > 0) sm.ChainSpells = chains.ToArray();
        }
        return true;
    }

    // Resolve a creature name (a field on the game's Shared.EntityIds, e.g. "JumpingSkull") to its base Guid.
    private static bool TryResolveCreature(string name, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;
        // custom creature (a pack's `creatures` id) takes precedence over a vanilla EntityIds name
        if (CustomCreatureGuids.TryGetValue(name, out id)) return true;
        var f = AccessTools.Field(typeof(EntityIds), name);
        if (f == null || f.FieldType != typeof(Guid)) return false;
        id = (Guid)f.GetValue(null);
        return true;
    }

    // Stable Guid from a string id (MD5-based, deterministic across machines → co-op-safe).
    private static Guid GuidFromId(string id)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id)));
    }

    // Damage-by-type dict -> the game's 9-slot DamageTypesArray (true,slash,pierce,bludgeon,pyro,chloro,aqua,cosmo,necro).
    private static DamageTypesArray BuildDamage(Dictionary<string, float> dmg)
    {
        var arr = new float[9];
        if (dmg != null)
            foreach (var kv in dmg)
            {
                int i = DamageIndex(kv.Key);
                if (i >= 0) arr[i] = kv.Value;
            }
        return new DamageTypesArray(arr);
    }

    private static int DamageIndex(string key) => (key ?? "").ToLowerInvariant() switch
    {
        "true" => 0,
        "slashing" => 1, "slash" => 1,
        "piercing" => 2, "pierce" => 2,
        "bludgeoning" => 3, "bludgeon" => 3,
        "pyro" => 4, "fire" => 4,
        "chloro" => 5, "nature" => 5,
        "aqua" => 6, "water" => 6,
        "cosmo" => 7,
        "necro" => 8,
        _ => -1,
    };

    // (Re)register our display strings, e.g. after the language loader cleared the table.
    public static void ApplyStrings()
    {
        // Pick the current game language for any string that has localized variants; otherwise the fallback.
        string lang = Globals.LanguageCode ?? "en";
        foreach (var kv in _strings)
        {
            string text = kv.Value;
            if (_locStrings.TryGetValue(kv.Key, out var byLang) && byLang != null
                && byLang.TryGetValue(lang, out var t) && !string.IsNullOrEmpty(t))
                text = t;
            StringDefinitions.Strings[kv.Key] = new StringDefinition(text);
        }
    }

    private static bool TryBuildItem(ItemDef d, string pack, out ItemData data)
    {
        data = null;
        if (d == null || string.IsNullOrWhiteSpace(d.Id))
        {
            Plugin.Log.LogWarning($"[{pack}] item with empty id skipped.");
            return false;
        }

        var item = new ItemData
        {
            Id = d.Id,
            // The game displays item names/descriptions via the "<id>*item:name" / "*item:description"
            // string-key convention (NOT ItemData.Name's own value). We set those keys here and register
            // the actual text in RegisterItemStrings so both the tooltip and the spawner resolve.
            Name = d.Id + ItemDataBase.SuffixName,
            Icon = string.IsNullOrEmpty(d.Icon) ? "sam" : d.Icon,
            MaxStackSize = d.MaxStack <= 0 ? 1 : d.MaxStack,
            Tier = d.Tier,
            Flags = ParseFlags(d.Flags, pack, d.Id),
        };
        if (!string.IsNullOrEmpty(d.Description)) item.Description = d.Id + ItemDataBase.SuffixDescription;
        if (d.Price.HasValue) item.Tradable = new TradableItem { Price = (ulong)Math.Max(0L, d.Price.Value) };
        if (d.Light != null) item.LightSource = new LightSourceItem { Radius = d.Light.Radius, Strength = d.Light.Strength };

        if (d.Equippable != null)
        {
            if (!Enum.TryParse(d.Equippable.EquipmentType, ignoreCase: true, out EquipmentType et) || et == EquipmentType.Invalid)
            {
                Plugin.Log.LogWarning($"[{pack}] item '{d.Id}': bad equipmentType '{d.Equippable.EquipmentType}' — skipped.");
                return false;
            }
            Enum.TryParse(d.Equippable.Material ?? "Steel", ignoreCase: true, out EquipmentMaterial mat);
            var equippable = new EquippableItem
            {
                EquipmentType = et,
                Material = mat,
                EntityAuraId = string.IsNullOrEmpty(d.Equippable.EntityAuraId) ? null : d.Equippable.EntityAuraId,
                StatBonuses = BuildStatBonuses(d.Equippable.StatBonuses),
                DisplayId = string.IsNullOrEmpty(d.Equippable.DisplayId) ? null : d.Equippable.DisplayId,
            };
            // Custom held look: recolor an existing shape, OR inject your own sprite sheet. Overrides DisplayId.
            var disp = d.Equippable.Display;
            if (disp != null && (!string.IsNullOrEmpty(disp.SkinName) || !string.IsNullOrEmpty(disp.Sheet)))
            {
                string ddId = "cf:dd:" + d.Id;
                CharacterDisplayFragment frag;
                if (!string.IsNullOrEmpty(disp.Sheet))
                {
                    // Custom art: register a new skin from the PNG; the fragment uses NO palette (use-as-authored).
                    string skinName = !string.IsNullOrEmpty(disp.SkinName) ? disp.SkinName : "cf_" + d.Id.Replace(':', '_');
                    _customSkins.Add((disp.SkinTag, skinName, Path.Combine(Plugin.PacksDir, disp.Sheet),
                        disp.FrameWidth <= 0 ? 48 : disp.FrameWidth, disp.FrameHeight <= 0 ? 48 : disp.FrameHeight));
                    frag = new CharacterDisplayFragment(disp.SkinTag, skinName);
                }
                else
                {
                    frag = new CharacterDisplayFragment(disp.SkinTag, disp.SkinName, disp.Palette ?? "weapon", disp.PaletteRow);
                }
                _displayData.Add(new CharacterDisplayData { Id = ddId, Fragments = new[] { frag } });
                equippable.DisplayId = ddId;
            }
            // Melee/ranged weapon: damage + behavior from WeaponStats.
            if (d.Equippable.Weapon != null)
            {
                var w = d.Equippable.Weapon;
                equippable.WeaponStats = new WeaponStats
                {
                    // a custom skillId trains/scales the pack's own skill instead of the class default
                    Class = WithSkill(ParseWeaponClass(w.Class), w.SkillId),
                    SwingTimer = w.SwingTimer <= 0f ? 1f : w.SwingTimer,
                    DamageRanges = new DamageTypeRanges
                    {
                        MinDamage = BuildDamage(w.MinDamage),
                        MaxDamage = BuildDamage(w.MaxDamage),
                    },
                    BaseKnockback = w.Knockback,
                    // Melee reach. 0 = a zero-width cone that hits nothing (silent "no damage" trap), so fall
                    // back to a sane default when unset. (Ranged weapons fire a projectile regardless.)
                    BaseAttackRange = w.AttackRange <= 0f ? 24f : w.AttackRange,
                    EnergyCost = w.EnergyCost,
                    SpecialEnergyCost = w.SpecialEnergyCost,
                    StunPower = w.StunPower,
                    MovementFactor = w.MovementFactor,
                };
                Enum.TryParse(w.DamageVfx ?? "Normal", ignoreCase: true, out DamageVfxModifier dvm);
                equippable.WeaponStats.DamageVfxModifier = dvm;
                // Weapon casts a spell: OnHit = procs when it connects (ignite-on-hit); OnUse = casts every
                // attack/swing (a staff — keep a melee class for the swing animation). Optional proc chance,
                // who it targets (the enemy or yourself, for lifesteal-style buffs), and a stronger charged variant.
                if (w.CastSpell != null && !string.IsNullOrEmpty(w.CastSpell.SpellId))
                {
                    var c = w.CastSpell;
                    Enum.TryParse(c.CastType ?? "OnHit", ignoreCase: true, out CastType ct);
                    Enum.TryParse(c.Event ?? "OnEntityHit", ignoreCase: true, out SpellEvent ev);
                    Enum.TryParse(c.Target ?? "Target", ignoreCase: true, out Target tgt);
                    equippable.WeaponStats.CastSpellTomeArgs = new CastSpellTomeArgs
                    { CastType = ct, SpellEvent = ev, Probability = c.Probability };
                    equippable.WeaponStats.SpellTomeArgs = new SpellTomeArgs
                    {
                        SpellId = c.SpellId,
                        Target = tgt,
                        ChargedSpellId = string.IsNullOrEmpty(c.ChargedSpellId) ? null : c.ChargedSpellId,
                        ChargeTime = c.ChargeTime,
                        ChargedTarget = tgt,
                    };
                    if (ct == CastType.OnUse) OnUseSpellIds.Add(c.SpellId);
                }
                if (!string.IsNullOrEmpty(w.TrailColor) && TryParseColor(w.TrailColor, out Vector4 tc))
                    TrailColors[d.Id] = tc;
                if (!string.IsNullOrEmpty(w.TrailSheet))
                    TrailSheets[d.Id] = Path.Combine(Plugin.PacksDir, w.TrailSheet);
                if (w.HideTrail) HideTrails.Add(d.Id);
            }
            // Shield: blocks damage when raised (equip Offhand).
            if (d.Equippable.Shield != null)
            {
                var s = d.Equippable.Shield;
                if (equippable.WeaponStats == null)
                    equippable.WeaponStats = new WeaponStats { Class = WeaponClass.Shield };
                equippable.ShieldStats = new ShieldStats
                {
                    BlockStrength = s.BlockStrength,
                    BlockArcSize = MathHelper.ToRadians(s.BlockArcDegrees),
                    StrongBlockArcSize = MathHelper.ToRadians(s.StrongBlockArcDegrees),
                    EnterCost = s.EnterCost,
                };
            }
            // Offhand spell-tome / scroll: equip + attack casts the spell.
            if (d.Equippable.SpellTome != null && !string.IsNullOrEmpty(d.Equippable.SpellTome.SpellId))
            {
                var t = d.Equippable.SpellTome;
                Enum.TryParse(t.Target ?? "Target", ignoreCase: true, out Target tomeTgt);
                equippable.WeaponStats = new WeaponStats
                {
                    SwingTimer = t.SwingTimer,
                    Class = WeaponClass.SpellTome,
                    SpellTomeArgs = new SpellTomeArgs
                    {
                        SpellId = t.SpellId,
                        Target = tomeTgt,
                        ChargedTarget = tomeTgt,
                        ChargedSpellId = string.IsNullOrEmpty(t.ChargedSpellId) ? null : t.ChargedSpellId,
                        ChargeTime = t.ChargeTime,
                    },
                    EnergyCost = t.EnergyCost,
                    SpecialEnergyCost = t.SpecialEnergyCost,
                    MovementFactor = t.MovementFactor,
                };
            }
            item.Equippable = equippable;
        }

        if (d.Usable != null)
        {
            Enum.TryParse(d.Usable.Type ?? "Special", ignoreCase: true, out UsableItem.UsableType ut);
            var usable = new UsableItem
            {
                Type = ut,
                SpellId = d.Usable.SpellId,
                UsesMax = d.Usable.UsesMax,
                Cooldown = d.Usable.Cooldown,
            };
            // Heal shortcut: a Healing usable with heal amounts becomes an instant heal/food potion
            // (casts spell:heal). Mirrors the game's own consumable potions — usable + hotbar-able.
            if (ut == UsableItem.UsableType.Healing && (d.Usable.InstantHeal > 0f || d.Usable.SlowHeal > 0f))
            {
                if (string.IsNullOrEmpty(usable.SpellId)) usable.SpellId = "spell:heal";
                usable.SpellTypeArgs = new HealSpellArgs
                {
                    InstantHeal = d.Usable.InstantHeal,
                    SlowHeal = d.Usable.SlowHeal,
                    SlowHealTime = d.Usable.SlowHealTime,
                };
            }
            item.Usable = usable;
        }

        data = item;
        return true;
    }

    // Convert the pack's stat-bonus dict into the game's Dictionary<string, StatModificationData>.
    private static Dictionary<string, StatModificationData> BuildStatBonuses(Dictionary<string, StatBonusDef> src)
    {
        if (src == null || src.Count == 0) return null;
        var result = new Dictionary<string, StatModificationData>();
        foreach (var kv in src)
        {
            var b = kv.Value ?? new StatBonusDef();
            result[kv.Key] = new StatModificationData
            {
                Additive = b.Additive,
                Multiplier = b.Multiplier,
                AdditiveMultiplier = b.AdditiveMultiplier,
                BaseMultiplier = b.BaseMultiplier,
                BonusMultiplier = b.BonusMultiplier,
            };
        }
        return result;
    }

    private static bool TryBuildRecipe(RecipeDef d, string pack, out ItemRecipe r)
    {
        r = null;
        if (d == null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Crafted))
        {
            Plugin.Log.LogWarning($"[{pack}] recipe missing id/crafted — skipped.");
            return false;
        }
        var ings = new List<ItemAmount>();
        if (d.Ingredients != null)
            foreach (var kv in d.Ingredients) ings.Add(new ItemAmount(kv.Key, kv.Value));
        r = new ItemRecipe
        {
            Id = d.Id,
            CraftedItemId = d.Crafted,
            CraftedAmount = d.Amount <= 0 ? 1 : d.Amount,
            ItemsAmounts = ings.ToArray(),
            RequiredCraftingStation = string.IsNullOrEmpty(d.Station) ? null : d.Station,
        };
        return true;
    }

    // Swap a weapon's class for one that trains a CUSTOM skill (same name/animation type, different SkillId).
    private static WeaponClass WithSkill(WeaponClass cls, string skillId)
        => string.IsNullOrWhiteSpace(skillId) ? cls : new WeaponClass(cls.Name, skillId, cls.Type);

    // Map a weapon-class name to the game's WeaponClass static (it's a struct, not an enum).
    private static WeaponClass ParseWeaponClass(string s) => (s ?? "").ToLowerInvariant() switch
    {
        "sword" => WeaponClass.Sword,
        "spear" => WeaponClass.Spear,
        "dagger" => WeaponClass.Dagger,
        "sledgehammer" => WeaponClass.Sledgehammer,
        "bow" => WeaponClass.Bow,
        "crossbow" => WeaponClass.Crossbow,
        "javelin" => WeaponClass.Javelin,
        "fists" => WeaponClass.Fists,
        "shield" => WeaponClass.Shield,
        "spelltome" => WeaponClass.SpellTome,
        _ => WeaponClass.Sword,
    };

    // Parse "#RRGGBB" or "#RRGGBBAA" into a Vector4 (r,g,b,a in 0..1) for trail tinting.
    private static bool TryParseColor(string hex, out Vector4 color)
    {
        color = Vector4.One;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;
        color = new Vector4(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        return true;
    }

    private static ItemFlag ParseFlags(string flags, string pack, string id)
    {
        if (string.IsNullOrWhiteSpace(flags)) return ItemFlag.NoFlags;
        if (Enum.TryParse(flags, ignoreCase: true, out ItemFlag f)) return f;
        Plugin.Log.LogWarning($"[{pack}] item '{id}': unknown flags '{flags}' — ignored.");
        return ItemFlag.NoFlags;
    }

    // Drop an example pack (+ a demo custom icon) on first run so creators have a working template.
    public static void WriteSampleIfEmpty(string dir)
    {
        try
        {
            if (Directory.GetFiles(dir, "*.json").Length > 0) return;
            File.WriteAllText(Path.Combine(dir, "sample.json"), Sample);
            File.WriteAllBytes(Path.Combine(dir, "cf_test.png"), Convert.FromBase64String(SamplePngBase64));
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Could not write sample pack: {e.Message}"); }
    }

    // An 18x18 demo icon (matches the game's native item-icon size; magenta disc, gold center, with a
    // transparent margin like real icons) so the sample shows a correctly-sized custom icon.
    private const string SamplePngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUA" +
        "AAAJcEhZcwAADsMAAA7DAcdvqGQAAABISURBVDhP7YyxDQAgDMP6/4OcEzakukQVK2DJW5yI91BIO7mzMHSy" +
        "KzBwsktw3Ml+waFGZP9RPWpkn+DYya7AwMnOwvD44B4mnCxwHFbn9acAAAAASUVORK5CYII=";

    private const string Sample = @"{
  ""id"": ""sample"",
  ""namespace"": ""cf"",
  ""icons"": {
    ""cf:test_icon"": ""cf_test.png""
  },
  ""spells"": [
    {
      ""id"": ""cf:zap"", ""displayName"": ""Arcane Zap"",
      ""projectileDisplayId"": ""arrow_spell"",
      ""castSound"": ""event:/spells/cast_spell_cosmo"",
      ""particleTrail"": ""small_fireball_blue"",
      ""attack"": { ""multiplier"": 1, ""attackType"": ""Magic"", ""maxTargets"": 1,
        ""minDamage"": { ""cosmo"": 15 }, ""maxDamage"": { ""cosmo"": 25 } }
    },
    {
      ""id"": ""cf:ignite"", ""displayName"": ""Ignite"",
      ""applyAura"": ""aura:general:burning""
    },
    {
      ""id"": ""cf:fireball"", ""displayName"": ""Fireball"",
      ""projectileDisplayId"": ""fire_spell_new"",
      ""particleTrail"": ""small_fireball_blue"",
      ""castSound"": ""event:/spells/cast_spell_cosmo"",
      ""attack"": { ""attackType"": ""Magic"", ""maxTargets"": 1,
        ""minDamage"": { ""pyro"": 40 }, ""maxDamage"": { ""pyro"": 70 } }
    }
  ],
  ""items"": [
    {
      ""id"": ""cf:sample_custom"",
      ""name"": ""Forge Sigil (custom icon)"",
      ""icon"": ""cf:test_icon"",
      ""maxStack"": 99
    },
    {
      ""id"": ""cf:scroll_zap"",
      ""name"": ""Scroll of Zapping"",
      ""icon"": ""scroll:brown"",
      ""maxStack"": 1,
      ""equippable"": { ""equipmentType"": ""Offhand"", ""spellTome"": { ""spellId"": ""cf:zap"", ""energyCost"": 0 } }
    },
    {
      ""id"": ""cf:sword"",
      ""name"": ""Forge Gladius"",
      ""icon"": ""sword:iron"",
      ""maxStack"": 1,
      ""tier"": 5,
      ""equippable"": {
        ""equipmentType"": ""Weapon"", ""material"": ""Iron"", ""displayId"": ""cdd:iron_sword"",
        ""weapon"": { ""class"": ""Sword"", ""swingTimer"": 0.4, ""knockback"": 60, ""attackRange"": 26,
          ""minDamage"": { ""slashing"": 100 }, ""maxDamage"": { ""slashing"": 150 } }
      }
    },
    {
      ""id"": ""cf:flamesword"",
      ""name"": ""Flamebrand"",
      ""icon"": ""sword:iron"",
      ""maxStack"": 1,
      ""tier"": 6,
      ""equippable"": {
        ""equipmentType"": ""Weapon"", ""material"": ""Gold"", ""displayId"": ""cdd:iron_sword"",
        ""weapon"": { ""class"": ""Sword"", ""swingTimer"": 0.5, ""knockback"": 50, ""attackRange"": 26,
          ""minDamage"": { ""pyro"": 60 }, ""maxDamage"": { ""pyro"": 90 },
          ""castSpell"": { ""spellId"": ""cf:ignite"", ""castType"": ""OnHit"" } }
      }
    },
    {
      ""id"": ""cf:firestaff"",
      ""name"": ""Forge Firestaff"",
      ""icon"": ""sword:iron"",
      ""maxStack"": 1,
      ""tier"": 6,
      ""equippable"": {
        ""equipmentType"": ""Weapon"", ""material"": ""Gold"", ""displayId"": ""cdd:iron_sword"",
        ""weapon"": { ""class"": ""Sword"", ""swingTimer"": 0.6, ""attackRange"": 20,
          ""minDamage"": { ""bludgeoning"": 5 }, ""maxDamage"": { ""bludgeoning"": 10 },
          ""castSpell"": { ""spellId"": ""cf:fireball"", ""castType"": ""OnUse"", ""event"": ""OnAnyHit"" },
          ""hideTrail"": true }
      }
    },
    {
      ""id"": ""cf:crossbow"",
      ""name"": ""Forge Arcuballista"",
      ""icon"": ""crossbow"",
      ""maxStack"": 1,
      ""tier"": 5,
      ""equippable"": {
        ""equipmentType"": ""Offhand"", ""displayId"": ""cdd:crossbow_leather"",
        ""weapon"": { ""class"": ""Crossbow"", ""swingTimer"": 0.4, ""knockback"": 20, ""energyCost"": 0,
          ""minDamage"": { ""piercing"": 80 }, ""maxDamage"": { ""piercing"": 120 } }
      }
    },
    {
      ""id"": ""cf:shield"",
      ""name"": ""Forge Aegis"",
      ""icon"": ""shield:iron"",
      ""maxStack"": 1,
      ""tier"": 5,
      ""price"": 500,
      ""equippable"": {
        ""equipmentType"": ""Offhand"", ""displayId"": ""cdd:shield_iron"",
        ""shield"": { ""blockStrength"": 0.8, ""blockArcDegrees"": 70 }
      }
    },
    {
      ""id"": ""cf:sample_trinket"",
      ""name"": ""Forge Token"",
      ""description"": ""A test trinket: +100 Health, +25% Movement Speed."",
      ""icon"": ""ring:red"",
      ""maxStack"": 1,
      ""tier"": 4,
      ""equippable"": {
        ""equipmentType"": ""Trinket"", ""material"": ""Gold"",
        ""statBonuses"": { ""Health"": { ""additive"": 100 }, ""MovementSpeed"": { ""additive"": 0.25 } }
      }
    },
    {
      ""id"": ""cf:sample_food"",
      ""name"": ""Forge Ration"",
      ""description"": ""A test food that heals when eaten."",
      ""icon"": ""apple:red"",
      ""maxStack"": 20,
      ""flags"": ""Food"",
      ""usable"": { ""type"": ""Healing"", ""instantHeal"": 50, ""cooldown"": 1 }
    }
  ],
  ""recipes"": [
    { ""id"": ""cf:recipe_ration"", ""crafted"": ""cf:sample_food"", ""amount"": 5,
      ""ingredients"": { ""food:apple"": 1 } }
  ]
}
";
}
