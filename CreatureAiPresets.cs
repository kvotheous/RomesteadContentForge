using BM;                                   // SMBuilder/State/Transition + BT nodes (CandideServer.dll)
using CandideServer;                        // ServerGameState
using CandideServer.Entities;               // ServerEntityModel, ServerEntityControllerLoader
using CandideServer.Entities.Controllers;   // (base classes live in CreatureAi.cs; kept for symmetry)
using CandideServer.MessageModels;          // PlayVfxOnEntityMessage
using CandideServer.ServerControllers;      // SpawnEntityArgs
using CandideServer.ServerManagers;         // EntityServerManager (SpawnEntity, Heal)
using CandideServer.ServerServices;         // ServerSoundAndVfxService (Send_PlayVfxOnEntity)
using HarmonyLib;
using Microsoft.Xna.Framework;              // Vector2/Vector3
using Shared;                               // EntityIds
using Shared.Entity;                        // EntityWrapper, EntityType
using Shared.Entity.Components;             // Component mask (ally filter)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// CREATURE AI PRESETS, WAVE 2 — five more server-side AI controllers on top of CreatureAi.cs's proven
// infrastructure (ForgeAiServerController / ForgeMeleeAiServerController, the same registration mechanism,
// the same parameter conventions: all controllerParams values are strings, forge kebab keys override the
// base controller's copied .cdx keys).
//
//   "forge_patrol"   — walks a waypoint route (`patrol-points` = "x1,y1;x2,y2;..." px; map-absolute, or
//                      spawn-relative with `patrol-relative`), loops (ping-pongs with `patrol-pingpong`),
//                      pauses `patrol-pause`s at each point. Aggros + melees like forge_guard while on the
//                      route and RESUMES it after combat at the nearest waypoint. Patrols even outside the
//                      fog of war (deliberate — that's the point of a route).
//   "forge_pack"     — a melee chaser that, the moment it enters combat, ALERTS every same-base packmate
//                      within `pack-radius` by feeding their ThreatTables the same target (the exact
//                      mechanism the vanilla vulture "control aura" uses: EntityWrapper.AddThreat).
//                      Aggro one, the pack comes — and each alerted packmate re-alerts as IT aggros, so
//                      the call cascades through spread-out packs.
//   "forge_summoner" — a kiting caster (forge_ranged's range-band movement) that periodically SPAWNS
//                      `minion` creatures (pack creature id or Shared.EntityIds name) instead of just
//                      shooting: `minion-count` per summon, `max-minions` alive at once (tracked by id,
//                      pruned by liveness — the EventsPlus pattern), `summon-cooldown` between summons,
//                      optional `ranged-spell` fallback shots between summons.
//   "forge_healer"   — a support creature: drifts toward its allies (same base creature, or any hostile
//                      creature) and every `heal-cooldown`s heals the MOST INJURED ally within
//                      `ally-radius` by `heal-amount` (direct server-side EntityServerManager.Heal — the
//                      same path the AI regen uses, so it syncs like any health change), playing the cast
//                      anim + optional `heal-vfx` spac + optional cosmetic `heal-spell`. ALWAYS flees from
//                      threats (healers are fragile) and returns to its flock when safe.
//   "forge_turret"   — a stationary caster: NEVER moves (no wander/chase/flee nodes at all — a living
//                      ballista/totem/trap), casts `ranged-spell` at any threat within `attack-radius`
//                      every `attack-cooldown`s, line of sight required. No leash, no flee. It can still
//                      be knocked back (immobility = no movement nodes, not infinite mass).
//
// ANIM VOCABULARY: identical to wave 1 — `anim-idle` (0) / `anim-walk` (1) / `anim-attack` (2) ints whose
// meaning is decided by the BASE creature's client controller (see CreatureAi.cs header). Per preset:
//   forge_patrol   walk on route + melee triangle (idle/walk/attack, exactly forge_guard's usage)
//   forge_pack     same as forge_guard (idle/walk/attack)
//   forge_summoner walk while kiting, attack for BOTH the spell shot and the summon gesture, idle to stand
//   forge_healer   walk to wander/follow/flee, attack as the heal-cast gesture, idle elsewhere
//   forge_turret   idle while waiting, attack for the shot; anim-walk is never used (it never walks)
//
// REGISTRATION: mirrors CreatureAiFeature/CreatureAiControllerMapPatch exactly — server map only, injected
// after vanilla fills ServerEntityControllerLoader.ControllerMap. Concrete classes keep parameterless ctors.
// Transition capacities are counted EXACTLY: State.AddTransition writes into a fixed array sized by the
// SMBuilder capacity args — one transition too many is an IndexOutOfRangeException at build time.
// ---------------------------------------------------------------------------------------------------------

