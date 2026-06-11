using Candide.Database;                                         // SpacDataBase (client spac registry)
using Candide.Entities;                                         // ClientEntityControllerLoader
using Candide.Entities.Animations;                              // EntitySpacAnimationStateMachine
using Candide.Graphics.VFX;                                     // VfxPlayer, CreatureDeathHandler, SpacRenderingArgs
using Candide.Sound;                                            // TileBasedFootstepHandler, EmitSoundOneShot ext
using CandideCreator.Shared.Helpers;                            // DirectionTo8IntDirection / DirectionToIntDirection (float ext)
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac
using CandideCreator.Shared.SpacSystem;                         // State, Transition, ISpacAnimationState
using HarmonyLib;
using Microsoft.Xna.Framework;                                  // GameTime
using Shared.Entity;                                            // EntitySystem, EntityWrapper, EntityKillInfo
using Shared.Entity.Base;                                       // AbstractController, OnKilledBehaviour
using Shared.Entity.Components;                                 // ControllerComponent
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// CLIENT half of the creature-AI wall: a GENERIC, DATA-DRIVEN client creature controller
// ("forge_creature_anim") so a custom creature can pair a CROSS-FAMILY server AI with working animations.
//
// THE WALL (research, rdll = Romestead.decompiled.cs):
//   Every vanilla client creature controller hardcodes three things (SkeletonWarriorController, rdll:1872):
//     1. the spac id        — "SpacDataBase.GetSpacOrCrash("skeleton_animations")" (rdll:1895). It lives in
//        client controller CODE, not in any entity component (the only param-driven one is the deer's
//        Parameters.GetString("spacId", "deer_female_animations"), rdll:177601).
//     2. the state machine  — new EntitySpacAnimationStateMachine(spac, State[], Transition[]) where every
//        Transition is "* -> <anim> when State == <int>" (rdll:1896-1916). The spac's animation NAMES are
//        enumerable at runtime: the machine ctor itself matches State.AnimationName against
//        spac.SpritesAnimations[i].AnimationName (cc AbstractSpacAnimationStateMachine ctor, cc:4500-4525).
//     3. the message maps   — OnServerSetState(int) just stores State (transitions react on the next
//        Update; looping states are always "IsDone", cc:4790 "_isDone = looping && !hasExitTime", so the
//        switch is immediate), while ReceiveEntityAnimationMessage(byte) explicitly SetState()s so
//        non-looping attacks RESTART (rdll:1977-2009: bytes 0/2/4/5 -> idle/attack/attack_windup/attack_swing).
//   A custom server AI that doesn't speak the base family's ints/bytes therefore freezes the client anims.
//
// HOW THE REST WORKS (all verified):
//   - walking: "currentState.TimeScale = Entity.DistanceWalked / Entity.Fdt / 63.3f" while walking +
//     "_animationStateMachine.CurrentDirection = Entity.Direction.DirectionTo8IntDirection()" every frame
//     (rdll:1934-1938). Divisors are per-creature px/sec constants (23-93 across controllers, rdll:632-1935).
//     SpacAnimationState.CurrentDirection mods by the animation's own direction count (cc:4781).
//   - death: client kill flow is SyncRemoveEntity -> Controller.OnKilled -> ForgetEntity (rdll:141948-141986),
//     so the controller must hand its machine to VfxPlayer.HandleCreatureDeath (rdll:127447) or the creature
//     just vanishes. CreatureDeathHandler (rdll:126843) plays the CURRENT machine state to completion when
//     extraDeathTime > 0 (magus: SetState("death") + 0.5f, rdll:715-717), or freezes the frame + red flash +
//     poof when extraDeathTime == 0 (deer, rdll:177686).
//   - server channels (cs2 = CandideServer.decompiled.cs): the BT leaf "SetEntityAnimation" sends BOTH
//     EntityServerManager.UpdateEntityState(state) AND EntityServerService.SendEntityAnimationMessage(same
//     value) (cs2:8345-8366) — so ONE anim-map usually covers both; hand-written vanilla AIs use distinct
//     bytes (skeleton 4/5), which `animmsg-map` overrides.
//   - params plumbing: the prototype controller's Parameters are COPIED per spawn ("AttachEntity(wrapper,
//     new Parameters(source...Parameters))", sh DeepCopyEntity, sh:12339-12341) and the server spawn model's
//     Parameters are merged before EntityInitialize (rdll SpawnEntity:149386 "AddOrOverwrite(model.Parameters)")
//     — so params set on the CLIENT prototype clone in RegisterCreatures reach every spawned instance.
//
// SCOPE (honest): idle/walk/attack/any-mapped anims + movement speed/direction + death anim/poof + optional
// tile-based footsteps + optional death sfx. NOT replicated from vanilla controllers: per-creature particle
// vfx (e.g. skeleton windup shadow_cloud), gib sheets (DeathHelper.DeathPartsArgs), bespoke sounds beyond
// death/footsteps, palette variants. Those stay family-specific; the creature still animates correctly.
//
// CONTROLLER PARAMETERS (ride creatures[].controllerParams; merged into BOTH side's controllers):
//   spac            REQUIRED — the spac id of the BASE family ("skeleton_animations", ...). The id is
//                   hardcoded in client code (see WALL #1), so authors supply it; the first init logs the
//                   spac's animation names + direction count (discovery log, like Rigs).
//   anim-map        "<int>=<anim>[:loop|:once][:<speed>]" comma list, e.g. "0=idle:loop,1=walk:loop,2=attack".
//                   Default: "0=idle,1=walk,2=attack" filtered to anims the spac actually has.
//                   loop default heuristic: idle/walk/run/fly/move/swim/channel/alert_loop loop, others once.
//   animmsg-map     same format for the animation BYTE channel; default = anim-map.
//   anim-default    initial/fallback anim name (default "idle" if present, else first mapped).
//   anim-walk       which anim is "walking" for speed/footsteps (default "walk" if present).
//   walk-px-per-sec movement speed at which the walk anim plays at 1x (default 60; skeleton=63.3, deer=23).
//   footsteps       tile-based footstep cadence (skeleton=24); 0/unset = no footsteps.
//   anim-death      death anim name (default "death" if the spac has one; "" disables -> freeze+flash+poof).
//   death-time      extra seconds the death anim may run (default 0.5, the magus value).
//   death-size      death poof size: none/tiny/small/medium/large (default medium).
//   death-sfx       FMOD event played on death (skips the default kill sound), e.g. "event:/creature/skeletons/skeleton_die".
// ---------------------------------------------------------------------------------------------------------

