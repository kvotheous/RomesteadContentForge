using CandideServer;                          // ServerGameState
using CandideServer.Entities;                 // ServerEntityModel
using CandideServer.MessageModels;            // ChatLogMessage
using CandideServer.Models;                   // ChatMessageType
using CandideServer.ServerManagers;           // DungeonServerManager, PlayerServerManager, SimpleInventoryServerManager
using CandideServer.ServerServices;           // PlayerServerService, ChatLogServerService, ServerJoinedPlayerModel
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// (EventDef lives in ContentPack.cs — the Validator compiles ContentPack standalone.)

// Server-side event types, batch 2 — competitive events on top of the same EventsFeature/EventsPlusFeature
// machinery. Three types, all host-only; clients only ever see chat lines (plain vanilla netcode):
//
//   teamBattle — on a pvp:true map: when minPlayers stand inside, the players are split into two teams
//                (alternating by join order — peer ids grow with joins — so the split stays balanced;
//                rosters announced in chat). Kills come from PvpFeature.PlayerKilledInPvp like the arena;
//                first team to killsToWin (10) wins and every member gets the reward. While a battle runs,
//                Teams (playerId -> team) is consulted by Pvp's SERVER damage gate to swallow same-team
//                damage. The CLIENT gate (PvpClientHitPatch) cannot know teams: teams are host-only state
//                (no server->client sync channel exists) and that patch has no victim identity anyway — so
//                a teammate-attacker still sees a predicted hit on their own screen that never applies or
//                rebroadcasts (cosmetic; documented, accepted).
//   koth       — king of the hill, any map (pvp optional): a hill at point [x,y] (map px, same space as
//                doodads/npc positions) with hillRadius (80). The SOLE player on the hill earns 1 point per
//                second; 2+ players on it = contested, nobody scores (on a pvp map, fighting settles it).
//                First to scoreToWin (60) wins + reward. Lead changes announce, throttled to one per 10s.
//   race       — checkpoint race: when minPlayers stand in the map's world, a 10s countdown announces, then
//                each player must pass within checkpointRadius (40) of every checkpoints[i] IN ORDER
//                (per-player progress from position polling EVERY tick, so sprinting can't skip one).
//                First through the last checkpoint wins + reward; timeLimitSeconds (300) aborts the race.
//
// Scheduling: EventsCompetitiveFeature.Tick(dt) chains after EventsPlusFeature.Tick in CompanionPatch's existing
// EntityServerSystem.Update postfix — no extra per-tick patch, and NO Harmony patch of its own at all
// (team kills ride the PvpFeature.PlayerKilledInPvp event; koth/race are pure position polling).
// State machine, reload-abort safety (server empties -> everything back to Idle), world resolution
// (DungeonServerManager.GetDungeonRoomFromWorldId) and reward path (TryAddItemAmounts) all mirror
// EventsPlusFeature; runtime state is keyed by the int peer/player id (what the damage gate holds).
internal static class EventsCompetitiveFeature
{
    private enum EvType { TeamBattle, Koth, Race }
    private enum EvState { Idle, Running, Cooldown }

    private const float CountdownSeconds = 10f;        // race: "get ready" -> GO
    private const double LeadAnnounceThrottle = 10.0;  // koth/race: min seconds between lead-change lines
    private static readonly string[] TeamNames = { "Team Red", "Team Blue" };

    private sealed class EventRuntime
    {
        public EventDef Def;
        public EvType Type;
        public EvState State = EvState.Idle;
        public Guid WorldId;                                    // world the current run is bound to
        public double StartAt;                                  // race: countdown end (GO)
        public double DeadlineAt;                               // race: time-limit deadline
        public bool Started;                                    // race: GO fired
        public readonly List<int> Members = new();              // teamBattle: this battle's player ids
        public readonly int[] TeamKills = new int[2];           // teamBattle: kills per team
        public readonly Dictionary<int, int> Score = new();     // koth: playerId -> hill-seconds
        public readonly Dictionary<int, int> Progress = new();  // race: playerId -> checkpoints passed
        public int LeaderId = -1;                               // koth: last announced leader
        public int LeaderMark;                                  // race: leader's last announced checkpoint
        public double LastLeadAnnounce = double.MinValue;
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
    private static bool _subscribed;
    private static double _now;           // server seconds, accumulated from Tick dt
    private static float _scanTimer;
    private const float ScanInterval = 1f;