internal static class CreatureAiPresetsFeature
{
    // pre-normalized names (lowercase, no "server"/"controller" affixes) -> server controller types.
    internal static readonly Dictionary<string, Type> ServerAiControllers = new()
    {
        ["forge_patrol"] = typeof(ForgePatrolServerController),
        ["forge_pack"] = typeof(ForgePackServerController),
        ["forge_summoner"] = typeof(ForgeSummonerServerController),
        ["forge_healer"] = typeof(ForgeHealerServerController),
        ["forge_turret"] = typeof(ForgeTurretServerController),
    };

    // Called from Plugin.Load. Same guard as CreatureAiFeature.Init: never seed an EMPTY map (vanilla
    // Initialize early-outs on Count > 0 and would skip the game's own controllers).
    public static void Init()
    {
        if (ServerEntityControllerLoader.ControllerMap.Count > 0) Inject();
    }

    internal static void Inject()
    {
        foreach (var kv in ServerAiControllers)
            ServerEntityControllerLoader.ControllerMap[kv.Key] = kv.Value;
    }
}

// Same chokepoint as CreatureAiControllerMapPatch: right after vanilla builds the SERVER controller map.
internal static class CreatureAiPresetsControllerMapPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityControllerLoader), nameof(ServerEntityControllerLoader.Initialize))]
    private static void Postfix()
    {
        try { CreatureAiPresetsFeature.Inject(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Creature-AI-2 controller-map inject failed: {e.Message}"); }
    }
}

// ------------------------------------------------ forge_patrol ------------------------------------------------

// A route walker: visits `patrol-points` in order (loop, or ping-pong with `patrol-pingpong`), pausing
// `patrol-pause`s at each. Combat = forge_guard's melee triangle, entered the moment something lands in
// the threat table (aggro-radius default 200) — afterwards the route resumes at the NEAREST waypoint.
// Movement to each point is the vanilla off-thread pathfinder (the engineer's patrol uses the same node),
// so an unreachable point simply fails and the route advances past it instead of wedging.
public sealed class ForgePatrolServerController : ForgeMeleeAiServerController
{
    private const string PatrolPointKey = "forgePatrolPoint";

    private string _rawPoints = "";
    private bool _relative;
    private bool _pingpong;
    private float _pause;
    private readonly List<Vector2> _pts = new();
    private int _idx = -1;   // index of the LAST targeted waypoint; advanced before each leg
    private int _dir = 1;    // ping-pong direction

    protected override void ReadAiParameters()
    {
        _rawPoints = Parameters.GetString("patrol-points", "") ?? "";
        _relative = Parameters.GetBool("patrol-relative");
        _pingpong = Parameters.GetBool("patrol-pingpong");
        _pause = Parameters.GetFloat("patrol-pause", 1.5f);
        AggroRadius = Parameters.GetFloat("aggro-radius", 200f);
        ReadMeleeParameters();
    }

    // step the route one waypoint and publish it for the pathfind leaf; vanilla Patrol-node stepping rules
    private bool AdvanceWaypoint(Blackboard bb)
    {
        if (_pts.Count == 0) return false;
        if (_pingpong)
        {
            _idx += _dir;
            if (_idx <= -1) { _idx = _pts.Count > 1 ? 1 : 0; _dir = 1; }
            else if (_idx >= _pts.Count) { _idx = _pts.Count > 1 ? _pts.Count - 2 : 0; _dir = -1; }
        }
        else
        {
            _idx++;
            if (_idx >= _pts.Count) _idx = 0;
        }
        bb.SetValue(PatrolPointKey, _pts[_idx]);
        return true;
    }

    // after combat: aim the next advance at the waypoint closest to wherever the fight ended
    private void ResumeNearestWaypoint()
    {
        if (_pts.Count == 0) return;
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < _pts.Count; i++)
        {
            float d = Vector2.DistanceSquared(_pts[i], Entity.Position2);
            if (d < best) { best = d; nearest = i; }
        }
        // set _idx so the next AdvanceWaypoint lands exactly on `nearest` (bounds are advance-safe)
        _idx = _pingpong ? nearest - _dir : nearest - 1;
    }

    public override StateMachine CreateStateMachine()
    {
        // parse here, not in ReadAiParameters: SpawnPosition (for patrol-relative) is only set by
        // base.EntityInitialize, which runs between ReadAiParameters and CreateStateMachine.
        _pts.Clear();
        foreach (string pair in _rawPoints.Split(';'))
        {
            string[] xy = pair.Split(',');
            if (xy.Length == 2
                && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Vector2 p = new(x, y);
                _pts.Add(_relative ? SpawnPosition + p : p);
            }
        }
        if (_pts.Count == 0)
        {
            Plugin.Log.LogWarning($"forge_patrol on '{Entity.Name}': controllerParam 'patrol-points' is missing " +
                "or unparsable (want \"x1,y1;x2,y2;...\") — standing at its spawn point instead.");
            _pts.Add(SpawnPosition);
        }

        State baseState, route, move, pause, aggro, chase, attack, stand;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("route", out route, 1)          // 1: route -> aggro
                    .Leaf("move", out move, 1)          // 1: move -> pause
                    .Leaf("pause", out pause, 1)        // 1: pause -> move
                .End()
                .Branch("aggro", out aggro, 1)          // 1: aggro -> route
                    .Leaf("chase", out chase, 2)        // 2: WireMeleeTriangle
                    .Leaf("attack", out attack, 2)      // 2: WireMeleeTriangle
                    .Leaf("stand", out stand, 2)        // 2: WireMeleeTriangle
                .End()
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(AttackCooldown);

        move.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new CustomBool((ctx, bb, _) => AdvanceWaypoint(bb)))
            .SubTree(BTFactory.MoveToPosition(PatrolPointKey, AnimWalk, WalkSpeed))
            .End()
            .Build();
        pause.UpdateBehaviorTree = BTFactory.Idle(AnimIdle, _pause);

        WireMeleeTriangle(chase, attack, stand);

        ref Transition moveToPause = ref move.AddTransition(pause);
        moveToPause.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());
        ref Transition pauseToMove = ref pause.AddTransition(move);
        pauseToMove.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());

        ref Transition toAggro = ref route.AddTransition(aggro);
        toAggro.Condition = ConditionHelper.Leaf(new HasThreateningEntity());
        ref Transition toRoute = ref aggro.AddTransition(route);
        toRoute.Condition = ConditionHelper.InvertLeaf(new HasThreateningEntity());
        toRoute.OnTransitionBT = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new CustomVoid(ResumeNearestWaypoint))
            .Leaf(new StopMoving())
            .Leaf(new PlayAnimation(AnimIdle))
            .Leaf(new Wait(PauseAfterAggro))
            .End()
            .Build();

        return sm;
    }
}