internal static class CreatureAnimFeature
{
    public const string ControllerName = "forge_creature_anim";

    // Insert our type into the CLIENT controller map (same idempotent indexer pattern as Interactive.cs;
    // Initialize() only scans the game's own assembly, rdll:152079-152100). Server map: untouched — this
    // feature is client-only by design (the server half is the AI presets in CreatureAi.cs).
    internal static void InjectClientType()
    {
        var map = ClientEntityControllerLoader.ControllerMap;
        if (map != null) map[ControllerName] = typeof(ForgeCreatureAnimController);
    }

    // CLIENT pass of ContentLoader.RegisterCreatures: swap the creature clone's client controller — the
    // exact mirror of the server AI swap (Plugin.cs:1553-1567). Takes plain values (not CreatureDef) so this
    // file compiles before the CreatureDef.ClientController field is added.
    internal static void ApplyClientController(EntitySystem clone, string defId, string clientController,
        Dictionary<string, string> controllerParams)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(clientController)) return;
            ClientEntityControllerLoader.Initialize();   // no-op when already built (rdll:152081)
            InjectClientType();                          // defensive re-inject (postfix normally already did)

            string name = clientController.ToLowerInvariant().Replace("controller", "").Trim();
            AbstractController cc = ClientEntityControllerLoader.CreateController(name, clone.General[0].Wrapper);
            if (cc == null)
            {
                Plugin.Log.LogWarning($"creature '{defId}': client controller '{clientController}' unknown — keeping the base visuals.");
                return;
            }
            // Base controller params first (vanilla client controllers ship .cdx params the spawn relies
            // on), then the pack's overrides — same merge order as the server pass.
            var baseCtrl = clone.ControllerComponent[0].Controller;
            if (baseCtrl != null) cc.Parameters.AddWithoutOverwrite(baseCtrl.Parameters);
            if (controllerParams != null)
                foreach (var kv in controllerParams) cc.Parameters.Set(kv.Key, kv.Value);

            // Drop any client Ai: per-spawn DeepCopyEntity casts the controller to EntityControllerBase
            // whenever Ai != null (sh:12342-12346) and ours isn't one. Current-gen vanilla creature client
            // controllers are plain AbstractControllers without Ai, so this only fires on odd bases.
            if (clone.ControllerComponent[0].Ai != null)
                Plugin.Log.LogInfo($"creature '{defId}': base client Ai dropped (forge_creature_anim replaces the client controller).");
            clone.ControllerComponent[0] = new ControllerComponent { Controller = cc };
            Plugin.Log.LogInfo($"creature '{defId}': client controller -> '{name}'.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"creature '{defId}' client controller swap failed: {e.Message}"); }
    }
}

