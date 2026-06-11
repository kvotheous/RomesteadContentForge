using BM;                                   // BehaviorMachine: SMBuilder/State/Transition + every BT node (lives in CandideServer.dll)
using CandideServer.Entities;               // ServerEntityControllerLoader (server controller name -> Type map)
using CandideServer.Entities.Controllers;   // ServerBehaviorMachineController (the modern creature-AI base)
using HarmonyLib;
using Microsoft.Xna.Framework;              // Vector2
using Shared.Entity;                        // EntityWrapper
using Shared.Entity.Aggro;                  // CircularAggroField (aggro-radius override)
using Shared.Entity.Components;             // Component (Aggressive mask check)
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// CREATURE AI PRESETS — server-side AI controllers a pack can put on any custom creature via
// `creatures[].controller` (the existing ContentLoader.RegisterCreatures swap; the client keeps the BASE
// creature's controller, so visuals/animations are untouched). Three presets:
//
//   "forge_guard"  — wanders near its spawn point (leash `guard-radius`), aggros threats within
//                    `aggro-radius`, chases + melees like a skeleton warrior, and ABANDONS the chase
//                    (walks back to its post) the moment the target leaves the leash.
//   "forge_coward" — vanilla-ish wander/chase/melee, but FLEES (vanilla flee steering) once
//                    Health/MaxHealth drops below `flee-threshold`, until the threat is far or it heals.
//   "forge_ranged" — keeps its distance: retreats when closer than `min-range`, approaches when farther
//                    than `max-range`, and casts `ranged-spell` (REQUIRED param — any pack or vanilla
//                    spell id) at the target every `attack-cooldown` seconds. Wolf + forge_ranged +
//                    "ranged-spell": "mymod:firebolt" = a wolf that spits fireballs.
//
// ROUTE TAKEN (all three): direct subclasses of ServerBehaviorMachineController overriding its single
// abstract member CreateStateMachine(), built from the same PUBLIC vanilla pieces the game's own creatures
// use — SMBuilder state machines, BTBuilderPool behavior trees, BTFactory.Wander/WanderWalkHome/Chase/
// Flee/IdleWithRange, TryGetThreateningEntity/HasThreateningEntity (the ThreatTable-based target pick),
// CooldownTimer/ResetCooldownTimer, CastSpellNode (-> SpellServerManager.TryCastSpell). Each machine
// mirrors a vanilla blueprint line-for-line where possible:
//   guard/coward combat = ServerSkeletonWarriorController.CreateStateMachine (wander/chase/attack/idle,
//   melee BT: PlayAnimationMsg(idle) -> LookAt -> charge -> PlayAnimationMsg(attack) -> CastSpellNode);
//   ranged = ServerSkeletonArcherController (chase/attack/idle/flee with range-band transitions).
// Deliberately NOT subclasses of the skeleton controllers themselves: those leak family behavior
// (OnKilled spawns a JumpingSkull!) and hardcode skeleton anim bytes.
//
// ANIM VOCABULARY (the honest hard part): the server drives the client's animation purely through
// EntityServerManager.UpdateEntityState ints (+ a one-shot EntityAnimationMessage byte from
// PlayAnimationMsg). WHICH int means what is decided by the base creature's CLIENT controller — there is
// no shared vocabulary. The three ints these presets use are parameters with skeleton-family defaults:
//   anim-idle (0), anim-walk (1), anim-attack (2)
// Verified vanilla maps: SkeletonWarrior 0=idle 1=walk 2=attack (4/5 = secondary windup/swing);
// SkeletonArcher 0=idle 1=walk 2..5=grab/load/draw/shoot; Wolf 0=idle 1=walk 2=run (NO attack int — its
// bite is a leap + vfx byte); SmallScorpion 0=idle 1=walk 2=run 3=attack. Skeleton-family bases work out
// of the box; for other bases the right ints are trial-and-error (there is no data source to read them
// from — try 0..5 and watch, or read the base's client controller in a decompiler).
//
// PARAMETERS: the creature swap in ContentLoader.RegisterCreatures copies the BASE controller's full
// parameter set (its .cdx values) before merging the pack's controllerParams, so these presets read the
// base's own combat numbers (melee_attack_spell, melee_attack_range, walk_speed, ...) as defaults and
// expose forge-style kebab keys to override them. All controllerParams values are strings, like every
// other forge preset.
//
// REGISTRATION: server-side ONLY (clients keep the base's controller; in co-op only the host simulates
// creatures — same authority model as the spawners). Injected into ServerEntityControllerLoader.ControllerMap
// by an Initialize postfix, the exact Interactive.cs mechanism, but NOT via InteractiveFeature.RegisterController
// (that registers client+server PAIRS — a null client Type would poison the client map for anyone who
// reuses the name on furniture). Concrete classes keep parameterless ctors (CreateController news them up).
// ---------------------------------------------------------------------------------------------------------