// ------------------------------------------------ forge_pack -------------------------------------------------

// A pack hunter: standard wander/chase/melee, but entering combat ALERTS every same-BaseGuid creature
// within `pack-radius` by writing `pack-threat` toward its own target into their ThreatTables —
// EntityWrapper.AddThreat -> AggressiveComponent.GenerateThreat, the verified vanilla pack-call path (the
// vulture "control aura" aggros whole flocks exactly this way). Alerted packmates aggro through their own
// state machines; if THEY are also forge_pack, each re-alerts as it engages, cascading the call.
public sealed class ForgePackServerController : ForgeMeleeAiServerController
{
    private float _packRadius;
    private float _packThreat;

    protected override void ReadAiParameters()
    {
        _packRadius = Parameters.GetFloat("pack-radius", 240f);
        _packThreat = Parameters.GetFloat("pack-threat", 100f);   // vanilla pack-call amount
        AggroRadius = Parameters.GetFloat("aggro-radius", 0f);    // 0 = keep the base's own notice radius
        ReadMeleeParameters();
    }

    private void AlertPack()
    {
        try
        {
            EntityWrapper target = Entity.TargetOfAggro
                ?? BehaviorMachine.Blackboard.GetValue<EntityWrapper>(TargetKey);
            if (target == null) return;
            foreach (EntityWrapper e in Entity.System.GetEntitiesInCircleArea(Entity.Position2, _packRadius))
            {
                if (e == null || e.Removed || e.Id == Entity.Id) continue;
                if (e.BaseGuid != Entity.BaseGuid) continue;
                e.AddThreat(target, _packThreat);   // silently a no-op on non-Aggressive entities
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"forge_pack on '{Entity.Name}': pack alert failed: {ex.Message}");
        }
    }

    public override StateMachine CreateStateMachine()
    {
        State baseState, wander, noPlayer, near, walk, idle, walkHome, aggro, chase, attack, stand;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("wander", out wander, 1, 1)     // 1: wander -> aggro; 1 start-child: wakeUp
                    .Leaf("no_player_nearby", out noPlayer, 1)
                    .Branch("player_nearby", out near, 1, 1)
                        .Leaf("walk", out walk, 1)
                        .Leaf("idle", out idle, 1)
                        .Leaf("walk_home", out walkHome, 1)
                    .End()
                .End()
                .Branch("aggro", out aggro, 1)          // 1: aggro -> wander
                    .Leaf("chase", out chase, 2)        // 2: WireMeleeTriangle
                    .Leaf("attack", out attack, 2)      // 2: WireMeleeTriangle
                    .Leaf("stand", out stand, 2)        // 2: WireMeleeTriangle
                .End()
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(AttackCooldown);
        WireWanderBranch(wander, noPlayer, near, walk, idle, walkHome);
        WireMeleeTriangle(chase, attack, stand);

        ref Transition toAggro = ref wander.AddTransition(aggro);
        toAggro.Condition = ConditionHelper.Leaf(new HasThreateningEntity());
        toAggro.OnTransitionBT = BTBuilderPool.Get().Leaf(new CustomVoid(AlertPack)).Build();

        ref Transition toWander = ref aggro.AddTransition(wander);
        toWander.Condition = ConditionHelper.InvertLeaf(new HasThreateningEntity());
        toWander.OnTransitionBT = BuildLoseAggroTree();

        return sm;
    }
}