    // ---- team state consulted by Pvp.cs's server damage gate (PvpDamageGatePatch) ----

    // playerId -> team (0/1). Non-empty only while a teamBattle runs; mutated on the server tick only.
    public static readonly Dictionary<int, int> Teams = new();

    public static bool SameTeam(int playerA, int playerB)
        => Teams.Count > 0
        && Teams.TryGetValue(playerA, out int ta)
        && Teams.TryGetValue(playerB, out int tb)
        && ta == tb;

    // ---- registration (called by ContentLoader while parsing packs, BEFORE EventsPlusFeature gets the
    //      def — returns true when the type is ours so unknown-type warnings stay accurate) ----

    public static bool RegisterEvent(EventDef def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Id) || string.IsNullOrWhiteSpace(def.Type)) return false;

        EvType type;
        switch (def.Type.Trim().ToLowerInvariant())
        {
            case "teambattle": type = EvType.TeamBattle; break;
            case "koth": case "kingofthehill": type = EvType.Koth; break;
            case "race": type = EvType.Race; break;
            default: return false;   // not ours — EventsPlusFeature handles (or warns about) it
        }
        foreach (var e in _events) if (e.Def.Id == def.Id) return true;   // duplicate id: already registered

        // All three bind to one of the pack's maps (parsed BEFORE the pack's events, so pvp flags are known).
        if (string.IsNullOrWhiteSpace(def.Map))
        { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: {def.Type} needs a map."); return true; }
        if (def.MinPlayers < 1) def.MinPlayers = 1;
        if (def.RewardAmount < 1) def.RewardAmount = 1;

        switch (type)
        {
            case EvType.TeamBattle:
                if (!PvpFeature.PvpMapIds.Contains(def.Map))
                {
                    Plugin.Log.LogWarning($"Event '{def.Id}' skipped: teamBattle needs a pvp:true map " +
                        $"('{def.Map}' isn't flagged — set \"pvp\": true on that map).");
                    return true;
                }
                if (def.MinPlayers < 2) def.MinPlayers = 2;
                if (def.KillsToWin < 1) def.KillsToWin = 10;
                break;

            case EvType.Koth:
                if (def.Point == null || def.Point.Length < 2)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: koth needs point [x,y] (map px, doodad space)."); return true; }
                if (def.HillRadius < 8f) def.HillRadius = 80f;
                if (def.ScoreToWin < 5) def.ScoreToWin = 60;
                break;

            case EvType.Race:
                bool badCp = def.Checkpoints == null || def.Checkpoints.Count == 0;
                if (!badCp)
                    foreach (float[] c in def.Checkpoints)
                        if (c == null || c.Length < 2) { badCp = true; break; }
                if (badCp)
                { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: race needs checkpoints [[x,y],...] (map px)."); return true; }
                if (def.CheckpointRadius < 8f) def.CheckpointRadius = 40f;
                if (def.TimeLimitSeconds < 30) def.TimeLimitSeconds = 300;
                break;
        }

        _events.Add(new EventRuntime { Def = def, Type = type });
        Plugin.Log.LogInfo($"Event '{def.Id}' registered ({def.Type}, map '{def.Map}').");
        return true;
    }

    // ---- per-server-tick entry point (chained after EventsPlusFeature.Tick in CompanionPatch's postfix) ----

    public static void Tick(float dt)
    {
        if (_events.Count == 0) return;
        try
        {
            _now += dt;
            if (!_subscribed) { PvpFeature.PlayerKilledInPvp += OnPvpKill; _subscribed = true; }
            PollRaces();                       // EVERY tick: checkpoint detection only (running races only)
            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;
            Scan();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"EventsCompetitive tick failed: {e.Message}"); }
    }