// Same chokepoint as InteractiveControllerMapClientPatch: insert our type right after the game builds the
// CLIENT controller map (RomesteadGame.Initialize). Add `h.PatchAll(typeof(CreatureAnimClientMapPatch));`
// next to the other map patches in Plugin.Load.
internal static class CreatureAnimClientMapPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClientEntityControllerLoader), nameof(ClientEntityControllerLoader.Initialize))]
    private static void Postfix()
    {
        try { CreatureAnimFeature.InjectClientType(); }
        catch (Exception e) { Plugin.Log.LogWarning($"creature-anim client controller-map inject failed: {e.Message}"); }
    }
}

// The generic client creature controller. Lifecycle mirrors SkeletonWarriorController (rdll:1872-2009):
// EntityInitialize builds the machine, Update drives walk speed/direction/footsteps, BeforeRender registers
// the spac draw, OnServerSetState/ReceiveEntityAnimationMessage map the two server channels, OnKilled hands
// the machine to the death handler. Every entry point is defensive: a bad param never crashes the client —
// worst case the creature renders frozen/invisible with a logged reason.
public sealed class ForgeCreatureAnimController : AbstractController
{
    private EntitySpacAnimationStateMachine _machine;
    private Dictionary<int, string> _stateMap;     // state int  -> anim name (transition channel)
    private Dictionary<int, string> _msgMap;       // anim byte  -> anim name (explicit SetState channel)
    private string _defaultAnim;
    private string _walkAnim;
    private float _walkPxPerSec;
    private TileBasedFootstepHandler _footsteps;
    private bool _useFootsteps;
    private string _deathAnim;
    private float _deathTime;
    private CreatureDeathHandler.Size _deathSize;
    private string _deathSfx;
    private int _spacDirections;
    private static readonly HashSet<string> _loggedSpacs = new();   // discovery log once per spac
    private static readonly HashSet<string> _warnedOnce = new();

    // Anim names that default to looping when no :loop/:once flag is given (vanilla conventions).
    private static readonly HashSet<string> LoopingByDefault = new(StringComparer.OrdinalIgnoreCase)
    { "idle", "walk", "run", "fly", "move", "swim", "channel", "alert_loop" };

