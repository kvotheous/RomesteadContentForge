# Content Forge — Advanced

_What this is: the power-user doc — C# controllers, addon DLLs, vanilla-art extraction, FMOD bank authoring.
What this isn't: needed for normal packs (JSON-only authoring is all in [PACKS.md](PACKS.md)). Docs index:
[README.md](../README.md)._

## Contents

- [How creature AI works](#how-creature-ai-works-and-how-to-extend-it) — server/client split, swapping brains, presets
- [Custom controllers (C#)](#custom-controllers-c) — interactive-placeable behavior beyond the presets
- [Addon DLLs](#addon-dlls) — packaging + shipping a companion plugin
- [Browse all game assets visually](#browse-all-game-assets-visually) — `asset_catalog.py` → one searchable HTML with thumbnails
- [Generating a held-item sheet from one drawing](#generating-a-held-item-sheet-from-one-drawing) — `make_held_sheet.py`
- [Art extraction & sheet discovery](#art-extraction--sheet-discovery) — `xnb2png.ps1` templates + finding sheet names
- [FMOD banks](#fmod-banks) — authoring new audio in FMOD Studio
- [How the mod loads things](#how-the-mod-loads-things)

---

## How creature AI works (and how to extend it)

The engine splits every creature in two: a **server controller** (decisions: targeting, movement, attacks —
runs on the host only) and a **client controller** (presentation: which spac animation plays — runs on every
client). They talk through two generic channels: a replicated **state int** (`UpdateEntityState`) and an
**animation byte** message. Spawn copies whatever controller instances sit on the creature's PROTOTYPE —
which is why Content Forge can swap them per creature:

- `creatures[].controller` swaps the SERVER brain — any vanilla AI by name, one of the eight built-in
  presets (forge_guard / forge_coward / forge_ranged / forge_patrol / forge_pack / forge_summoner /
  forge_healer / forge_turret), or a class your addon DLL registered (same registry
  as placeable controllers, parameterless ctor required). The base creature's `.cdx` params are copied
  automatically, plus the params of the AI's own home prototype (so an archer brain brings its arrow spell),
  plus your `controllerParams` on top.
- `creatures[].clientController` swaps the CLIENT presentation — `"forge_creature_anim"` builds the
  animation state machine from data (`spac` + `anim-map`), removing the "same family only" rule.
- Addon DLL custom AI: subclass `ServerBehaviorMachineController`, override `CreateStateMachine()` (the
  game's behavior-tree pieces — wander/chase/flee/cast — are public), register via the controller map.
  CreatureAi.cs + CreatureAi2.cs are the worked examples: eight complete presets built exactly this way.

Save/co-op: saves store only the creature's BaseId (never controller types); the AI runs host-only;
clients need the pack for the body. Removing the mod = the creature is absent from old saves (the same
accepted profile as all custom creatures).

## Custom controllers (C#)

**Advanced — most packs never need this.** Reach for it only when the
[interactive-placeable presets](PACKS.md#interactive-placeables-controller-presets) can't express your idea
(a loot chest, a wave trigger, a puzzle lock…). You write a tiny **companion BepInEx plugin** containing a
CLIENT + SERVER controller class pair, register the pair under a name, and any pack can then use that name in
its `placeables[].controller` — exactly how the built-in presets themselves are wired.

*Why C# and not Lua?* This API is Content Forge's scripting layer: controllers run **server-authoritatively**
(effects replicate correctly in co-op), are **typed and compile-checked**, and ride the game's own stable
controller extension point. The game does ship a Lua layer, but it is client-only, unsandboxed (a pack `.lua`
would be arbitrary code execution), and abandoned in place by the developers — see `RESEARCH.md` §9.5.

**The class pair.** The split mirrors vanilla (GateController vs ServerGateController): **server types carry
the gameplay effects** (heal, give, state, teleport — replicated by the game), **client types are presentation
only** (tooltip, floating text, sprite animation). No "am I the server?" checks ever needed. Both classes need
a **parameterless constructor**.

- **Client side:** inherit Content Forge's public `RomesteadContentForge.ForgeClientController` — you get the
  `tooltip`/`message` params, the custom-sprite animation (its `Update` drives it — call `base.Update` if you
  override) and a `ParseColor` helper for free. Overridables: `GetToolTip`, `OnInteract` (keep it cosmetic),
  `Update`, `EntityInitialize`.
- **Server side:** inherit the game's `Shared.Entity.Base.AbstractController`. Read your params in
  `EntityInitialize` (`Parameters.GetString/GetFloat/GetInt(key, default)` — the construction's
  `controllerParams` are merged in before it runs), do the work in `OnInteract(EntityWrapper otherEntity)`
  (the interacting player's entity), tick in `Update(GameTime)`; `State`/`OnServerSetState` +
  `EntityServerManager.UpdateEntityState` give you replicated, save-persisted state (see the lever).

**Register it.** The registry (`InteractiveFeature.RegisterController(name, clientType, serverType)`) is
`internal`, so call it via reflection from your plugin's `Awake()` (any time before a world loads works —
the types are (re)injected into the game's controller maps at every load):

```csharp
[BepInPlugin("com.you.mymod", "MyMod", "1.0.0")]
[BepInDependency("RomesteadContentForge")]   // Content Forge's plugin GUID — loads it first
public class MyMod : BaseUnityPlugin
{
    void Awake()
    {
        var forge = HarmonyLib.AccessTools.TypeByName("RomesteadContentForge.InteractiveFeature");
        HarmonyLib.AccessTools.Method(forge, "RegisterController")
            .Invoke(null, new object[] { "mymod_chest", typeof(MyChestClient), typeof(MyChestServer) });
    }
}

public sealed class MyChestClient : RomesteadContentForge.ForgeClientController { }   // tooltip/message free

// A working "once per player per game day" loot chest:
public sealed class MyChestServer : Shared.Entity.Base.AbstractController
{
    string _item;
    // gameDay -> players already served. In-memory: resets on world reload — fine for a daily chest.
    // Need it persistent? Stamp a player Flag instead (the game's flag system survives saves).
    readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>> _served = new();

    public override void EntityInitialize() => _item = Parameters.GetString("loot-item", "");

    public override void OnInteract(Shared.Entity.EntityWrapper otherEntity)
    {
        // resolve the interacting player (otherEntity is their entity)
        if (!CandideServer.ServerManagers.PlayerServerManager
                .TryGetPlayerFromEntity(otherEntity, out var player)) return;

        int day = CandideServer.ServerSystems.TimeServerSystem.Day;          // current game day
        if (!_served.TryGetValue(day, out var set)) _served[day] = set = new();
        if (!set.Add(player.Id)) return;                                     // already served today

        // grant the item into the player's bag (server-side, replicated)
        System.Guid invId = player.CharacterModel.Character.CharacterInventoryModel.InventoryId;
        CandideServer.ServerManagers.SimpleInventoryServerManager
            .TryAddItemAmounts(invId, _item, 1, null, allowPartialStack: true, targetSlotIndex: null);

        // flip the chest "open" — State is replicated to clients AND persisted in the save (the lever pattern)
        CandideServer.ServerManagers.EntityServerManager.UpdateEntityState(Entity.Id, 1,
            updateSimulationState: true, CandideServer.SyncStrategies.SyncStrategy.SendToAll);
    }
}
```

**Building it:** target **net8.0**; reference **`Shared.dll`**, **`Romestead.dll`** and **`CandideServer.dll`**
from the game directory plus the **BepInEx.Core** and **HarmonyX** packages — same references as Content Forge
itself. (`TimeServerSystem`/`PlayerServerManager`/`SimpleInventoryServerManager`/`EntityServerManager` live in
`CandideServer`; `AbstractController`/`EntityWrapper` in `Shared`.)

**Use it from a pack.** The pack JSON then simply says `"controller": "mymod_chest",
"controllerParams": { "loot-item": "mymod:relic" }` (plus `cloneEntity` as usual). Names are normalized like
vanilla's (lowercased, a trailing `controller` stripped), so `MymodChestController` and `mymod_chest` are the
same key.

## Addon DLLs

A controller addon is a normal BepInEx plugin: `[BepInDependency("RomesteadContentForge")]` (the plugin GUID)
makes BepInEx load Content Forge first, so the registry types exist when your `Awake()` runs. Ship your addon
DLL next to `RomesteadContentForge.dll` in `BepInEx\plugins\`; players need **both DLLs plus the pack** that
uses your controller name. Everything else about distribution is the normal pack story
([GETTING_STARTED.md §8](GETTING_STARTED.md#8-ship-it)) — just list the addon as one more requirement.

## Browse all game assets visually

`python tools/asset_catalog.py` → `asset-catalog.html` — a single self-contained page (open in any
browser, no server) with every sprite sheet in the game decoded to a thumbnail (grouped by category:
character, creatures, constructions, buildings, boss, flora, ...), plus searchable tabs for every
reference id list (items, icons, creatures, entities, decorations, drop tables, auras, displays,
citizens, raids, music). Instant substring filter, click any name/id to copy it, click a thumbnail to
zoom the whole sheet. Pass a different Content dir as the first argument if Romestead isn't in the
default Steam location. Icons have no previews — they're cells in runtime sheets whose id→cell mapping
isn't in static files (use `tools/xnb2png.ps1` on `media/interface/icons/**` to eyeball the raw sheets).

## Art extraction & sheet discovery

### Extracting vanilla art (`tools/xnb2png.ps1`)

Custom-art fields (held looks, trails, vfx, palettes, rig repaints) are **index-locked to vanilla sheets** —
your PNG must match a vanilla sheet's grid, so you trace over an extracted template instead of drawing blind.
The included `tools\xnb2png.ps1` dumps any uncompressed Color sprite sheet in a game `.xnb` to PNG **and prints
each one's size**:

```
tools\xnb2png.ps1 -In "<game>\Content\media\sprites\character\tools" -Out ".\templates" -Recurse
```

Point `-In` at the folder for what you're repainting:

| You're repainting | Extract from `<game>\Content\…` | Used by |
|---|---|---|
| Held weapon looks | `media\sprites\character\tools` | [`display.sheet`](PACKS.md#display-equippable-display--custom-held-look) (every weapon sheet + its size) |
| Swing trails | `media\particles\vfx` **and** `media\combat` | [`trailSheet`](PACKS.md#weapon) (sword `slash.png`/`slash2.png`, `dagger.png`, `spear2.png`, `sledge.png`) |
| Hit/impact VFX | `media\particles\vfx` | [`vfx.sheet`](PACKS.md#vfx-custom-sprite-animation-effect) (cells usually 48×48) |
| Dye-ramp palettes | `media\sprites\character\palettes` | [`palettes.sheet`](PACKS.md#palette-custom-dye-ramp) (`weapon.png` = 16×10) |
| Character parts / hair / rig | `media\sprites\character` | [`playerSkins`](PACKS.md#playerskins-top-level-playerskins--the-players-own-look) repaints |
| Dialogue-NPC body | `media\sprites\creatures\hostiles\goblin_bandit2` | [NPC `sheets`](PACKS.md#npc-top-level-npcs--a-talking-character-in-your-maps) (`no_weapons`, 48×48) |

> **Copyright:** the extracted templates are vanilla game art — keep them local for tracing; **don't ship them**
> in a published pack. Distribute your *own* repainted sheets (and the tool), not the originals.

### Discovering sheet names

Multi-sheet repaint fields (`creatures.sheets`, `spacs.sheets`, `playerSkins.rig.sheets`) key on **vanilla sheet
basenames** you can't see from the outside — so the mod logs them for you. The pattern everywhere: **configure a
guess (or a placeholder), launch, read the log** — it prints the real sheet names, texture sizes and frame
sizes, plus your unmatched keys. Then repaint on the same grid.

- **Player rig** (`playerSkins.rig`): the body sheets belong to the `player_new_animations` spac, not the
  regular character-part files. Configure a placeholder rig (`"rig": {"sheets": {"placeholder": "x.png"}}`),
  launch, stand in-world — the log prints every base sheet's name, texture size and frame size, plus your
  unmatched keys.
- **Creatures** (`creatures.sheets`): the log prints the base's sheet names + sizes on the creature's first
  appearance ([PACKS.md — Creature](PACKS.md#creature-top-level-creatures--a-custom-creature)).
- **Spacs** (`spacs.sheets`): a wrong `sheets` key once → the log prints the real sheet names + frame sizes
  ([PACKS.md — Spacs](PACKS.md#spacs-top-level-spacs--clone--reskinretime-a-vanilla-sprite-animation)).
- **Weapon trails** (`trailSheet`): swing once — the log prints the exact vanilla sheet name + frame size it
  cloned ([PACKS.md — Display](PACKS.md#display-equippable-display--custom-held-look)).

## Generating a held-item sheet from one drawing

The hardest part of a custom weapon is the rig-aligned held sheet (23×5 frames of 48×48 for
main-hand). `tools/make_held_sheet.py` generates it from a single PNG: draw your weapon **pointing
straight up, grip (pommel end) at bottom-center**, transparent background, then:

```
python tools/make_held_sheet.py mysword.png --template Sword
```

It calibrates per-frame angle/position from the vanilla sheet (pixel analysis per cell), scales your
art to the vanilla median weapon length, and writes `mysword_sheet.png` (point `display.sheet` at it)
plus `mysword_compare.png` — vanilla row over your row for every frame; **orange-ticked cells are
low-confidence** (vanilla motion-smear/foreshortened frames) — eyeball those. `--list-templates`
shows every shape (sword/dagger/spear/hammer/bow validated; dual-wield dagger cells handled);
`--grip x,y` if your grip isn't bottom-center; `--content <dir>` if Steam isn't in the default
location. Runtime ~15–40s per sheet. **Smear-cell modes (`--smear`).** Vanilla attack frames often contain a painted motion-smear arc rather
than a rigid weapon; the tool flags these and offers three treatments. `--smear straight` (default) draws
your rigid weapon at the recovered swing angle — always safe, but you lose the swoosh. `--smear keep`
copies the vanilla smear art verbatim — perfect if your weapon uses vanilla-ish metal colours.
`--smear transfer` keeps the vanilla smear's shape and transparency but remaps its palette to your
weapon's colours via a luminance-band lookup (dark template pixels become your dark tones, white streaks
your brightest highlight), so the swoosh reads as *your* weapon. Cells handled by keep/transfer are ticked
blue in the compare strip (orange = other warnings) — always eyeball them. Foreshortened frames are never
affected: their art is a real weapon pose, just compressed.

The held sheet is one of SIX weapon-feel knobs — see the checklist in [COOKBOOK.md](COOKBOOK.md#custom-sword-art-in-3-commands) (swing trail, hit VFX, sound, arc/combo, animation pacing). A bad orange frame is fixable by hand: the output is a plain PNG — repaint that one 48×48 cell in any pixel editor. The `tools/` folder ships inside the **Authoring zip** download (and in the repo).

## FMOD banks

The game plays sounds by FMOD **event** id (`event:/...`). Two levels:

- **Reuse a vanilla sound** — just reference its id, no bank needed: `"castSound": "event:/spells/cast_spell_cosmo"`.
- **Ship new audio** — build an FMOD **`.bank`** and drop it in `…\packs\banks\` (or the packs folder). It loads
  at startup and its events become usable as `event:/yourmod/...` anywhere a sound id is taken.

*(For simple one-shots there's a no-FMOD-Studio path — the
[`sounds` map](PACKS.md#sounds-top-level-sounds--drop-in-audio-files): drop a `.wav`/`.ogg` next to the pack.
Banks are for full music tracks, authored loops and parameters.)*

**⚠️ Version must match.** This game runs **FMOD 2.3.13**, so build banks with **FMOD Studio 2.3.x**. A bank from
a different major/minor fails to load (the log shows `Sound bank '…' load failed: ERR_VERSION`).

**Make a bank (FMOD Studio):**
1. Get **FMOD Studio** from fmod.com (free account; indie license is free) — version **2.3.x**.
2. **New Project.**
3. **Add audio** — drag a `.wav`/`.ogg` into the Audio Bin (or onto an event).
4. **New event** — Events browser → right-click → **New Event → 2D**; drag your audio onto its timeline. Put it
   under a folder named like your mod so its path is `event:/yourmod/zap`.
5. **Own bank** — switch the bottom-left to **Banks** view → **New Bank** `yourmod` → right-click the event →
   **Assign to Bank → yourmod**. *(Keep it separate — don't touch Master.)*
6. **Build** — `File → Build` (F7). Output in `<project>\Build\Desktop\`: **`yourmod.bank`** + **`yourmod.strings.bank`**.

**Install + use:**
7. Copy **both** files into `…\romestead\BepInEx\config\RomesteadContentForge\packs\banks\`.
8. Reference the event: `"castSound": "event:/yourmod/zap"`.

**Gotchas:** ship the **`.strings.bank`** too (it holds the event-path names — without it `event:/…` won't
resolve; the loader loads it first). The event path must match `castSound` exactly. Don't overwrite the game's
`Master.bank`. On success the log shows `Content Forge: loaded 1/1 custom sound bank(s)`.

## How the mod loads things

One paragraph: at game startup the mod reads every pack json, resolves dependencies, and registers content
through the game's own public `DataBase` add methods (a postfix on `SharedDataSetup.Setup`), injecting assets
(PNG/TMX/yarn/audio) through the matching subsystems; everything else (replication, saves, co-op handshake)
rides vanilla systems. The full architecture — feature by feature, with the Harmony patch map — is
**[DESIGN.md](DESIGN.md)**; per-feature engine findings are **[RESEARCH.md](RESEARCH.md)**.