    // ---- once-per-second state machine ----

    private static void Scan()
    {
        // Reload / empty server: abort everything to Idle (same safety rule as EventsPlusFeature).
        if (PlayerServerService.JoinedPlayers.Count == 0)
        {
            foreach (var rt in _events)
                if (rt.State != EvState.Idle) Abort(rt);
            return;
        }

        foreach (EventRuntime rt in _events)
        {
            try
            {
                switch (rt.Type)
                {
                    case EvType.TeamBattle: ScanTeamBattle(rt); break;
                    case EvType.Koth: ScanKoth(rt); break;
                    case EvType.Race: ScanRace(rt); break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Event '{rt.Def.Id}' scan failed: {e.Message}"); }
        }
    }

    // ---- teamBattle ----

    private static void ScanTeamBattle(EventRuntime rt)
    {
        switch (rt.State)
        {
            case EvState.Idle:
                Guid world = FindBestWorld(rt.Def.Map, rt.Def.MinPlayers);
                if (world == Guid.Empty) return;
                StartTeamBattle(rt, world);
                return;

            case EvState.Running:
                int present = CountPlayersInWorld(rt.WorldId);
                if (present == 0) { Abort(rt); return; }
                AssignLateJoiners(rt);
                if (present == 1)
                {
                    // everyone else left mid-battle: the remaining fighter's team takes it (arena convention)
                    int pid = SolePlayerInWorld(rt.WorldId);
                    EndTeamBattle(rt, pid >= 0 && Teams.TryGetValue(pid, out int t) ? t : -1, "the field is abandoned");
                }
                return;

            case EvState.Cooldown:
                CooldownToIdle(rt);
                return;
        }
    }

    private static void StartTeamBattle(EventRuntime rt, Guid world)
    {
        rt.State = EvState.Running;
        rt.WorldId = world;
        rt.TeamKills[0] = rt.TeamKills[1] = 0;
        rt.Members.Clear();

        // Alternate team assignment in join order — peer ids are handed out as players join, so sorting by
        // id is the join order; A,B,A,B keeps an odd count off-by-one at worst.
        var pids = new List<int>();
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == world) pids.Add(p.Id);
        pids.Sort();
        var roster = new[] { new List<string>(), new List<string>() };
        for (int i = 0; i < pids.Count; i++)
        {
            int team = i & 1;
            Teams[pids[i]] = team;
            rt.Members.Add(pids[i]);
            roster[team].Add(NameOf(pids[i]));
        }
        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': battle started in world {world} ({pids.Count} fighters).");
        Broadcast($"{rt.DisplayName}: {TeamNames[0]} ({string.Join(", ", roster[0])}) vs " +
            $"{TeamNames[1]} ({string.Join(", ", roster[1])}) — first to {rt.Def.KillsToWin} kills!");
    }

    // Players walking in mid-battle join the smaller team (and become valid targets/scorers).
    private static void AssignLateJoiners(EventRuntime rt)
    {
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            var ch = p?.CharacterModel?.Character;
            if (ch == null || ch.WorldId != rt.WorldId || Teams.ContainsKey(p.Id)) continue;
            int a = 0, b = 0;
            foreach (int m in rt.Members)
                if (Teams.TryGetValue(m, out int t)) { if (t == 0) a++; else b++; }
            int team = a <= b ? 0 : 1;
            Teams[p.Id] = team;
            rt.Members.Add(p.Id);
            Broadcast($"{ch.Name} joins {TeamNames[team]}!");
        }
    }