// ------------------------------------------------ forge_summoner ---------------------------------------------

// A kiting summoner: keeps forge_ranged's distance band, but its primary "attack" is SPAWNING `minion`
// creatures (resolved like the summon spells/EventsPlus: pack creature id first, then a Shared.EntityIds
// field name). Spawns are EntityType.Hostile + DynamicSpawn near the summoner in a golden-angle ring (the
// EventsPlus wave pattern); spawned ids are tracked and pruned by ServerGameState.Entities liveness, so
// `max-minions` counts only LIVING minions. `ranged-spell` (optional) is the filler attack on summon
// cooldown. With no valid minion AND no spell it just kites — and warns once at spawn.
public sealed class ForgeSummonerServerController : ForgeAiServerController
{
    private const string SummonTimerKey = "summonCooldownTimer";
    private const string SummonOnCooldownKey = "summonOnCooldown";

    private Guid _minionBaseId;
    private int _minionCount;
    private int _maxMinions;
    private float _summonCooldown;
    private float _summonWindup;
    private string _rangedSpell = "";
    private bool _hasSpell;
    private float _minRange, _maxRange;
    private float _attackCooldown, _attackWindup, _attackLock, _losWidth;

    private readonly List<Guid> _minions = new();
    private readonly List<Guid> _gone = new();
    private int _ringK;   // golden-angle ring counter, monotonic so successive summons fan out

    protected override void ReadAiParameters()
    {
        string minion = Parameters.GetString("minion", "") ?? "";
        if (!TryResolveCreature(minion, out _minionBaseId))
        {
            _minionBaseId = Guid.Empty;
            Plugin.Log.LogWarning($"forge_summoner on '{Entity.Name}': controllerParam 'minion' ('{minion}') " +
                "is not a pack creature id or Shared.EntityIds name — it will never summon.");
        }
        _minionCount = Parameters.GetInt("minion-count", 1);
        _maxMinions = Parameters.GetInt("max-minions", 3);
        _summonCooldown = Parameters.GetFloat("summon-cooldown", 8f);
        _summonWindup = Parameters.GetFloat("summon-windup", 0.5f);
        _rangedSpell = Parameters.GetString("ranged-spell", "") ?? "";
        _hasSpell = !string.IsNullOrWhiteSpace(_rangedSpell);
        float preferred = Parameters.GetFloat("preferred-range", 140f);
        _minRange = Parameters.GetFloat("min-range", preferred - 50f);
        _maxRange = Parameters.GetFloat("max-range", preferred + 80f);
        if (_maxRange <= _minRange) _maxRange = _minRange + 32f;
        _attackCooldown = Parameters.GetFloat("attack-cooldown", 2f);
        _attackWindup = Parameters.GetFloat("attack-windup", 0.35f);
        _attackLock = Parameters.GetFloat("attack-lock", 0.4f);
        _losWidth = F("los-width", "los_width", 6f);
        AggroRadius = Parameters.GetFloat("aggro-radius", 0f);   // 0 = keep the base's own notice radius
    }

    // resolve a creature id exactly like EventsPlus/summon spells: pack id first, then an EntityIds field
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

    // prune dead/removed minions (EventsPlus liveness pattern), return the live count
    private int LiveMinionCount()
    {
        if (_minions.Count == 0) return 0;
        _gone.Clear();
        foreach (Guid id in _minions)
            if (!ServerGameState.Entities.ContainsKey(id)) _gone.Add(id);
        foreach (Guid id in _gone) _minions.Remove(id);
        return _minions.Count;
    }

    private bool CanSummon() => _minionBaseId != Guid.Empty && LiveMinionCount() < _maxMinions;

