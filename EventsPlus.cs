using CandideServer;                          // ServerGameState
using CandideServer.Entities;                 // ServerEntityModel
using CandideServer.MessageModels;            // ChatLogMessage
using CandideServer.Models;                   // ChatMessageType
using CandideServer.Models.Quests.EventModels; // OnEntityKilledEvent (the quest kill-credit funnel)
using CandideServer.Models.Town;              // ServerTownModel
using CandideServer.ServerControllers;        // SpawnEntityArgs
using CandideServer.ServerManagers;           // EntityServerManager, RaidsServerManager, TownsServerManager, ...
using CandideServer.ServerServices;           // PlayerServerService, ChatLogServerService, ServerJoinedPlayerModel
using CandideServer.ServerSystems;            // TimeServerSystem
using HarmonyLib;
using Microsoft.Xna.Framework;                // Vector2/Vector3
using Shared;                                 // EntityIds
using Shared.Data;                            // RaidDataBase
using Shared.Entity;                          // EntityType, EntityRemoveInfo, EntityRemoveType
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// (EventDef/WaveDef live in ContentPack.cs — the Validator compiles ContentPack standalone.)

// Server-side event TYPES on top of the EventsFeature machinery (Events.cs). Four types, all host-only;
// clients only ever see chat lines and the spawned entities/raids themselves (plain vanilla netcode):
//
//   waveDefense   — timed creature waves spawned in a pack map's world; clear the last wave and every
//                   player still inside is rewarded.
//   bossRush      — same skeleton, but one "boss" creature at a time; the next spawns when the last dies.
//   huntContest   — global kill contest for durationSeconds; most kills (optionally of one target creature)
//                   wins the reward. Starts on a schedule edge, or whenever enough players are online.
//   scheduledRaid — fires a VANILLA raid (RaidsServerManager.CreateRaid) at a town every N days, on the
//                   newDay/night/day edge — town picked like a war declaration (closest to a player,
//                   weighted fallback).
//
// Scheduling: EventsPlusFeature.Tick(dt) chains after EventsFeature.Tick in CompanionPatch's existing
// EntityServerSystem.Update postfix — no extra per-tick patch. Heavy work runs once per second.
//
// Kill detection REUSES the game's own quest kill-credit funnel: every creature death the quest system can
// see goes through OnEntityKilledEvent.Send(killedId, killedBaseId, killerEntityId, ...) — the same event
// KillEntityQuestObjective subscribes to. killerEntityId is the killing player's character-entity Guid on
// the client-resolved death paths ("entity-die-and-spawn-world-items"); the server-sim path passes no
// killer, exactly as vanilla quests experience it (documented limit: DoT/summon finishes aren't credited).
// Waves/boss rush don't need attribution at all — they track their own spawned entity Guids, pruned both
// by the kill event and by a once-per-second liveness scan (covers despawn paths that skip the event).
internal static class EventsPlusFeature
{
    private enum EvType { WaveDefense, BossRush, HuntContest, ScheduledRaid }
    private enum EvState { Idle, Running, Cooldown }
    private enum Sched { None, Night, Day, NewDay }

    private sealed class WavePlan
    {
        public Dictionary<string, int> Creatures;   // creature id -> count (resolved lazily at spawn)
        public float Delay;                         // seconds before this wave spawns
        public string Announce;                     // chat line on spawn (null = generic)
    }