    // Kill feed: same source as the duel arena (PvpFeature.PlayerKilledInPvp, server-side, both the exact
    // killing-blow attribution and the last-hit heuristic — see Pvp.cs).
    private static void OnPvpKill(string victimName, string killerName, Guid worldId)
    {
        try
        {
            foreach (EventRuntime rt in _events)
            {
                if (rt.Type != EvType.TeamBattle || rt.State != EvState.Running || rt.WorldId != worldId) continue;
                if (killerName == null)
                {
                    if (rt.Def.AnnounceWaves) Broadcast($"{victimName} fell in {rt.DisplayName}.");
                    return;
                }
                int killer = PlayerIdFromName(killerName);
                if (killer < 0 || !Teams.TryGetValue(killer, out int team)) return;   // not a combatant
                int victim = PlayerIdFromName(victimName);
                if (victim >= 0 && Teams.TryGetValue(victim, out int vt) && vt == team) return; // teammate down (mob steal) — no score
                rt.TeamKills[team]++;
                if (rt.Def.AnnounceWaves)
                    Broadcast($"{killerName} scores for {TeamNames[team]} ({rt.TeamKills[team]}/{rt.Def.KillsToWin})!");
                if (rt.TeamKills[team] >= rt.Def.KillsToWin) EndTeamBattle(rt, team, null);
                return;   // one battle per world
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"EventsCompetitive kill handling failed: {e.Message}"); }
    }