internal static class CreatureAiFeature
{
    // pre-normalized names (lowercase, no "server"/"controller" affixes) -> server controller types.
    internal static readonly Dictionary<string, Type> ServerAiControllers = new()
    {
        ["forge_guard"] = typeof(ForgeGuardServerController),
        ["forge_coward"] = typeof(ForgeCowardServerController),
        ["forge_ranged"] = typeof(ForgeRangedServerController),
    };

    // Called from Plugin.Load. IMPORTANT: the map must NOT be seeded while still empty — vanilla
    // Initialize() early-outs when Count > 0 and would then never add the game's own controllers. So this
    // only injects if vanilla already filled the map; the Initialize postfix below is the normal path.
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

// Insert the AI types right after the game builds the SERVER controller map (Initialize only scans the
// game's own assembly; it early-outs once filled, so "just after it filled" is the reliable hook — the
// same chokepoint InteractiveControllerMapServerPatch uses). Runs before any LoadDoodadDatabase, which is
// where ContentLoader.RegisterCreatures resolves `controller` names via CreateController.
internal static class CreatureAiControllerMapPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityControllerLoader), nameof(ServerEntityControllerLoader.Initialize))]
    private static void Postfix()
    {
        try { CreatureAiFeature.Inject(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Creature-AI controller-map inject failed: {e.Message}"); }
    }
}

// ------------------------------------------------ shared base ------------------------------------------------

// Common plumbing for the three presets: parameter reading (forge key -> base .cdx key -> hard default),
// the standard wander branch (FoW-gated idle/walk/walk-home — mirrors every vanilla BM controller), the
// base-state targeting tree, and the optional aggro-radius override.
public abstract class ForgeAiServerController : ServerBehaviorMachineController
{
    // blackboard keys — same names the vanilla controllers use
    protected const string TargetKey = "targetEntity";
    protected const string CooldownTimerKey = "attackCooldownTimer";
    protected const string OnCooldownKey = "attackOnCooldown";

    protected float WalkSpeed;
    protected float RunSpeed;
    protected bool WanderHome;
    protected float HomeRadius;
    protected float MinWanderDistance, MaxWanderDistance;
    protected float MinWanderIdle, MaxWanderIdle;
    protected float PauseAfterAggro;
    protected int AnimIdle, AnimWalk, AnimAttack;
    protected float AggroRadius;   // px; 0 = keep the base entity's own AggroField

    // forge key first, then the base controller's vanilla key (copied from the base's .cdx), then a default
    protected float F(string forgeKey, string vanillaKey, float def)
        => Parameters.GetFloat(forgeKey, Parameters.GetFloat(vanillaKey, def));

    protected float F2(string forgeKey, string vanillaKey1, string vanillaKey2, float def)
        => Parameters.GetFloat(forgeKey, Parameters.GetFloat(vanillaKey1, Parameters.GetFloat(vanillaKey2, def)));

    public sealed override void EntityInitialize()
    {
        WalkSpeed = F("walk-speed", "walk_speed", 0.66f);
        RunSpeed = F("run-speed", "run_speed", 1f);
        WanderHome = Parameters.GetBool("should_wander_home");
        HomeRadius = F("wander-home-radius", "wander_home_radius", 256f);
        MinWanderDistance = Parameters.GetFloat("min_wander_distance", 32f);
        MaxWanderDistance = Parameters.GetFloat("max_wander_distance", 160f);
        MinWanderIdle = Parameters.GetFloat("min_wander_idle_duration", 0.5f);
        MaxWanderIdle = Parameters.GetFloat("max_wander_idle_duration", 2f);
        PauseAfterAggro = Parameters.GetFloat("pause_duration_after_losing_aggro", 0.7f);
        AnimIdle = Parameters.GetInt("anim-idle", 0);
        AnimWalk = Parameters.GetInt("anim-walk", 1);
        AnimAttack = Parameters.GetInt("anim-attack", 2);

        ReadAiParameters();        // preset-specific params (may override the common ones above)

        base.EntityInitialize();   // sets SpawnPosition, news the BehaviorMachine, calls CreateStateMachine()

        if (!Entity.Mask.HasFlags(Component.Aggressive))
            Plugin.Log.LogWarning($"{GetType().Name} on '{Entity.Name}': base entity has no Aggressive " +
                "component — it will never acquire a target (pick a hostile-creature base).");
        else if (AggroRadius > 0f)
            // The ThreatTable is filled by the entity's AggroField proximity scan (AggressiveComponent
            // .UpdateTrigger); swapping the field is how we control WHEN it notices a player. Radii are in
            // TILES (16 px). Loss radius past which a chased target is dropped from the table.
            Entity.AggroField = new CircularAggroField(AggroRadius / 16f, AggroLossRadius() / 16f, AggroRadius / 16f);
    }

    // read preset params; runs before the state machine is built
    protected abstract void ReadAiParameters();

    protected virtual float AggroLossRadius() => AggroRadius * 1.5f;

    // The shared "base" state tree: pick the highest-threat entity into the blackboard + tick the attack
    // cooldown. Runs every frame in parallel with whatever child state is active (vanilla pattern).
    protected BehaviorTree BuildTargetingTree(float attackCooldown)
        => BTBuilderPool.Get().Composite(new ParallelSequence())
            .Leaf(new TryGetThreateningEntity(TargetKey))
            .Leaf(new CooldownTimer(BehaviorMachine.Blackboard, attackCooldown, CooldownTimerKey, OnCooldownKey))
            .End()
            .Build();

    // The standard wander branch, mirroring ServerSkeletonWarriorController: sleep while outside the fog
    // of war, otherwise idle/walk in a circle (around the spawn point when WanderHome), and run home when
    // beyond HomeRadius. Caller creates the states (capacities: wander Branch(1,1) -> noPlayer Leaf(1),
    // near Branch(1,1) -> walk(1)/idle(1)/walkHome(1)) and wires its own wander<->aggro transitions.
    protected void WireWanderBranch(State wander, State noPlayer, State near, State walk, State idle, State walkHome)
    {
        idle.UpdateBehaviorTree = BTFactory.IdleWithRange(AnimIdle, MinWanderIdle, MaxWanderIdle);
        walk.UpdateBehaviorTree = BTFactory.Wander(new WanderBTSettings
        {
            Blackboard = BehaviorMachine.Blackboard,
            MoveAnim = AnimWalk,
            ShouldWanderHome = WanderHome,
            SpawnPosition = SpawnPosition,
            MinWanderDistance = MinWanderDistance,
            MaxWanderDistance = MaxWanderDistance,
            AccelerationScaling = WalkSpeed,
        });
        walkHome.UpdateBehaviorTree = BTFactory.WanderWalkHome(new WanderWalkHomeBTSettings
        {
            HomePosition = SpawnPosition,
            MoveAnim = AnimWalk,
            HomeRadius = HomeRadius,
            AccelerationScaling = RunSpeed,
        });

        ref Transition wakeUp = ref wander.AddStartChildTransition(near);
        wakeUp.Condition = ConditionHelper.Leaf(new CustomBool(() => !EnemyUtil.IsOutsideFoW(Entity)));

        ref Transition goHome = ref near.AddStartChildTransition(walkHome);
        goHome.Condition = ConditionHelper.Sequence(new Node[]
        {
            new CustomBool(() => WanderHome),
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, Vector2>(Entity, SpawnPosition, HomeRadius)),
        });

        ref Transition nearToSleep = ref near.AddTransition(noPlayer);
        nearToSleep.Condition = ConditionHelper.Sequence(new Node[]
        {
            new Wait(0.5f),
            new CustomBool(() => EnemyUtil.IsOutsideFoW(Entity)),
        });
        ref Transition sleepToNear = ref noPlayer.AddTransition(near);
        sleepToNear.Condition = ConditionHelper.Sequence(new Node[]
        {
            new Wait(0.5f),
            new CustomBool(() => !EnemyUtil.IsOutsideFoW(Entity)),
        });

        ref Transition idleToWalk = ref idle.AddTransition(walk);
        idleToWalk.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());
        ref Transition walkToIdle = ref walk.AddTransition(idle);
        walkToIdle.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());
        ref Transition homeToIdle = ref walkHome.AddTransition(idle);
        homeToIdle.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());
    }

    // the standard "lost aggro" cool-off: stop, idle anim, short pause (vanilla's reference9.OnTransitionBT)
    protected BehaviorTree BuildLoseAggroTree()
        => BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new StopMoving())
            .Leaf(new PlayAnimation(AnimIdle))
            .Leaf(new Wait(PauseAfterAggro))
            .End()
            .Build();
}