    private void SpawnMinions()
    {
        try
        {
            int want = Math.Min(_minionCount, _maxMinions - LiveMinionCount());
            for (int i = 0; i < want; i++, _ringK++)
            {
                double ang = _ringK * 2.39996;   // golden-angle spread (EventsPlus ring)
                float r = 36f + 10f * (i % 3);
                Vector3 pos = Entity.Position + new Vector3((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r, 0f);
                ServerEntityModel sem = EntityServerManager.SpawnEntity(new SpawnEntityArgs
                {
                    Id = Guid.NewGuid(),
                    BaseId = _minionBaseId,
                    WorldId = Entity.System.WorldId,
                    Position = pos,
                    EntityType = EntityType.Hostile,   // hostile to players; the minion's own AI takes over
                    DynamicSpawn = true,
                });
                if (sem != null) _minions.Add(sem.Id);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"forge_summoner on '{Entity.Name}': summon failed: {ex.Message}");
        }
    }

    public override StateMachine CreateStateMachine()
    {
        State baseState, wander, noPlayer, near, walk, idle, walkHome, aggro, approach, retreat, summon, attack, stand;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("wander", out wander, 1, 1)     // 1: wander -> aggro; 1 start-child: wakeUp
                    .Leaf("no_player_nearby", out noPlayer, 1)
                    .Branch("player_nearby", out near, 1, 1)
                        .Leaf("walk", out walk, 1)
                        .Leaf("idle", out idle, 1)
                        .Leaf("walk_home", out walkHome, 1)
                    .End()
                .End()
                .Branch("aggro", out aggro, 1)          // 1: aggro -> wander
                    .Leaf("approach", out approach, 4)  // 4: -> retreat/summon/attack/stand
                    .Leaf("retreat", out retreat, 1)    // 1: -> stand
                    .Leaf("summon", out summon, 1)      // 1: -> stand
                    .Leaf("attack", out attack, 1)      // 1: -> stand
                    .Leaf("stand", out stand, 4)        // 4: -> retreat/summon/attack/approach
                .End()
            .End()
            .Build();

        // BuildTargetingTree + a SECOND cooldown channel for summons (same CooldownTimer node, own keys)
        baseState.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new ParallelSequence())
            .Leaf(new TryGetThreateningEntity(TargetKey))
            .Leaf(new CooldownTimer(BehaviorMachine.Blackboard, _attackCooldown, CooldownTimerKey, OnCooldownKey))
            .Leaf(new CooldownTimer(BehaviorMachine.Blackboard, _summonCooldown, SummonTimerKey, SummonOnCooldownKey))
            .End()
            .Build();
        WireWanderBranch(wander, noPlayer, near, walk, idle, walkHome);

        approach.UpdateBehaviorTree = BTFactory.Chase(new ChaseBTSettings
        {
            TargetEntity = TargetKey,
            MoveAnim = AnimWalk,
            AccelerationScaling = RunSpeed,
        });
        retreat.UpdateBehaviorTree = BTFactory.Flee(new FleeBTSettings
        {
            Entity = Entity,
            MoveAnim = AnimWalk,
            AccelerationScaling = RunSpeed,
        });
        stand.EnterBehaviorTree = BTBuilderPool.Get().Leaf(new PlayAnimation(AnimIdle)).Build();
        stand.UpdateBehaviorTree = BTBuilderPool.Get().Leaf(new LookAt<EntityWrapper>(TargetKey)).Build();
        // the summon gesture: stop, face the target, attack anim, conjure, brief recovery, start cooldown
        summon.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new LookAt<EntityWrapper>(TargetKey))
            .Leaf(new PlayAnimationMsg((byte)AnimAttack))
            .Leaf(new Wait(_summonWindup))
            .Leaf(new CustomVoid(SpawnMinions))
            .Leaf(new Wait(0.4f))
            .Leaf(new ResetCooldownTimer(SummonTimerKey, SummonOnCooldownKey))
            .End()
            .Build();
        // the filler shot — forge_ranged's attack tree
        attack.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new LookAt<EntityWrapper>(TargetKey))
            .Leaf(new PlayAnimationMsg((byte)AnimAttack))
            .Composite(new ParallelSequence())
                .Leaf(new Wait(_attackWindup))
                .Composite(new Sequence())
                    .Leaf(new GetDirection<EntityWrapper, EntityWrapper>("targetEntityDirection", Entity, TargetKey))
                    .Leaf(new SmoothLookInDirection("targetEntityDirection", 360f))
                .End()
            .End()
            .Leaf(new CastSpellNode(new CastSpellNode.Args
            {
                SpellId = _rangedSpell,
                TargetEntity = TargetKey,
            }))
            .Leaf(new Wait(_attackLock))
            .Leaf(new ResetCooldownTimer(CooldownTimerKey, OnCooldownKey))
            .End()
            .Build();

        // priorities (add order): keep distance > summon > shoot > stand
        ref Transition approachToRetreat = ref approach.AddTransition(retreat);
        approachToRetreat.Condition = ConditionHelper.Leaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange));
        ref Transition approachToSummon = ref approach.AddTransition(summon);
        approachToSummon.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(SummonOnCooldownKey, false),
            new CustomBool(CanSummon),
        });
        ref Transition approachToAttack = ref approach.AddTransition(attack);
        approachToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new CustomBool(() => _hasSpell),
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange),
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange)),
            new IsInLos<EntityWrapper>(TargetKey, _losWidth),
        });
        ref Transition approachToStand = ref approach.AddTransition(stand);
        approachToStand.Condition = ConditionHelper.Leaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange));

        ref Transition retreatToStand = ref retreat.AddTransition(stand);
        retreatToStand.Condition = ConditionHelper.InvertLeaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange * 1.15f));

        ref Transition summonToStand = ref summon.AddTransition(stand);
        summonToStand.Condition = ConditionHelper.Leaf(new BehaviorTreeTerminated());
        ref Transition attackToStand = ref attack.AddTransition(stand);
        attackToStand.Condition = ConditionHelper.Leaf(new BehaviorTreeTerminated());

        ref Transition standToRetreat = ref stand.AddTransition(retreat);
        standToRetreat.Condition = ConditionHelper.Leaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange));
        ref Transition standToSummon = ref stand.AddTransition(summon);
        standToSummon.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(SummonOnCooldownKey, false),
            new CustomBool(CanSummon),
        });
        ref Transition standToAttack = ref stand.AddTransition(attack);
        standToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new CustomBool(() => _hasSpell),
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange),
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange)),
            new IsInLos<EntityWrapper>(TargetKey, _losWidth),
        });
        ref Transition standToApproach = ref stand.AddTransition(approach);
        standToApproach.Condition = ConditionHelper.Tree(BTBuilderPool.Get().Composite(new Selector())
            .InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange))
            .InvertLeaf(new IsInLos<EntityWrapper>(TargetKey, _losWidth))
            .End()
            .Build());

        ref Transition toAggro = ref wander.AddTransition(aggro);
        toAggro.Condition = ConditionHelper.Leaf(new HasThreateningEntity());
        ref Transition toWander = ref aggro.AddTransition(wander);
        toWander.Condition = ConditionHelper.InvertLeaf(new HasThreateningEntity());
        toWander.OnTransitionBT = BuildLoseAggroTree();

        return sm;
    }
}