    public override void EntityInitialize()
    {
        try
        {
            string spacId = Parameters.GetString("spac", Parameters.GetString("spacId", null));
            if (string.IsNullOrWhiteSpace(spacId))
            {
                WarnOnce("nospac:" + Entity?.BaseGuid,
                    "forge_creature_anim: missing required param 'spac' (the base creature's spac id, e.g. " +
                    "\"skeleton_animations\") — the creature will be invisible.");
                return;
            }
            Spac spac = SpacDataBase.GetSpacOrNull(spacId);
            if (spac?.SpritesAnimations == null)
            {
                WarnOnce("badspac:" + spacId, $"forge_creature_anim: spac '{spacId}' not found — the creature will be invisible.");
                return;
            }
            _spacDirections = spac.Directions;

            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sda in spac.SpritesAnimations) available.Add(sda.AnimationName);
            if (_loggedSpacs.Add(spacId))   // discovery log: exactly what anim-map can reference
                Plugin.Log.LogInfo($"forge_creature_anim: spac '{spacId}' ({spac.Directions} directions) — animations: "
                    + string.Join(", ", available));

            // --- channel maps ---------------------------------------------------------------------------
            _stateMap = ParseAnimMap(Parameters.GetString("anim-map", null), spacId, out var flags);
            if (_stateMap.Count == 0)   // default melee vocabulary, filtered to what the spac has
            {
                _stateMap = new Dictionary<int, string>();
                if (available.Contains("idle")) _stateMap[0] = "idle";
                if (available.Contains("walk")) _stateMap[1] = "walk";
                if (available.Contains("attack")) _stateMap[2] = "attack";
            }
            _msgMap = ParseAnimMap(Parameters.GetString("animmsg-map", null), spacId, out var msgFlags);
            if (_msgMap.Count == 0) _msgMap = _stateMap;   // BT SetEntityAnimation sends the same int on both
            foreach (var kv in msgFlags) flags[kv.Key] = kv.Value;   // byte-channel anims may add loop/speed flags

            _defaultAnim = Parameters.GetString("anim-default", null);
            if (string.IsNullOrWhiteSpace(_defaultAnim))
                _defaultAnim = available.Contains("idle") ? "idle" : FirstMapped(_stateMap);
            _walkAnim = Parameters.GetString("anim-walk", null);
            if (string.IsNullOrWhiteSpace(_walkAnim)) _walkAnim = available.Contains("walk") ? "walk" : null;
            _walkPxPerSec = Parameters.GetFloat("walk-px-per-sec", 60f);
            if (_walkPxPerSec <= 0f) _walkPxPerSec = 60f;

            float footsteps = Parameters.GetFloat("footsteps", 0f);
            _useFootsteps = footsteps > 0f;
            if (_useFootsteps) _footsteps = new TileBasedFootstepHandler(footsteps);

            _deathAnim = Parameters.GetString("anim-death", available.Contains("death") ? "death" : "");
            if (!string.IsNullOrWhiteSpace(_deathAnim) && !available.Contains(_deathAnim))
            {
                WarnOnce("death:" + spacId + _deathAnim, $"forge_creature_anim: spac '{spacId}' has no '{_deathAnim}' animation — using freeze+poof death.");
                _deathAnim = "";
            }
            _deathTime = Parameters.GetFloat("death-time", 0.5f);
            _deathSfx = Parameters.GetString("death-sfx", "");
            _deathSize = CreatureDeathHandler.Size.Medium;
            string size = Parameters.GetString("death-size", null);
            if (size != null && Enum.TryParse(size, ignoreCase: true, out CreatureDeathHandler.Size parsed)) _deathSize = parsed;

            // --- build the machine (vanilla pattern, generically) ---------------------------------------
            var stateDefs = new List<CandideCreator.Shared.SpacSystem.State>();
            var transitions = new List<Transition>();
            var named = new HashSet<string>(StringComparer.Ordinal);
            void AddStateDef(string anim)
            {
                if (!named.Add(anim)) return;
                if (!available.Contains(anim))
                {
                    WarnOnce("anim:" + spacId + anim, $"forge_creature_anim: spac '{spacId}' has no animation '{anim}' "
                        + "(see the animation list logged above) — that mapping is ignored.");
                    return;
                }
                (bool loop, float speed) = flags.TryGetValue(anim, out var f)
                    ? f : (LoopingByDefault.Contains(anim), 1f);
                stateDefs.Add(new CandideCreator.Shared.SpacSystem.State(anim, looping: loop, null, speed));
            }
            foreach (var kv in _stateMap) AddStateDef(kv.Value);
            foreach (var kv in _msgMap) AddStateDef(kv.Value);
            if (_defaultAnim != null) AddStateDef(_defaultAnim);
            if (!string.IsNullOrWhiteSpace(_deathAnim))
            {
                if (!named.Contains(_deathAnim))
                {
                    named.Add(_deathAnim);
                    stateDefs.Add(new CandideCreator.Shared.SpacSystem.State(_deathAnim, looping: false, null, 1f));
                }
            }
            // "* -> anim when State == int" — exactly the vanilla transition shape (rdll:1908-1915). Looping
            // states are permanently IsDone, so leaving idle/walk is immediate; non-looping attacks finish first.
            foreach (var kv in _stateMap)
            {
                int k = kv.Key;
                string anim = kv.Value;
                if (named.Contains(anim) && available.Contains(anim))
                    transitions.Add(new Transition("*", anim, () => State == k));
            }

            if (stateDefs.Count == 0)
            {
                WarnOnce("empty:" + spacId, $"forge_creature_anim: no usable animations for spac '{spacId}' — the creature will be invisible.");
                return;
            }
            _machine = new EntitySpacAnimationStateMachine(spac, stateDefs, transitions);
            _machine.Args = SpacRenderingArgs.Default;
            _machine.Args.SetDefaultEntityOffset(Entity);
            // Visual tuning knobs (from-scratch art rarely matches the vanilla cell-margin conventions):
            //   sprite-scale    — multiplies the quad size (default 1)
            //   sprite-offset-y — px added to the vertical anchor (positive = sprite drawn HIGHER; the
            //                     default anchor is collision-box-height/2 via SetDefaultEntityOffset)
            float sScale = Parameters.GetFloat("sprite-scale", 1f);
            if (sScale > 0f && sScale != 1f) _machine.Args.Scale = sScale;
            float sOffY = Parameters.GetFloat("sprite-offset-y", 0f);
            if (sOffY != 0f) _machine.Args.Offset += new Microsoft.Xna.Framework.Vector3(0f, sOffY, 0f);
            Entity.UsesSpac = true;

            // Initial state: spawn sets controller.State from the server model BEFORE EntityInitialize
            // (rdll SpawnEntity:149405-149412), so honor it; else the default.
            string initial = (_stateMap.TryGetValue(State, out string mapped) && available.Contains(mapped)) ? mapped : _defaultAnim;
            if (initial != null && available.Contains(initial)) _machine.SetState(initial);
        }
        catch (Exception e)
        {
            _machine = null;
            Plugin.Log.LogWarning($"forge_creature_anim init failed: {e.Message}");
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (_machine == null) return;
        try
        {
            ISpacAnimationState current = _machine.CurrentState;
            if (_walkAnim != null && current.StateName == _walkAnim)
            {
                // walk anim speed follows actual movement (skeleton: rdll:1934-1936)
                current.TimeScale = Entity.DistanceWalked / Entity.Fdt / _walkPxPerSec;
                if (_useFootsteps) _footsteps.PlayFootstepSound(Entity, isWalking: true, 1f, 0.4f, 0.7f);
            }
            // 8-dir spacs take the 8-int direction; 4-dir spacs the 4-int (wolf pattern, rdll:162767);
            // SpacAnimationState mods by its own direction count anyway (cc:4781) so this never overflows.
            _machine.CurrentDirection = _spacDirections >= 8
                ? Entity.Direction.DirectionTo8IntDirection()
                : (_spacDirections >= 4 ? Entity.Direction.DirectionToIntDirection() : 0);
            _machine.Update(Entity, Entity.Fdt);
        }
        catch (Exception e) { WarnOnce("update", $"forge_creature_anim update failed: {e.Message}"); }
    }