// Shared melee combat (guard + coward): warrior-style chase/attack/stand-idle triangle + the warrior's
// melee attack tree, with the spell/range/timing read from the copied base params.
public abstract class ForgeMeleeAiServerController : ForgeAiServerController
{
    protected string MeleeSpell = "";
    protected float MeleeRange;
    protected float AttackCharge;
    protected float AttackDelay;
    protected float AttackLock;
    protected float AttackCooldown;

    protected void ReadMeleeParameters()
    {
        // base-param fallbacks cover the whole BM family: warrior (melee_attack_*), wolf/scorpion/archer (attack_*)
        MeleeSpell = Parameters.GetString("melee-spell",
            Parameters.GetString("melee_attack_spell",
                Parameters.GetString("attack_spell", "skeleton_warrior_slash:0")));
        MeleeRange = F2("melee-range", "melee_attack_range", "attack_range", 14f);
        AttackCharge = F2("attack-charge", "melee_attack_charge_duration", "attack_charge_time", 0.15f);
        AttackDelay = F2("attack-delay", "melee_attack_spell_trigger_delay", "attack_spell_trigger_delay", 0.45f);
        AttackLock = F2("attack-lock", "melee_attack_movement_lock_duration", "attack_movement_lock_duration", 0.4f);
        AttackCooldown = F2("attack-cooldown", "melee_attack_cooldown", "attack_cooldown", 0f);
    }