// ------------------------------------------------ forge_healer -----------------------------------------------

// A flock medic: wanders normally, walks back toward its group when it strays past `follow-distance`, and
// every `heal-cooldown`s heals the MOST INJURED ally within `ally-radius` by `heal-amount`. "Ally" = same
// base creature OR any aggressive (hostile-AI) creature — never players. The heal is applied directly
// server-side via EntityServerManager.Heal (verified API; clients see it as a normal health update), with
// the attack anim as the cast gesture, an optional `heal-vfx` spac on the target, and an optional purely
// cosmetic `heal-spell` cast at it. The shared CooldownTimerKey channel of BuildTargetingTree is the HEAL
// cooldown here (this preset never attacks). Threatened, it ALWAYS flees — healers are fragile.
public sealed class ForgeHealerServerController : ForgeAiServerController
{
    private const string HealTargetKey = "forgeHealTarget";
    private const string FollowTargetKey = "forgeFollowEntity";

    private float _allyRadius;
    private float _healAmount;
    private float _healCooldown;
    private float _healWindup;
    private float _followDistance;
    private string _healSpell = "";
    private string _healVfx = "";

    private EntityWrapper _healTarget;     // cached by the transition probe, consumed by the heal tree
    private EntityWrapper _followTarget;   // cached by NeedRegroup, consumed by the follow tree

    protected override void ReadAiParameters()
    {
        _allyRadius = Parameters.GetFloat("ally-radius", 200f);
        _healAmount = Parameters.GetFloat("heal-amount", 10f);
        _healCooldown = Parameters.GetFloat("heal-cooldown", 3f);
        _healWindup = Parameters.GetFloat("heal-windup", 0.35f);
        _followDistance = Parameters.GetFloat("follow-distance", 150f);
        _healSpell = Parameters.GetString("heal-spell", "") ?? "";
        _healVfx = Parameters.GetString("heal-vfx", "") ?? "";
        AggroRadius = Parameters.GetFloat("aggro-radius", 0f);   // 0 = keep the base's own notice radius
    }

    private bool IsAlly(EntityWrapper e)
        => e != null && !e.Removed && e.Id != Entity.Id
            && e.Mask.HasFlags(Component.Health)
            && e.BaseGuid != EntityIds.PlayerBaseEntityGuid
            && (e.BaseGuid == Entity.BaseGuid || e.Mask.HasFlags(Component.Aggressive));

    private bool TryFindMostInjuredAlly()
    {
        _healTarget = null;
        try
        {
            float worstFrac = 1f;
            foreach (EntityWrapper e in Entity.System.GetEntitiesInCircleArea(Entity.Position2, _allyRadius))
            {
                if (!IsAlly(e) || e.MaxHealth <= 0f) continue;
                float frac = e.Health / e.MaxHealth;
                if (frac < worstFrac - 0.001f) { worstFrac = frac; _healTarget = e; }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"forge_healer on '{Entity.Name}': ally scan failed: {ex.Message}");
        }
        return _healTarget != null;   // only sub-full allies qualify (worstFrac starts at 1)
    }