    private sealed class EventRuntime
    {
        public EventDef Def;
        public EvType Type;
        public Sched Schedule;
        public EvState State = EvState.Idle;
        public bool Disabled;                                  // hard config error found at runtime — never run again
        public Guid WorldId;                                   // wave/boss: world the run is bound to
        public double NextAt;                                  // generic deadline (wave delay / hunt end / cooldown end)
        public int WaveIdx = -1;                               // index of the wave currently fighting
        public bool BetweenWaves;                              // waiting out the delay before WaveIdx+1
        public List<WavePlan> Plan;                            // wave/boss spawn plan
        public readonly HashSet<Guid> Alive = new();           // entity ids we spawned that still live
        public readonly Dictionary<string, int> Kills = new(); // hunt: player name -> kills
        public readonly Dictionary<string, double> LastKill = new();
        public Guid TargetBaseId;                              // hunt: resolved target (Guid.Empty = any creature)
        public int LastRaidDay = int.MinValue;                 // scheduledRaid pacing
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Def.Name)) return Def.Name;
                int i = Def.Id.LastIndexOf(':');
                return i >= 0 ? Def.Id.Substring(i + 1) : Def.Id;
            }
        }
    }

    private static readonly List<EventRuntime> _events = new();
    private static readonly Dictionary<Guid, string> _worldMapCache = new();   // worldId -> room DataId (map id)
    private static readonly List<Vector3> _anchors = new();    // reused: player spawn anchors
    private static readonly List<Guid> _gone = new();          // reused: Alive ids to prune
    private static readonly Random _rng = new();
    private static double _now;           // server seconds, accumulated from Tick dt
    private static float _scanTimer;
    private const float ScanInterval = 1f;

    // day/night edge detection (mirrors TimeServerSystem.Update's LastDayStarted logic)
    private static bool _timeInit;
    private static int _prevDay;
    private static bool _prevNight;

    // ---- registration (called by ContentLoader while parsing packs) ----

    public static void RegisterEvent(EventDef def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Id) || string.IsNullOrWhiteSpace(def.Type)) return;
        foreach (var e in _events) if (e.Def.Id == def.Id) return;

        EvType type;
        switch (def.Type.Trim().ToLowerInvariant())
        {
            case "wavedefense": type = EvType.WaveDefense; break;
            case "bossrush": type = EvType.BossRush; break;
            case "huntcontest": type = EvType.HuntContest; break;
            case "scheduledraid": type = EvType.ScheduledRaid; break;
            default:
                Plugin.Log.LogWarning($"Event '{def.Id}' skipped: unknown type '{def.Type}' " +
                    "(waveDefense / bossRush / huntContest / scheduledRaid).");
                return;
        }

        Sched sched = Sched.None;
        if (!string.IsNullOrWhiteSpace(def.Schedule))
            switch (def.Schedule.Trim().ToLowerInvariant())
            {
                case "night": sched = Sched.Night; break;
                case "day": sched = Sched.Day; break;
                case "newday": sched = Sched.NewDay; break;
                default:
                    Plugin.Log.LogWarning($"Event '{def.Id}': unknown schedule '{def.Schedule}' (night/day/newDay) — ignored.");
                    break;
            }

        var rt = new EventRuntime { Def = def, Type = type, Schedule = sched };
        if (def.MinPlayers < 1) def.MinPlayers = 1;
        if (def.RewardAmount < 1) def.RewardAmount = 1;

        switch (type)
        {
            case EvType.WaveDefense:
                if (string.IsNullOrWhiteSpace(def.Map) || def.Waves == null || def.Waves.Count == 0)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: waveDefense needs map + waves."); return; }
                rt.Plan = new List<WavePlan>();
                foreach (WaveDef w in def.Waves)
                    if (w?.Creatures != null && w.Creatures.Count > 0)
                        rt.Plan.Add(new WavePlan
                        {
                            Creatures = w.Creatures,
                            Delay = w.DelaySeconds < 1 ? 1f : w.DelaySeconds,
                            Announce = null,   // generic "Wave i/N" line
                        });
                if (rt.Plan.Count == 0)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: no wave has creatures."); return; }
                break;

            case EvType.BossRush:
                if (string.IsNullOrWhiteSpace(def.Map) || def.Bosses == null || def.Bosses.Count == 0)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: bossRush needs map + bosses."); return; }
                rt.Plan = new List<WavePlan>();
                foreach (string b in def.Bosses)
                    if (!string.IsNullOrWhiteSpace(b))
                        rt.Plan.Add(new WavePlan
                        {
                            Creatures = new Dictionary<string, int> { [b] = 1 },
                            Delay = rt.Plan.Count == 0 ? 8f : 6f,   // breather between bosses
                            Announce = $"{CreatureLabel(b)} enters the fray!",
                        });
                if (rt.Plan.Count == 0)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: bosses list is empty."); return; }
                break;

            case EvType.HuntContest:
                if (def.DurationSeconds < 10) def.DurationSeconds = 600;
                if (def.CooldownSeconds < 30) def.CooldownSeconds = 300;
                break;

            case EvType.ScheduledRaid:
                if (string.IsNullOrWhiteSpace(def.RaidData))
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: scheduledRaid needs raidData (see reference list)."); return; }
                if (rt.Schedule == Sched.None) rt.Schedule = Sched.NewDay;
                if (def.EveryNDays < 1) def.EveryNDays = 1;
                break;
        }

        _events.Add(rt);
        Plugin.Log.LogInfo($"Event '{def.Id}' registered ({def.Type}" +
            (def.Map != null ? $", map '{def.Map}'" : "") +
            (rt.Schedule != Sched.None ? $", schedule {rt.Schedule}" : "") + ").");
    }

    // ---- per-server-tick entry point (chained after EventsFeature.Tick in CompanionPatch's postfix) ----

    public static void Tick(float dt)
    {
        if (_events.Count == 0) return;
        try
        {
            _now += dt;
            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;
            Scan();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"EventsPlus tick failed: {e.Message}"); }
    }

    // ---- once-per-second state machine ----

    private static void Scan()
    {
        // Reload / empty server: abort everything to Idle and forget the time baseline so a save with a
        // different clock can't fire spurious newDay/night edges on the first scan back.
        if (PlayerServerService.JoinedPlayers.Count == 0)
        {
            foreach (var rt in _events)
                if (rt.State == EvState.Running || rt.State == EvState.Cooldown) Abort(rt);
            _timeInit = false;
            return;
        }

        // Day/night edges (mirrors TimeServerSystem.Update: "new day" = day number changed AND it's daytime;
        // _prevDay only advances when the edge fires, like LastDayStarted).
        int day = TimeServerSystem.Day;
        bool night = TimeServerSystem.IsNight;
        bool edgeNewDay = false, edgeNight = false, edgeDay = false;
        if (!_timeInit) { _timeInit = true; _prevDay = day; _prevNight = night; }
        else
        {
            edgeNewDay = day != _prevDay && !night;
            edgeNight = night && !_prevNight;
            edgeDay = !night && _prevNight;
            if (edgeNewDay) _prevDay = day;
            _prevNight = night;
        }

        foreach (EventRuntime rt in _events)
        {
            if (rt.Disabled) continue;
            try
            {
                switch (rt.Type)
                {
                    case EvType.WaveDefense:
                    case EvType.BossRush: ScanWaves(rt); break;
                    case EvType.HuntContest: ScanHunt(rt, edgeNewDay, edgeNight, edgeDay); break;
                    case EvType.ScheduledRaid: ScanRaid(rt, edgeNewDay, edgeNight, edgeDay, day); break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Event '{rt.Def.Id}' scan failed: {e.Message}"); }
        }
    }

    // ---- waveDefense / bossRush ----

    private static void ScanWaves(EventRuntime rt)
    {
        switch (rt.State)
        {
            case EvState.Idle:
                Guid world = FindBestWorld(rt.Def.Map, rt.Def.MinPlayers);
                if (world == Guid.Empty) return;
                rt.State = EvState.Running;
                rt.WorldId = world;
                rt.WaveIdx = -1;
                rt.BetweenWaves = true;
                rt.NextAt = _now + rt.Plan[0].Delay;
                rt.Alive.Clear();
                Plugin.Log.LogInfo($"Event '{rt.Def.Id}': run started in world {world}.");
                Broadcast(rt.Type == EvType.BossRush
                    ? $"{rt.DisplayName} begins — the first foe arrives in {(int)rt.Plan[0].Delay}s!"
                    : $"{rt.DisplayName} begins — first wave in {(int)rt.Plan[0].Delay}s!");
                return;

            case EvState.Running:
                if (CountPlayersInWorld(rt.WorldId) == 0) { Abort(rt); return; }
                PruneDead(rt);
                if (rt.BetweenWaves)
                {
                    if (_now >= rt.NextAt) SpawnNextWave(rt);
                }
                else if (rt.Alive.Count == 0) OnWaveCleared(rt);
                return;

            case EvState.Cooldown:
                if (!PlayerServerManager.AnyPlayerInWorld(rt.WorldId))
                {
                    rt.State = EvState.Idle;
                    rt.WorldId = Guid.Empty;
                    Plugin.Log.LogInfo($"Event '{rt.Def.Id}' re-armed (world emptied).");
                }
                return;
        }
    }

    private static void SpawnNextWave(EventRuntime rt)
    {
        rt.WaveIdx++;
        WavePlan wave = rt.Plan[rt.WaveIdx];
        int spawned = SpawnCreatures(rt, wave.Creatures);
        if (spawned == 0)
        {
            // nothing could spawn (bad ids / no players to anchor on) — skip the wave instead of stalling
            Plugin.Log.LogWarning($"Event '{rt.Def.Id}': wave {rt.WaveIdx + 1} spawned nothing — skipping.");
            OnWaveCleared(rt);
            return;
        }
        rt.BetweenWaves = false;
        if (rt.Def.AnnounceWaves)
            Broadcast(wave.Announce ?? $"{rt.DisplayName}: wave {rt.WaveIdx + 1}/{rt.Plan.Count} — fight!");
    }

    private static void OnWaveCleared(EventRuntime rt)
    {
        if (rt.WaveIdx >= rt.Plan.Count - 1)   // that was the last wave: victory
        {
            Broadcast($"{rt.DisplayName} is conquered! Glory to the defenders!");
            if (!string.IsNullOrWhiteSpace(rt.Def.Reward)) RewardPlayersInWorld(rt.WorldId, rt);
            Plugin.Log.LogInfo($"Event '{rt.Def.Id}': completed. Cooling down.");
            rt.State = EvState.Cooldown;
            return;
        }
        rt.BetweenWaves = true;
        rt.NextAt = _now + rt.Plan[rt.WaveIdx + 1].Delay;
        if (rt.Def.AnnounceWaves && rt.WaveIdx >= 0)
            Broadcast($"{rt.DisplayName}: wave {rt.WaveIdx + 1} cleared — next in {(int)rt.Plan[rt.WaveIdx + 1].Delay}s!");
    }

    // Spawn a wave's creatures in a ring around the players inside the event world (never ON them: 56-96px
    // out, golden-angle spread so multi-spawns fan out). Anchoring on live player ENTITY positions keeps us
    // inside walkable space without needing world bounds. Returns how many entities actually spawned.
    private static int SpawnCreatures(EventRuntime rt, Dictionary<string, int> creatures)
    {
        _anchors.Clear();
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            var ch = p?.CharacterModel?.Character;
            if (ch == null || ch.WorldId != rt.WorldId) continue;
            if (ServerGameState.Entities.TryGetValue(ch.EntityId, out ServerEntityModel sem) && sem != null)
                _anchors.Add(sem.Position);
        }
        if (_anchors.Count == 0) return 0;

        int spawned = 0, k = 0;
        foreach (var kv in creatures)
        {
            if (kv.Value <= 0) continue;
            if (!TryResolveCreature(kv.Key, out Guid baseId))
            {
                Plugin.Log.LogWarning($"Event '{rt.Def.Id}': unknown creature '{kv.Key}' " +
                    "(use a pack creature id or a Shared.EntityIds name — see reference/creature-ids.txt).");
                continue;
            }
            for (int i = 0; i < kv.Value; i++, k++)
            {
                Vector3 anchor = _anchors[k % _anchors.Count];
                double ang = k * 2.39996 + _rng.NextDouble();          // golden-angle ring, jittered
                float r = 56f + 40f * (float)_rng.NextDouble();        // 3.5 - 6 tiles out
                var pos = anchor + new Vector3((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r, 0f);
                ServerEntityModel sem = EntityServerManager.SpawnEntity(new SpawnEntityArgs
                {
                    Id = Guid.NewGuid(),
                    BaseId = baseId,
                    WorldId = rt.WorldId,
                    Position = pos,
                    EntityType = EntityType.Hostile,    // hostile to players; vanilla AI takes over
                    DynamicSpawn = true,
                });
                if (sem != null) { rt.Alive.Add(sem.Id); spawned++; }
            }
        }
        return spawned;
    }

    // Drop Alive ids whose entity no longer exists (covers removal paths that skip the kill event).
    private static void PruneDead(EventRuntime rt)
    {
        if (rt.Alive.Count == 0) return;
        _gone.Clear();
        foreach (Guid id in rt.Alive)
            if (!ServerGameState.Entities.ContainsKey(id)) _gone.Add(id);
        foreach (Guid id in _gone) rt.Alive.Remove(id);
    }

    // ---- huntContest ----

    private static void ScanHunt(EventRuntime rt, bool edgeNewDay, bool edgeNight, bool edgeDay)
    {
        switch (rt.State)
        {
            case EvState.Idle:
                if (PlayerServerService.JoinedPlayers.Count < rt.Def.MinPlayers) return;
                bool start = rt.Schedule switch
                {
                    Sched.Night => edgeNight,
                    Sched.Day => edgeDay,
                    Sched.NewDay => edgeNewDay,
                    _ => true,   // no schedule: run whenever enough players are online
                };
                if (!start) return;
                rt.TargetBaseId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(rt.Def.Target) && !TryResolveCreature(rt.Def.Target, out rt.TargetBaseId))
                {
                    Plugin.Log.LogWarning($"Event '{rt.Def.Id}' disabled: unknown target creature '{rt.Def.Target}'.");
                    rt.Disabled = true;
                    return;
                }
                rt.Kills.Clear();
                rt.LastKill.Clear();
                rt.NextAt = _now + rt.Def.DurationSeconds;
                rt.State = EvState.Running;
                Plugin.Log.LogInfo($"Event '{rt.Def.Id}': hunt started ({rt.Def.DurationSeconds}s).");
                Broadcast($"{rt.DisplayName}: the hunt is on! Slay " +
                    (rt.TargetBaseId == Guid.Empty ? "anything that moves" : CreatureLabel(rt.Def.Target)) +
                    $" — most kills in {rt.Def.DurationSeconds / 60} min wins!");
                return;

            case EvState.Running:
                if (_now < rt.NextAt) return;
                string winner = PickHuntWinner(rt);
                if (winner != null)
                {
                    rt.Kills.TryGetValue(winner, out int k);
                    Broadcast($"{rt.DisplayName} is over — {winner} wins with {k} kill{(k == 1 ? "" : "s")}!");
                    if (!string.IsNullOrWhiteSpace(rt.Def.Reward)) GiveReward(winner, rt);
                }
                else Broadcast($"{rt.DisplayName} ends with no kills — no victor.");
                Plugin.Log.LogInfo($"Event '{rt.Def.Id}': hunt ended, winner = {winner ?? "(none)"}.");
                // Scheduled hunts re-arm immediately — the schedule edge has passed, so the next one can
                // only fire on the NEXT night/day/newDay. Unscheduled hunts pause for cooldownSeconds.
                if (rt.Schedule != Sched.None) rt.State = EvState.Idle;
                else { rt.State = EvState.Cooldown; rt.NextAt = _now + rt.Def.CooldownSeconds; }
                return;

            case EvState.Cooldown:
                if (_now >= rt.NextAt) rt.State = EvState.Idle;
                return;
        }
    }

    // Most kills; ties broken by whose LAST kill landed first (same rule as the arena).
    private static string PickHuntWinner(EventRuntime rt)
    {
        string best = null; int bestKills = 0; double bestLast = double.MaxValue;
        foreach (var kv in rt.Kills)
        {
            rt.LastKill.TryGetValue(kv.Key, out double last);
            if (kv.Value > bestKills || (kv.Value == bestKills && best != null && last < bestLast))
            { best = kv.Key; bestKills = kv.Value; bestLast = last; }
        }
        return best;
    }

    // ---- scheduledRaid ----

    private static void ScanRaid(EventRuntime rt, bool edgeNewDay, bool edgeNight, bool edgeDay, int day)
    {
        bool fire = rt.Schedule switch
        {
            Sched.Night => edgeNight,
            Sched.Day => edgeDay,
            _ => edgeNewDay,
        };
        if (!fire) return;
        if (PlayerServerService.JoinedPlayers.Count < rt.Def.MinPlayers) return;
        if (rt.LastRaidDay != int.MinValue && day - rt.LastRaidDay < rt.Def.EveryNDays) return;

        if (!RaidDataBase.DataMap.ContainsKey(rt.Def.RaidData))
        {
            Plugin.Log.LogWarning($"Event '{rt.Def.Id}' disabled: unknown raidData '{rt.Def.RaidData}' " +
                "(vanilla ids: raid:rats, raid:potmen, raid:grey_satyr, raid:fallen:1, raid:satyr, " +
                "raid:gorgons, raid:cyclopelli, raid:fallen:3, raid:pyzifax).");
            rt.Disabled = true;
            return;
        }

        // Town pick mirrors UseWarDeclaration: closest town to a player, weighted-random fallback.
        ServerTownModel town = null;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            var ch = p?.CharacterModel?.Character;
            if (ch == null) continue;
            if (ServerGameState.Entities.TryGetValue(ch.EntityId, out ServerEntityModel sem) && sem != null)
            { town = TownsServerManager.GetClosestTown(new Vector2(sem.Position.X, sem.Position.Y)); break; }
        }
        town ??= RaidsServerManager.GetWeightedTown();
        if (town == null)
        {
            Plugin.Log.LogInfo($"Event '{rt.Def.Id}': no town to raid — skipped this cycle.");
            return;   // not marked as fired: retry on the next edge
        }

        rt.LastRaidDay = day;
        if (rt.Def.Immediate)
        {
            // war-declaration style: short approach, marching immediately
            var raid = RaidsServerManager.CreateRaid(rt.Def.RaidData, town, null, 0.3f, startedByWarDeclaration: true);
            if (raid == null) { Plugin.Log.LogWarning($"Event '{rt.Def.Id}': CreateRaid returned null."); return; }
            RaidsServerManager.OnRaidStartedApproach(raid, town);
        }
        else if (RaidsServerManager.CreateRaid(rt.Def.RaidData, town) == null)
        { Plugin.Log.LogWarning($"Event '{rt.Def.Id}': CreateRaid returned null."); return; }

        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': raid '{rt.Def.RaidData}' created against town '{town.Model.Name}'.");
        Broadcast($"{rt.DisplayName}: war drums echo — raiders march on {town.Model.Name}!");
    }

    // ---- kill funnel (Harmony postfix on OnEntityKilledEvent.Send, see EventsPlusKillPatch) ----

    internal static void OnEntityKilled(Guid killedId, Guid killedBaseId, Guid killerEntityId)
    {
        if (_events.Count == 0) return;
        try
        {
            string killerName = null;
            bool killerResolved = false;
            foreach (EventRuntime rt in _events)
            {
                if (rt.State != EvState.Running) continue;
                if (rt.Type == EvType.WaveDefense || rt.Type == EvType.BossRush)
                {
                    rt.Alive.Remove(killedId);   // liveness scan would catch it too; this is just snappier
                    continue;
                }
                if (rt.Type != EvType.HuntContest) continue;
                if (killedBaseId == EntityIds.PlayerBaseEntityGuid) continue;            // player deaths don't score
                if (rt.TargetBaseId != Guid.Empty && killedBaseId != rt.TargetBaseId) continue;
                if (killerEntityId == Guid.Empty) continue;                              // unattributed (server-sim) kill
                if (!killerResolved) { killerName = PlayerNameFromEntityId(killerEntityId); killerResolved = true; }
                if (killerName == null) continue;
                rt.Kills.TryGetValue(killerName, out int k);
                rt.Kills[killerName] = k + 1;
                rt.LastKill[killerName] = _now;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"EventsPlus kill handling failed: {e.Message}"); }
    }

    // ---- helpers ----

    private static void Abort(EventRuntime rt)
    {
        try
        {
            if (rt.Alive.Count > 0)   // despawn leftovers so an abandoned arena doesn't fill with mobs
            {
                foreach (Guid id in rt.Alive)
                    try { EntityServerManager.RemoveEntity(id, new EntityRemoveInfo { RemoveType = EntityRemoveType.Unloaded }); }
                    catch { }
                rt.Alive.Clear();
            }
        }
        catch { }
        if (rt.State == EvState.Running)
            Plugin.Log.LogInfo($"Event '{rt.Def.Id}': aborted (players left / reload). Back to Idle.");
        rt.State = EvState.Idle;
        rt.WorldId = Guid.Empty;
        rt.WaveIdx = -1;
        rt.BetweenWaves = false;
        rt.Kills.Clear();
        rt.LastKill.Clear();
    }

    // A world of the given pack map currently holding >= minPlayers (the fullest one wins).
    private static Guid FindBestWorld(string mapId, int minPlayers)
    {
        Guid bestWorld = Guid.Empty;
        int bestCount = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            Guid w = p?.CharacterModel?.Character?.WorldId ?? Guid.Empty;
            if (w == Guid.Empty || MapIdForWorld(w) != mapId) continue;
            int c = CountPlayersInWorld(w);
            if (c > bestCount) { bestCount = c; bestWorld = w; }
        }
        return bestCount >= minPlayers ? bestWorld : Guid.Empty;
    }

    private static int CountPlayersInWorld(Guid worldId)
    {
        int n = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == worldId) n++;
        return n;
    }

    // Which pack map does this world render? Pack maps are dungeon rooms; the room's DataId IS the map id.
    private static string MapIdForWorld(Guid worldId)
    {
        if (worldId == Guid.Empty) return null;
        if (_worldMapCache.TryGetValue(worldId, out string cached)) return cached;
        string id = null;
        try { id = DungeonServerManager.GetDungeonRoomFromWorldId(worldId)?.DataId; }
        catch { }
        if (id != null)
        {
            if (_worldMapCache.Count > 512) _worldMapCache.Clear();
            _worldMapCache[worldId] = id;
        }
        return id;
    }

    private static string PlayerNameFromEntityId(Guid characterEntityId)
    {
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            var ch = p?.CharacterModel?.Character;
            if (ch != null && ch.EntityId == characterEntityId) return ch.Name;
        }
        return null;
    }

    // Resolve a creature id exactly like a summon spell's `creature`: a pack creature id first, then a
    // Shared.EntityIds field name (e.g. "FallenSword" — see reference/creature-ids.txt).
    private static bool TryResolveCreature(string name, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (ContentLoader.CustomCreatureGuids.TryGetValue(name, out id)) return true;
        var f = AccessTools.Field(typeof(EntityIds), name);
        if (f == null || f.FieldType != typeof(Guid)) return false;
        id = (Guid)f.GetValue(null);
        return true;
    }

    private static string CreatureLabel(string creatureId)
    {
        if (string.IsNullOrWhiteSpace(creatureId)) return "the foe";
        int i = creatureId.LastIndexOf(':');
        return i >= 0 ? creatureId.Substring(i + 1) : creatureId;
    }

    private static void GiveReward(string playerName, EventRuntime rt)
    {
        try
        {
            foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            {
                if (p?.CharacterModel?.Character?.Name != playerName) continue;
                Guid inv = p.CharacterModel.Character.CharacterInventoryModel.InventoryId;
                SimpleInventoryServerManager.TryAddItemAmounts(inv, rt.Def.Reward, rt.Def.RewardAmount,
                    null, allowPartialStack: true, null);
                return;
            }
            Plugin.Log.LogWarning($"Event '{rt.Def.Id}': winner '{playerName}' not found for reward (disconnected?).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Event reward failed: {e.Message}"); }
    }

    // Every player still inside the event world gets the reward (wave defense / boss rush "survivors").
    private static void RewardPlayersInWorld(Guid worldId, EventRuntime rt)
    {
        try
        {
            foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            {
                var ch = p?.CharacterModel?.Character;
                if (ch == null || ch.WorldId != worldId) continue;
                SimpleInventoryServerManager.TryAddItemAmounts(ch.CharacterInventoryModel.InventoryId,
                    rt.Def.Reward, rt.Def.RewardAmount, null, allowPartialStack: true, null);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Event reward failed: {e.Message}"); }
    }

    private static void Broadcast(string text)
    {
        try { ChatLogServerService.SendChatLogMessage(ChatLogMessage.DirectText(text, ChatMessageType.WorldEvent, "Content Forge Events")); }
        catch (Exception e) { Plugin.Log.LogWarning($"Event broadcast failed: {e.Message}"); }
    }
}

// (server) Creature-kill funnel: OnEntityKilledEvent.Send is the single point the game's own quest
// objectives (KillEntityQuestObjective & co.) get their kill credit from — every observable entity death
// routes through it: client-resolved kills with the killing player's character-entity id
// ("entity-die-and-spawn-world-items"), and server-sim/loot paths without attribution. We piggyback with a
// postfix; Harmony binds the three parameters we need by name.
internal static class EventsPlusKillPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(OnEntityKilledEvent), nameof(OnEntityKilledEvent.Send))]
    private static void Postfix(Guid killedEntityId, Guid killedEntityBaseId, Guid killerEntityId)
        => EventsPlusFeature.OnEntityKilled(killedEntityId, killedEntityBaseId, killerEntityId);
}