    // Mirror of the warrior's melee attack BT (sans its hardcoded sword sfx): face the target while
    // charging, play the attack anim, cast the melee spell after the swing delay, then lock briefly.
    protected BehaviorTree BuildMeleeAttackTree()
        => BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new PlayAnimationMsg((byte)AnimIdle))
            .Leaf(new LookAt<EntityWrapper>(TargetKey))
            .Composite(new ParallelSequence())
                .Leaf(new Wait(AttackCharge))
                .Composite(new Sequence())
                    .Leaf(new GetDirection<EntityWrapper, EntityWrapper>("targetEntityDirection", Entity, TargetKey))
                    .Leaf(new SmoothLookInDirection("targetEntityDirection", 360f))
                .End()
            .End()
            .Leaf(new PlayAnimationMsg((byte)AnimAttack))
            .Leaf(new Wait(AttackDelay))
            .Leaf(new CastSpellNode(new CastSpellNode.Args { SpellId = MeleeSpell }))
            .Leaf(new Wait(AttackLock))
            .Leaf(new ResetCooldownTimer(CooldownTimerKey, OnCooldownKey))
            .End()
            .Build();

    // The warrior's six chase/attack/stand transitions. NOTE: transitions are checked in ADD order, so a
    // subclass adds any higher-priority exits (coward's flee) on these states BEFORE calling this.
    protected void WireMeleeTriangle(State chase, State attack, State stand)
    {
        chase.UpdateBehaviorTree = BTFactory.Chase(new ChaseBTSettings
        {
            TargetEntity = TargetKey,
            MoveAnim = AnimWalk,
            AccelerationScaling = RunSpeed,
        });
        attack.UpdateBehaviorTree = BuildMeleeAttackTree();
        stand.EnterBehaviorTree = BTBuilderPool.Get().Leaf(new PlayAnimation(AnimIdle)).Build();
        stand.UpdateBehaviorTree = BTBuilderPool.Get().Leaf(new LookAt<EntityWrapper>(TargetKey)).Build();

        ref Transition chaseToAttack = ref chase.AddTransition(attack);
        chaseToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange),
        });
        ref Transition chaseToStand = ref chase.AddTransition(stand);
        chaseToStand.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, true),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange),
        });
        ref Transition attackToChase = ref attack.AddTransition(chase);
        attackToChase.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BehaviorTreeSucceeded(),
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange)),
        });
        ref Transition attackToStand = ref attack.AddTransition(stand);
        attackToStand.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BehaviorTreeSucceeded(),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange),
        });
        ref Transition standToAttack = ref stand.AddTransition(attack);
        standToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange),
        });
        ref Transition standToChase = ref stand.AddTransition(chase);
        standToChase.Condition = ConditionHelper.Sequence(new Node[]
        {
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, MeleeRange)),
            new Wait(0.15f),
        });
    }
}