    // strayed from the flock? cache the nearest ally to walk back to
    private bool NeedRegroup()
    {
        _followTarget = null;
        try
        {
            float best = float.MaxValue;
            foreach (EntityWrapper e in Entity.System.GetEntitiesInCircleArea(Entity.Position2, _allyRadius * 2f))
            {
                if (!IsAlly(e)) continue;
                float d = Vector2.DistanceSquared(e.Position2, Entity.Position2);
                if (d < best) { best = d; _followTarget = e; }
            }
            if (_followTarget == null || best <= _followDistance * _followDistance) { _followTarget = null; }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"forge_healer on '{Entity.Name}': regroup scan failed: {ex.Message}");
        }
        return _followTarget != null;
    }

    private void DoHeal()
    {
        try
        {
            EntityWrapper t = _healTarget;
            if (t == null || t.Removed) return;
            if (!ServerGameState.Entities.TryGetValue(t.Id, out ServerEntityModel sem) || sem == null) return;
            EntityServerManager.Heal(sem, _healAmount);
            if (_healVfx.Length > 0)
                ServerSoundAndVfxService.Send_PlayVfxOnEntity(new PlayVfxOnEntityMessage
                {
                    SpacVfxId = _healVfx,
                    EntityId = t.Id,
                });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"forge_healer on '{Entity.Name}': heal failed: {ex.Message}");
        }
    }

    public override StateMachine CreateStateMachine()
    {
        State baseState, calm, wander, noPlayer, near, walk, idle, walkHome, follow, heal, flee;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("calm", out calm, 1)            // 1: calm -> flee
                    .Branch("wander", out wander, 2, 1) // 2: -> heal, -> follow; 1 start-child: wakeUp
                        .Leaf("no_player_nearby", out noPlayer, 1)
                        .Branch("player_nearby", out near, 1, 1)
                            .Leaf("walk", out walk, 1)
                            .Leaf("idle", out idle, 1)
                            .Leaf("walk_home", out walkHome, 1)
                        .End()
                    .End()
                    .Leaf("follow", out follow, 2)      // 2: -> heal, -> wander
                    .Leaf("heal", out heal, 1)          // 1: -> wander
                .End()
                .Leaf("flee", out flee, 1)              // 1: -> calm
            .End()
            .Build();

        // the shared cooldown channel IS the heal cooldown for this preset
        baseState.UpdateBehaviorTree = BuildTargetingTree(_healCooldown);
        WireWanderBranch(wander, noPlayer, near, walk, idle, walkHome);

        follow.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new CustomBool((ctx, bb, _) =>
            {
                EntityWrapper t = _followTarget;
                if (t == null || t.Removed) return false;
                bb.SetValue(FollowTargetKey, t);
                return true;
            }))
            .SubTree(BTFactory.MoveToEntity(FollowTargetKey, AnimWalk, WalkSpeed, 40f))
            .End()
            .Build();

        // the heal gesture: stop, face the patient, attack anim, windup, heal (+vfx), optional cosmetic
        // spell at it, brief recovery, start the cooldown
        BTBuilder healTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new CustomBool((ctx, bb, _) =>
            {
                EntityWrapper t = _healTarget;
                if (t == null || t.Removed) return false;
                bb.SetValue(HealTargetKey, t);
                return true;
            }))
            .Leaf(new LookAt<EntityWrapper>(HealTargetKey))
            .Leaf(new PlayAnimationMsg((byte)AnimAttack))
            .Leaf(new Wait(_healWindup))
            .Leaf(new CustomVoid(DoHeal));
        if (_healSpell.Length > 0)
            healTree = healTree.Leaf(new CastSpellNode(new CastSpellNode.Args
            {
                SpellId = _healSpell,
                TargetEntity = HealTargetKey,
            }));
        heal.UpdateBehaviorTree = healTree
            .Leaf(new Wait(0.3f))
            .Leaf(new ResetCooldownTimer(CooldownTimerKey, OnCooldownKey))
            .End()
            .Build();

        flee.UpdateBehaviorTree = BTFactory.Flee(new FleeBTSettings
        {
            Entity = Entity,
            MoveAnim = AnimWalk,
            AccelerationScaling = RunSpeed,
        });

        // ALWAYS flee from threats — the calm branch yields wherever its children are
        ref Transition calmToFlee = ref calm.AddTransition(flee);
        calmToFlee.Condition = ConditionHelper.Leaf(new HasThreateningEntity());
        ref Transition fleeToCalm = ref flee.AddTransition(calm);
        fleeToCalm.Condition = ConditionHelper.InvertLeaf(new HasThreateningEntity());
        fleeToCalm.OnTransitionBT = BuildLoseAggroTree();

        // heal first, regroup second; the Wait leaves throttle the area scans (vanilla FoW-poll pattern)
        ref Transition wanderToHeal = ref wander.AddTransition(heal);
        wanderToHeal.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, false),
            new Wait(0.25f),
            new CustomBool(TryFindMostInjuredAlly),
        });
        ref Transition wanderToFollow = ref wander.AddTransition(follow);
        wanderToFollow.Condition = ConditionHelper.Sequence(new Node[]
        {
            new Wait(0.5f),
            new CustomBool(NeedRegroup),
        });

        ref Transition followToHeal = ref follow.AddTransition(heal);
        followToHeal.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, false),
            new Wait(0.25f),
            new CustomBool(TryFindMostInjuredAlly),
        });
        ref Transition followToWander = ref follow.AddTransition(wander);
        followToWander.Condition = ConditionHelper.Leaf(new BehaviorTreeTerminated());

        ref Transition healToWander = ref heal.AddTransition(wander);
        healToWander.Condition = ConditionHelper.Leaf(new BehaviorTreeTerminated());

        return sm;
    }
}