    public override void BeforeRender()
    {
        _machine?.Draw(Entity);
    }

    // State-int channel: store State; the machine's transitions react on the next Update (the same division
    // of labor as vanilla — explicit restarts come through the byte channel below).
    public override void OnServerSetState(int state)
    {
        base.OnServerSetState(state);
    }

    // Anim-byte channel: explicit SetState so non-looping anims RESTART even when the byte repeats
    // (vanilla skeleton: rdll:1989-2009). Also aligns State so transitions don't immediately yank us out.
    public override void ReceiveEntityAnimationMessage(byte animation)
    {
        if (_machine != null && _msgMap != null && _msgMap.TryGetValue(animation, out string anim))
        {
            _machine.SetState(anim);
            State = animation;
        }
        base.ReceiveEntityAnimationMessage(animation);
    }

    // Client kill flow removes the entity right after OnKilled (rdll:141970-141986); the death handler keeps
    // drawing our machine: death anim to completion when we have one (magus pattern), else freeze+flash+poof
    // (deer pattern, extraDeathTime 0 -> FreezeAnimation, rdll:126896-126900).
    public override OnKilledBehaviour OnKilled(EntityKillInfo info)
    {
        try
        {
            if (_machine != null && !string.IsNullOrWhiteSpace(_deathAnim))
            {
                _machine.SetState(_deathAnim);
                VfxPlayer.HandleCreatureDeath(Entity, info.DamageResult, _machine, null, _deathSize, null, 0, _deathTime <= 0f ? 0.5f : _deathTime);
            }
            else
            {
                VfxPlayer.HandleCreatureDeath(Entity, info.DamageResult, _machine, null, _deathSize);
            }
            if (!string.IsNullOrEmpty(_deathSfx))
            {
                Entity.EmitSoundOneShot(_deathSfx);
                return OnKilledBehaviour.PlayedCustomSound;
            }
        }
        catch (Exception e) { WarnOnce("killed", $"forge_creature_anim death failed: {e.Message}"); }
        return default;
    }