// ------------------------------------------------ forge_guard ------------------------------------------------

// A post guard: wanders within `guard-radius` of its spawn point, aggros threats inside `aggro-radius`,
// fights like a skeleton warrior, and breaks off + walks home the moment the target (or it) leaves the
// leash. The leash is enforced twice: the aggro transition requires the target inside the leash, and the
// de-aggro transition clears the threat table so the guard doesn't ping-pong at the leash edge.
public sealed class ForgeGuardServerController : ForgeMeleeAiServerController
{
    private float _guardRadius;

    protected override void ReadAiParameters()
    {
        _guardRadius = Parameters.GetFloat("guard-radius", 160f);
        AggroRadius = Parameters.GetFloat("aggro-radius", 200f);
        ReadMeleeParameters();
        // the "guard feel": always wander around the POST, never drift away
        WanderHome = true;
        HomeRadius = _guardRadius;
        if (MaxWanderDistance > _guardRadius) MaxWanderDistance = _guardRadius;
        if (MinWanderDistance > MaxWanderDistance) MinWanderDistance = MaxWanderDistance * 0.25f;
    }

    // let a chased target be dropped by the threat system itself once it's clearly out of reach
    protected override float AggroLossRadius() => _guardRadius + AggroRadius;

    public override StateMachine CreateStateMachine()
    {
        State baseState, wander, noPlayer, near, walk, idle, walkHome, aggro, chase, attack, stand;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("wander", out wander, 1, 1)
                    .Leaf("no_player_nearby", out noPlayer, 1)
                    .Branch("player_nearby", out near, 1, 1)
                        .Leaf("walk", out walk, 1)
                        .Leaf("idle", out idle, 1)
                        .Leaf("walk_home", out walkHome, 1)
                    .End()
                .End()
                .Branch("aggro", out aggro, 1)
                    .Leaf("chase", out chase, 2)
                    .Leaf("attack", out attack, 2)
                    .Leaf("stand", out stand, 2)
                .End()
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(AttackCooldown);
        WireWanderBranch(wander, noPlayer, near, walk, idle, walkHome);
        WireMeleeTriangle(chase, attack, stand);

        float leashSq = _guardRadius * _guardRadius;

        // engage only while the target is inside the leash (AggroField already gates the notice radius)
        ref Transition toAggro = ref wander.AddTransition(aggro);
        toAggro.Condition = ConditionHelper.Sequence(new Node[]
        {
            new HasThreateningEntity(),
            new CustomBool(() =>
            {
                EntityWrapper t = Entity.TargetOfAggro;
                return t != null && Vector2.DistanceSquared(t.Position2, SpawnPosition) <= leashSq;
            }),
        });

        // disengage when the target is gone OR has left the leash; wipe the threat table on the way out so
        // HasThreateningEntity doesn't immediately re-trigger while the target loiters outside the leash
        ref Transition toWander = ref aggro.AddTransition(wander);
        toWander.Condition = ConditionHelper.Leaf(new CustomBool(() =>
        {
            EntityWrapper t = Entity.TargetOfAggro;
            return t == null || Vector2.DistanceSquared(t.Position2, SpawnPosition) > leashSq;
        }));
        toWander.OnTransitionBT = BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new CustomVoid(() =>
            {
                Entity.ThreatTable.ThreateningEntities.Clear();
                Entity.TargetOfAggro = null;
            }))
            .Leaf(new StopMoving())
            .Leaf(new PlayAnimation(AnimIdle))
            .Leaf(new Wait(PauseAfterAggro))
            .End()
            .Build();