// ------------------------------------------------ forge_turret -----------------------------------------------

// A living ballista: two states, wait and shoot. No wander, no chase, no flee, no leash — it NEVER moves
// on its own. Casts `ranged-spell` (required) at the highest-threat entity within `attack-radius` every
// `attack-cooldown` seconds, line of sight required. The aggro field is widened to the attack radius by
// default so the threat table actually fills at firing range (override with `aggro-radius`). Needs an
// Aggressive base creature like every other preset (the shared base warns otherwise).
public sealed class ForgeTurretServerController : ForgeAiServerController
{
    private string _rangedSpell = "";
    private float _attackRadius;
    private float _attackCooldown;
    private float _attackWindup;
    private float _attackLock;
    private float _losWidth;

    protected override void ReadAiParameters()
    {
        _rangedSpell = Parameters.GetString("ranged-spell", "") ?? "";
        if (string.IsNullOrWhiteSpace(_rangedSpell))
        {
            // REQUIRED param — fall back to the base's own attack spell so the turret visibly works
            _rangedSpell = Parameters.GetString("attack_spell", "skeleton_archer_arrow:0");
            Plugin.Log.LogWarning($"forge_turret on '{Entity.Name}': required controllerParam 'ranged-spell' " +
                $"is missing — falling back to '{_rangedSpell}'.");
        }
        _attackRadius = Parameters.GetFloat("attack-radius", 260f);
        _attackCooldown = Parameters.GetFloat("attack-cooldown", 1.5f);
        _attackWindup = Parameters.GetFloat("attack-windup", 0.35f);
        _attackLock = Parameters.GetFloat("attack-lock", 0.3f);
        _losWidth = F("los-width", "los_width", 6f);
        AggroRadius = Parameters.GetFloat("aggro-radius", _attackRadius);
    }

    public override StateMachine CreateStateMachine()
    {
        State baseState, idle, attack;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Leaf("idle", out idle, 1)      // 1: idle -> attack
                .Leaf("attack", out attack, 1)  // 1: attack -> idle
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(_attackCooldown);

        // wait: kill any knockback drift, sit in the idle anim (no movement nodes anywhere in this machine)
        idle.EnterBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new PlayAnimation(AnimIdle))
            .End()
            .Build();

        // the shot — forge_ranged's attack tree (face through the windup, cast AT the target, recover)
        attack.UpdateBehaviorTree = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new LookAt<EntityWrapper>(TargetKey))
            .Leaf(new PlayAnimationMsg((byte)AnimAttack))
            .Composite(new ParallelSequence())
                .Leaf(new Wait(_attackWindup))
                .Composite(new Sequence())
                    .Leaf(new GetDirection<EntityWrapper, EntityWrapper>("targetEntityDirection", Entity, TargetKey))
                    .Leaf(new SmoothLookInDirection("targetEntityDirection", 360f))
                .End()
            .End()
            .Leaf(new CastSpellNode(new CastSpellNode.Args
            {
                SpellId = _rangedSpell,
                TargetEntity = TargetKey,
            }))
            .Leaf(new Wait(_attackLock))
            .Leaf(new ResetCooldownTimer(CooldownTimerKey, OnCooldownKey))
            .End()
            .Build();

        ref Transition idleToAttack = ref idle.AddTransition(attack);
        idleToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new HasThreateningEntity(),
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _attackRadius),
            new IsInLos<EntityWrapper>(TargetKey, _losWidth),
        });
        ref Transition attackToIdle = ref attack.AddTransition(idle);
        attackToIdle.Condition = ConditionHelper.Leaf(new BehaviorTreeTerminated());

        return sm;
    }
}