    // ------------------------------------------------ helpers ------------------------------------------------

    // "0=idle:loop, 1=walk, 2=attack:once:1.1" -> int->name map + per-anim (loop, speed) flags.
    private static Dictionary<int, string> ParseAnimMap(string raw, string spacId, out Dictionary<string, (bool Loop, float Speed)> flags)
    {
        var map = new Dictionary<int, string>();
        flags = new Dictionary<string, (bool, float)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return map;
        foreach (string entry in raw.Split(','))
        {
            string e = entry.Trim();
            if (e.Length == 0) continue;
            int eq = e.IndexOf('=');
            if (eq <= 0 || !int.TryParse(e.Substring(0, eq).Trim(), out int key))
            {
                WarnOnce("parse:" + spacId + e, $"forge_creature_anim: bad anim-map entry '{e}' (want \"<int>=<anim>[:loop|:once][:<speed>]\").");
                continue;
            }
            string[] parts = e.Substring(eq + 1).Split(':');
            string anim = parts[0].Trim();
            if (anim.Length == 0) continue;
            bool loop = LoopingByDefault.Contains(anim);
            float speed = 1f;
            for (int i = 1; i < parts.Length; i++)
            {
                string flag = parts[i].Trim();
                if (flag.Equals("loop", StringComparison.OrdinalIgnoreCase)) loop = true;
                else if (flag.Equals("once", StringComparison.OrdinalIgnoreCase)) loop = false;
                else if (float.TryParse(flag, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f) speed = s;
            }
            map[key] = anim;
            flags[anim] = (loop, speed);
        }
        return map;
    }

    private static string FirstMapped(Dictionary<int, string> map)
    {
        foreach (var kv in map) return kv.Value;
        return null;
    }

    private static void WarnOnce(string key, string message)
    {
        if (_warnedOnce.Add(key)) Plugin.Log.LogWarning(message);
    }
}