        return sm;
    }
}

// ------------------------------------------------ forge_coward ------------------------------------------------

// Vanilla-ish wander/chase/melee, but once Health/MaxHealth < `flee-threshold` it runs (the vanilla flee
// steering the skeleton archer uses) until the threat is farther than `flee-safe-distance` or gone. Out
// of combat the base class's own regeneration (10% per 5s with an empty threat table) heals it back up,
// so a recovered coward re-engages.
public sealed class ForgeCowardServerController : ForgeMeleeAiServerController
{
    private float _fleeThreshold;
    private float _fleeSafeDistance;

    protected override void ReadAiParameters()
    {
        _fleeThreshold = Parameters.GetFloat("flee-threshold", 0.35f);
        _fleeSafeDistance = Parameters.GetFloat("flee-safe-distance", 260f);
        AggroRadius = Parameters.GetFloat("aggro-radius", 0f);   // 0 = keep the base's own notice radius
        ReadMeleeParameters();
    }

    private bool Scared()
        => Entity.MaxHealth > 0f && Entity.Health / Entity.MaxHealth < _fleeThreshold;

    public override StateMachine CreateStateMachine()
    {
        State baseState, wander, noPlayer, near, walk, idle, walkHome, aggro, chase, attack, stand, flee;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("wander", out wander, 1, 1)
                    .Leaf("no_player_nearby", out noPlayer, 1)
                    .Branch("player_nearby", out near, 1, 1)
                        .Leaf("walk", out walk, 1)
                        .Leaf("idle", out idle, 1)
                        .Leaf("walk_home", out walkHome, 1)
                    .End()
                .End()
                .Branch("aggro", out aggro, 1)
                    .Leaf("chase", out chase, 3)
                    .Leaf("attack", out attack, 3)
                    .Leaf("stand", out stand, 3)
                    .Leaf("flee", out flee, 2)
                .End()
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(AttackCooldown);
        WireWanderBranch(wander, noPlayer, near, walk, idle, walkHome);

        flee.UpdateBehaviorTree = BTFactory.Flee(new FleeBTSettings
        {
            Entity = Entity,
            MoveAnim = AnimWalk,
            AccelerationScaling = RunSpeed,
        });

        // flee exits FIRST on each combat state (transitions are checked in add order = priority)
        ref Transition chaseToFlee = ref chase.AddTransition(flee);
        chaseToFlee.Condition = ConditionHelper.Leaf(new CustomBool(Scared));
        ref Transition attackToFlee = ref attack.AddTransition(flee);
        attackToFlee.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BehaviorTreeSucceeded(),          // never cut a swing mid-animation
            new CustomBool(Scared),
        });
        ref Transition standToFlee = ref stand.AddTransition(flee);
        standToFlee.Condition = ConditionHelper.Sequence(new Node[]
        {
            new CustomBool(Scared),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _fleeSafeDistance * 0.9f),
        });

        WireMeleeTriangle(chase, attack, stand);

        // stop running once it healed back up (re-engage) or shook the threat off far enough (stand)
        ref Transition fleeToChase = ref flee.AddTransition(chase);
        fleeToChase.Condition = ConditionHelper.Sequence(new Node[]
        {
            new CustomBool(() => !Scared()),
            new HasThreateningEntity(),
        });
        ref Transition fleeToStand = ref flee.AddTransition(stand);
        fleeToStand.Condition = ConditionHelper.InvertLeaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _fleeSafeDistance));

        ref Transition toAggro = ref wander.AddTransition(aggro);
        toAggro.Condition = ConditionHelper.Leaf(new HasThreateningEntity());
        ref Transition toWander = ref aggro.AddTransition(wander);
        toWander.Condition = ConditionHelper.InvertLeaf(new HasThreateningEntity());
        toWander.OnTransitionBT = BuildLoseAggroTree();

        return sm;
    }
}

// ------------------------------------------------ forge_ranged ------------------------------------------------