    private static void EndTeamBattle(EventRuntime rt, int winTeam, string reason)
    {
        if (winTeam == 0 || winTeam == 1)
        {
            Broadcast($"{TeamNames[winTeam]} wins {rt.DisplayName} {rt.TeamKills[winTeam]}–{rt.TeamKills[1 - winTeam]}" +
                (reason != null ? $" — {reason}!" : "!"));
            // Reward every member of the winning team still ONLINE (a pvp respawn may have moved them
            // out of the arena world by now, so presence-in-world would be unfair here).
            if (!string.IsNullOrWhiteSpace(rt.Def.Reward))
                foreach (int m in rt.Members)
                    if (Teams.TryGetValue(m, out int t) && t == winTeam) RewardPlayer(m, rt);
        }
        else Broadcast($"{rt.DisplayName} ends with no victor.");
        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': battle ended, winner = " +
            $"{(winTeam == 0 || winTeam == 1 ? TeamNames[winTeam] : "(none)")}. Cooling down.");
        ClearTeams(rt);
        rt.State = EvState.Cooldown;
    }

    // ---- koth ----

    private static void ScanKoth(EventRuntime rt)
    {
        switch (rt.State)
        {
            case EvState.Idle:
                Guid world = FindBestWorld(rt.Def.Map, rt.Def.MinPlayers);
                if (world == Guid.Empty) return;
                rt.State = EvState.Running;
                rt.WorldId = world;
                rt.Score.Clear();
                rt.LeaderId = -1;
                rt.LastLeadAnnounce = double.MinValue;
                Plugin.Log.LogInfo($"Event '{rt.Def.Id}': hill contest started in world {world}.");
                Broadcast($"{rt.DisplayName} begins — hold the hill alone! First to {rt.Def.ScoreToWin} seconds wins.");
                return;

            case EvState.Running:
                if (CountPlayersInWorld(rt.WorldId) == 0) { Abort(rt); return; }

                // Who is on the hill? Sole occupant scores 1/second; 2+ occupants = contested, nobody scores.
                int insideCount = 0, insidePid = -1;
                float hx = rt.Def.Point[0], hy = rt.Def.Point[1], r2 = rt.Def.HillRadius * rt.Def.HillRadius;
                foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
                {
                    var ch = p?.CharacterModel?.Character;
                    if (ch == null || ch.WorldId != rt.WorldId || p.Dead) continue;
                    if (!ServerGameState.Entities.TryGetValue(ch.EntityId, out ServerEntityModel sem) || sem == null) continue;
                    float dx = sem.Position.X - hx, dy = sem.Position.Y - hy;
                    if (dx * dx + dy * dy > r2) continue;
                    insideCount++;
                    insidePid = p.Id;
                }
                if (insideCount == 1)
                {
                    rt.Score.TryGetValue(insidePid, out int s);
                    rt.Score[insidePid] = ++s;
                    if (s >= rt.Def.ScoreToWin)
                    {
                        Broadcast($"{NameOf(insidePid)} is king of the hill — {rt.DisplayName} is won after {s}s on top!");
                        if (!string.IsNullOrWhiteSpace(rt.Def.Reward)) RewardPlayer(insidePid, rt);
                        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': won by player #{insidePid}. Cooling down.");
                        rt.State = EvState.Cooldown;
                        return;
                    }
                }
                AnnounceKothLead(rt);
                return;

            case EvState.Cooldown:
                CooldownToIdle(rt);
                return;
        }
    }

    // Lead changes announce at most once per LeadAnnounceThrottle (skipped changes re-announce on a later
    // scan once the window opens — LeaderId only advances when a line actually goes out).
    private static void AnnounceKothLead(EventRuntime rt)
    {
        int bestPid = -1, bestScore = 0;
        foreach (var kv in rt.Score)
            if (kv.Value > bestScore) { bestScore = kv.Value; bestPid = kv.Key; }
        if (bestPid < 0 || bestPid == rt.LeaderId) return;
        if (_now - rt.LastLeadAnnounce < LeadAnnounceThrottle) return;
        rt.LeaderId = bestPid;
        rt.LastLeadAnnounce = _now;
        Broadcast($"{NameOf(bestPid)} takes the hill lead ({bestScore}/{rt.Def.ScoreToWin})!");
    }

    // ---- race ----

    private static void ScanRace(EventRuntime rt)
    {
        switch (rt.State)
        {
            case EvState.Idle:
                Guid world = FindBestWorld(rt.Def.Map, rt.Def.MinPlayers);
                if (world == Guid.Empty) return;
                rt.State = EvState.Running;
                rt.WorldId = world;
                rt.Started = false;
                rt.StartAt = _now + CountdownSeconds;
                rt.DeadlineAt = rt.StartAt + rt.Def.TimeLimitSeconds;
                rt.Progress.Clear();
                rt.LeaderMark = 0;
                rt.LastLeadAnnounce = double.MinValue;
                Plugin.Log.LogInfo($"Event '{rt.Def.Id}': race armed in world {world}.");
                Broadcast($"{rt.DisplayName}: a race through {rt.Def.Checkpoints.Count} checkpoints " +
                    $"begins in {(int)CountdownSeconds}s — line up!");
                return;

            case EvState.Running:
                if (CountPlayersInWorld(rt.WorldId) == 0) { Abort(rt); return; }
                if (!rt.Started)
                {
                    if (_now < rt.StartAt) return;
                    rt.Started = true;
                    Broadcast($"{rt.DisplayName}: GO!");
                    return;
                }
                if (_now >= rt.DeadlineAt)   // checkpoint passes & the win are detected per tick in PollRaces
                {
                    Broadcast($"{rt.DisplayName}: time's up — nobody finished the course.");
                    Plugin.Log.LogInfo($"Event '{rt.Def.Id}': race timed out. Cooling down.");
                    rt.State = EvState.Cooldown;
                }
                return;

            case EvState.Cooldown:
                CooldownToIdle(rt);
                return;
        }
    }

    // EVERY server tick (not the 1s scan): a sprinting player covers only a few px per tick, so per-tick
    // polling makes checkpointRadius reliable. Cost is a handful of players x one distance check each.
    private static void PollRaces()
    {
        foreach (EventRuntime rt in _events)
        {
            if (rt.Type != EvType.Race || rt.State != EvState.Running || !rt.Started) continue;
            List<float[]> cps = rt.Def.Checkpoints;
            float r2 = rt.Def.CheckpointRadius * rt.Def.CheckpointRadius;
            foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            {
                var ch = p?.CharacterModel?.Character;
                if (ch == null || ch.WorldId != rt.WorldId || p.Dead) continue;
                if (!ServerGameState.Entities.TryGetValue(ch.EntityId, out ServerEntityModel sem) || sem == null) continue;
                rt.Progress.TryGetValue(p.Id, out int prog);
                bool advanced = false;
                while (prog < cps.Count)   // chained: tightly-spaced checkpoints can be passed in one tick
                {
                    float dx = sem.Position.X - cps[prog][0], dy = sem.Position.Y - cps[prog][1];
                    if (dx * dx + dy * dy > r2) break;
                    prog++;
                    advanced = true;
                }
                if (!advanced) continue;
                rt.Progress[p.Id] = prog;
                if (prog >= cps.Count)   // first through the last checkpoint wins
                {
                    Broadcast($"{ch.Name} wins {rt.DisplayName} — all {cps.Count} checkpoints cleared!");
                    if (!string.IsNullOrWhiteSpace(rt.Def.Reward)) RewardPlayer(p.Id, rt);
                    Plugin.Log.LogInfo($"Event '{rt.Def.Id}': race won by player #{p.Id}. Cooling down.");
                    rt.State = EvState.Cooldown;
                    break;
                }
                if (prog > rt.LeaderMark && _now - rt.LastLeadAnnounce >= LeadAnnounceThrottle)
                {
                    rt.LeaderMark = prog;
                    rt.LastLeadAnnounce = _now;
                    Broadcast($"{ch.Name} leads {rt.DisplayName} — checkpoint {prog}/{cps.Count}!");
                }
            }
        }
    }

    // ---- shared state transitions ----

    private static void CooldownToIdle(EventRuntime rt)
    {
        if (PlayerServerManager.AnyPlayerInWorld(rt.WorldId)) return;
        rt.State = EvState.Idle;
        rt.WorldId = Guid.Empty;
        Plugin.Log.LogInfo($"Event '{rt.Def.Id}' re-armed (world emptied).");
    }

    private static void Abort(EventRuntime rt)
    {
        if (rt.State == EvState.Running)
            Plugin.Log.LogInfo($"Event '{rt.Def.Id}': aborted (players left / reload). Back to Idle.");
        ClearTeams(rt);
        rt.Score.Clear();
        rt.Progress.Clear();
        rt.Started = false;
        rt.LeaderId = -1;
        rt.LeaderMark = 0;
        rt.LastLeadAnnounce = double.MinValue;
        rt.State = EvState.Idle;
        rt.WorldId = Guid.Empty;
    }

    // Remove ONLY this battle's members (another teamBattle may be running on a different map).
    private static void ClearTeams(EventRuntime rt)
    {
        foreach (int m in rt.Members) Teams.Remove(m);
        rt.Members.Clear();
        rt.TeamKills[0] = rt.TeamKills[1] = 0;
    }

    // ---- helpers (same shapes as Events.cs / EventsPlus.cs) ----

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

    private static int SolePlayerInWorld(Guid worldId)
    {
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == worldId) return p.Id;
        return -1;
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

    private static int PlayerIdFromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if (p?.CharacterModel?.Character?.Name == name) return p.Id;
        return -1;
    }

    private static string NameOf(int playerId)
    {
        if (PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p))
        {
            string n = p?.CharacterModel?.Character?.Name;
            if (!string.IsNullOrEmpty(n)) return n;
        }
        return $"Player #{playerId}";
    }

    private static void RewardPlayer(int playerId, EventRuntime rt)
    {
        try
        {
            if (!PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p)
                || p?.CharacterModel?.Character == null)
            {
                Plugin.Log.LogWarning($"Event '{rt.Def.Id}': winner #{playerId} not found for reward (disconnected?).");
                return;
            }
            SimpleInventoryServerManager.TryAddItemAmounts(p.CharacterModel.Character.CharacterInventoryModel.InventoryId,
                rt.Def.Reward, rt.Def.RewardAmount, null, allowPartialStack: true, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Event reward failed: {e.Message}"); }
    }

    private static void Broadcast(string text)
    {
        try { ChatLogServerService.SendChatLogMessage(ChatLogMessage.DirectText(text, ChatMessageType.WorldEvent, "Content Forge Events")); }
        catch (Exception e) { Plugin.Log.LogWarning($"Event broadcast failed: {e.Message}"); }
    }
}