// A kiting caster: approaches when farther than `max-range`, retreats when closer than `min-range`, and
// inside the band casts `ranged-spell` at the target every `attack-cooldown` seconds (line of sight
// required). Range-band state machine mirrors ServerSkeletonArcherController's chasing/attacking/idle/
// fleeing layout; the spell fires through the same CastSpellNode every vanilla creature attack uses, so
// ANY castable spell id works — a pack spell ("mymod:firebolt") or a vanilla one.
public sealed class ForgeRangedServerController : ForgeAiServerController
{
    private string _rangedSpell = "";
    private float _minRange;
    private float _maxRange;
    private float _attackCooldown;
    private float _attackWindup;
    private float _attackLock;
    private float _losWidth;

    protected override void ReadAiParameters()
    {
        _rangedSpell = Parameters.GetString("ranged-spell", "");
        if (string.IsNullOrWhiteSpace(_rangedSpell))
        {
            // REQUIRED param — fall back to the base's own attack spell so the creature visibly works
            _rangedSpell = Parameters.GetString("attack_spell", "skeleton_archer_arrow:0");
            Plugin.Log.LogWarning($"forge_ranged on '{Entity.Name}': required controllerParam 'ranged-spell' " +
                $"is missing — falling back to '{_rangedSpell}'.");
        }
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

    public override StateMachine CreateStateMachine()
    {
        State baseState, wander, noPlayer, near, walk, idle, walkHome, aggro, approach, retreat, attack, stand;
        StateMachine sm = new SMBuilder()
            .Branch("base", out baseState)
                .Branch("wander", out wander, 1, 1)
                    .Leaf("no_player_nearby", out noPlayer, 1)
                    .Branch("player_nearby", out near, 1, 1)
                        .Leaf("walk", out walk, 1)
                        .Leaf("idle", out idle, 1)
                        .Leaf("walk_home", out walkHome, 1)
                    .End()
                .End()
                .Branch("aggro", out aggro, 1)
                    .Leaf("approach", out approach, 3)
                    .Leaf("retreat", out retreat, 1)
                    .Leaf("attack", out attack, 3)
                    .Leaf("stand", out stand, 3)
                .End()
            .End()
            .Build();

        baseState.UpdateBehaviorTree = BuildTargetingTree(_attackCooldown);
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
        // the shot: stop, face the target through the windup, cast AT it, brief recovery, start cooldown
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

        // range-band transitions; retreat is always checked first (added first = highest priority)
        ref Transition approachToRetreat = ref approach.AddTransition(retreat);
        approachToRetreat.Condition = ConditionHelper.Leaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange));
        ref Transition approachToAttack = ref approach.AddTransition(attack);
        approachToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, false),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange),
            BTHelper.InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange)),
            new IsInLos<EntityWrapper>(TargetKey, _losWidth),
        });
        ref Transition approachToStand = ref approach.AddTransition(stand);
        approachToStand.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BoolCondition(OnCooldownKey, true),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange),
        });

        // small hysteresis so retreat doesn't flip back the moment it crosses the line
        ref Transition retreatToStand = ref retreat.AddTransition(stand);
        retreatToStand.Condition = ConditionHelper.InvertLeaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange * 1.15f));

        ref Transition attackToRetreat = ref attack.AddTransition(retreat);
        attackToRetreat.Condition = ConditionHelper.Sequence(new Node[]
        {
            new BehaviorTreeSucceeded(),
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange),
        });
        ref Transition attackToApproach = ref attack.AddTransition(approach);
        attackToApproach.Condition = ConditionHelper.Tree(BTBuilderPool.Get().Composite(new Sequence())
            .Leaf(new BehaviorTreeSucceeded())
            .Composite(new Selector())
                .InvertLeaf(new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _maxRange))
                .InvertLeaf(new IsInLos<EntityWrapper>(TargetKey, _losWidth))
            .End()
            .End()
            .Build());
        ref Transition attackToStand = ref attack.AddTransition(stand);
        attackToStand.Condition = ConditionHelper.Leaf(new BehaviorTreeSucceeded());   // catch-all: wait out the cooldown

        ref Transition standToRetreat = ref stand.AddTransition(retreat);
        standToRetreat.Condition = ConditionHelper.Leaf(
            new IsInRange<EntityWrapper, EntityWrapper>(TargetKey, Entity, _minRange));
        ref Transition standToAttack = ref stand.AddTransition(attack);
        standToAttack.Condition = ConditionHelper.Sequence(new Node[]
        {
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
