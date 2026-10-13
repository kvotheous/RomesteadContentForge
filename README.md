<div align="center">

# ⚒️ Romestead Content Forge

**Turn Romestead into a moddable platform — plain JSON content packs, no compiling, no game files touched.**

[![Version](https://img.shields.io/badge/version-1.1.0-d89a4e?style=flat-square)](CHANGELOG.md)
[![License: MIT](https://img.shields.io/badge/license-MIT-2ea44f?style=flat-square)](LICENSE)
[![BepInEx 6](https://img.shields.io/badge/BepInEx-6%20(CoreCLR)-5c2d91?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![Game](https://img.shields.io/badge/game-Romestead-b03a2e?style=flat-square)](https://store.steampowered.com/)

**[🚀 Get Started](docs/GETTING_STARTED.md)** · **[📜 Pack Reference](docs/PACKS.md)** · **[🍳 Cookbook](docs/COOKBOOK.md)** · **[⬇️ Releases](../../releases)**

</div>

---

**Content Forge** is a BepInEx mod for Romestead that turns the game into a moddable platform: authors write
**content packs** — plain JSON files (plus PNG / TMX / WAV / `.yarn` assets) dropped into
`BepInEx\config\RomesteadContentForge\packs\` — and the mod injects their items, weapons, spells, summons,
creatures, dungeons, NPCs, quests, classes, placeables, events and more into the live game. Ready-made AI and
behaviors are built in — C# is optional, for when you want to go further. No compiling, no game files touched;
works in single-player, co-op and on **dedicated servers** (server-authoritative, save-safe uninstall) — see
[Dedicated servers](nexus/README_NEXUS.md#dedicated-servers).

## 🛠️ Easiest way to start: the Forge Editor

Don't want to write JSON by hand? Download **`ForgeEditor-<version>.zip`**, unzip, and run **`ForgeEditor.exe`**
(no Python, no install):

- **Every game value is built in** — items, creatures, icons, sounds, effects and more are picked from dropdowns,
  so you never guess an id.
- **Forms for every field**, with PNG previews and per-language text.
- **Built-in dialogue editor** — write NPC conversations (Yarn) with syntax highlighting, a node outline,
  error checks, and a side-by-side translation grid that tracks how much is translated.
- **One-click validation** catches mistakes before you launch the game.
- It saves the same `.json` packs the mod reads, so you can switch between the editor and hand-editing anytime.

More: [editor/README.md](editor/README.md).

## 🧩 More than an injector: built-in building blocks

It's not just a content injector: Content Forge ships ready-made systems your packs can use by name.

- **Creature AI**: **8 ready-made AI brains**: guard, coward, ranged caster, patroller, pack hunter, summoner, healer, turret. Give any creature one by name.
- **Interactive objects**: **ready-made behaviors** for placeables: crafting stations, portals, healing shrines, levers, talking objects (dialogue), props and multi-part buildings, plus a data-driven animation controller for fully custom creatures.
- **World events**: **wave defense, boss rush, hunt contest, scheduled raids, team battles, king of the hill, races** and PvP duel arenas, all configured in JSON.
- **Dialogue commands**: NPC conversations can **give items, take payment and heal** (`ForgeGiveItem`, `ForgeTakeItem`, `ForgeHeal`).
- **C# when you want it**: addon DLLs can register their **own behaviors and AI** with one call, and any pack can then use them by name (see [ADVANCED.md](docs/ADVANCED.md)).
- **Worked examples**: **3 example packs** (a story world, a necromancer class, and a test pack covering every feature) plus a **cookbook** of copy-paste recipes.

## 📦 Downloads — which zip is which

The release ships as **two main zips** plus the optional editor and dedicated-server zips (Nexus Files tab / GitHub Releases):

| Zip | Contents | Who needs it |
|---|---|---|
| **`RomesteadContentForge-<version>.zip`** — *the mod* | `BepInEx\plugins\` DLLs (the mod + its YarnSpinner/CSV libraries). Extract over the game folder. | **Everyone** — players just installing packs, and creators. |
| **`RomesteadContentForge-Authoring-<version>.zip`** — *the creator kit* | All docs (this index, PACKS/COOKBOOK/GETTING_STARTED…), the **GUI editor's Python source** (the ready-to-run `ForgeEditor.exe` is the separate `ForgeEditor-<version>.zip`), `tools/` (art extraction, asset catalog, held-sheet generator), `examples/` (3 worked packs incl. the full test bench) and `reference/` (17 id catalogs). Unzip anywhere — nothing installs into the game. | **Pack creators.** Players don't need it. |

`ForgeEditor-<version>.zip` (just `ForgeEditor.exe` + license notices) is also offered as its own optional file for creators who only want the editor. Third-party licenses for everything the downloads bundle are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

`RomesteadContentForge-Server-<version>.zip` is the **dedicated-server build** (`RomesteadContentForge.Server.dll` + `start-modded-server.bat`) — only for people running a headless Romestead Dedicated Server; see [Dedicated servers](nexus/README_NEXUS.md#dedicated-servers).

## 🧭 Find what you need

| You want to… | Read |
|---|---|
| Make your first pack (install + 5-minute tutorial) | [GETTING_STARTED.md](docs/GETTING_STARTED.md) |
| Look up every field & option of the pack format | [PACKS.md](docs/PACKS.md) |
| Copy-paste a recipe for a goal ("a staff that casts fireballs", "a healing shrine"…) | [COOKBOOK.md](docs/COOKBOOK.md) |
| Go beyond JSON — C# controllers, addon DLLs, art extraction, FMOD banks | [ADVANCED.md](docs/ADVANCED.md) |
| Find valid ids (icons, items, creatures, auras, drop tables…) | [REFERENCE.md](docs/REFERENCE.md) + `reference/*.txt` |
| See what's tested and how to test | [TESTING.md](docs/TESTING.md) |
| Read the changelog | [CHANGELOG.md](CHANGELOG.md) |
| Use the GUI editor instead of hand-written JSON | [editor/README.md](editor/README.md) |
| Copy from worked examples | [examples/README.md](examples/README.md) |

## 🗺️ What packs can do (capability index → PACKS.md)

- ⚔️ **Items & Gear** — [items](docs/PACKS.md#item) (icons, stats, prices, light sources) · [equippables](docs/PACKS.md#equippable) (slots, stat bonuses) · [consumables](docs/PACKS.md#usable-item-usable--consumable--potion) · [recipes](docs/PACKS.md#recipe) · [loot drops](docs/PACKS.md#drop-mob--chest-loot) (tables **and** per-creature/wildlife drops)
- 🗡️ **Combat & Weapons** — [weapons](docs/PACKS.md#weapon) (melee/ranged, stun, swing arcs, combo steps) · [on-hit / on-use casts](docs/PACKS.md#cast-weapon-castspell--fire-a-spell-from-a-weapon) · [scrolls & wands](docs/PACKS.md#spelltome-equippable-spelltome--classic-scrollwand) · [shields](docs/PACKS.md#shield-equippable-shield) · [held looks & swing trails](docs/PACKS.md#display-equippable-display--custom-held-look) · [two-handed weapons](docs/COOKBOOK.md#a-two-handed-staff) (`twoHanded`) · [held-sheet generator](docs/ADVANCED.md#generating-a-held-item-sheet-from-one-drawing)
- ✨ **Magic & Spells** — [spells](docs/PACKS.md#spell) (projectiles, auras, chains) · [behaviors & casting requirements](docs/PACKS.md#spell-behaviors-spellsplus--casting-requirements) (AoE, explosion, heal, cone, line, multi-projectile; skill/favour/quest/class gates)
- 💀 **Summons** — [summon spells](docs/PACKS.md#summon-spell-summon--spawn-a-creature-that-fights-for-you) (count, caps, guardian/companion) · [custom creatures](docs/PACKS.md#creature-top-level-creatures--a-custom-creature) (clone + restat/recolor/repaint, swappable AI) · [8 built-in AI presets](docs/PACKS.md#built-in-ai-presets-no-addon-dll-needed) (guard, coward, ranged, patrol, pack, summoner, healer, turret) · [fully custom creatures from your own PNGs](docs/COOKBOOK.md#a-fully-custom-creature-from-one-png)
- 🗺️ **World, Maps & Dungeons** — [Tiled-authored dungeon rooms](docs/PACKS.md#map-top-level-maps--a-custom-dungeon-room) (entrances — incl. `retrofit` into existing saves — multi-room doors, vanilla-room replace) · [atmosphere](docs/PACKS.md#map-atmosphere-fields-on-maps-entries) (music/ambience/light) · [pre-placed doodads](docs/PACKS.md#pre-placed-doodads-props-in-your-maps) · [shops](docs/PACKS.md#shop-top-level-shops--sell-your-items-at-the-market) · [crops](docs/PACKS.md#crop-top-level-crops--plantable-crops)
- 🗣️ **NPCs, Dialogue & Citizens** — [Yarn dialogue NPCs](docs/PACKS.md#npc-top-level-npcs--a-talking-character-in-your-maps) (localized, give/take/heal commands) · [town citizens & overrides](docs/PACKS.md#citizens-top-level-citizens--citizenoverrides--town-job-workers)
- 🏛️ **Placeables & Buildings** — [placeables](docs/PACKS.md#placeables-top-level-placeables-aliases-furniture--structures): [basic (vanilla art)](docs/PACKS.md#basic-placeables-vanilla-art-via-spawnedid) · [custom-sprite](docs/PACKS.md#custom-sprite-placeables-your-own-art) · [composite multi-part](docs/PACKS.md#composite-placeables-multi-part-parts) (several PNG layers + per-part collision as ONE buildable) · [interactive presets](docs/PACKS.md#interactive-placeables-controller-presets) (shrine / lever / dialogue / prop / portal) · [custom crafting stations](docs/COOKBOOK.md#a-custom-crafting-station) (`station`) · [town buildings & work recipes](docs/PACKS.md#town-building-top-level-townbuildings--a-citizen-staffed-workshop) (citizen-staffed workshops) · [pre-placed map props](docs/PACKS.md#pre-placed-doodads-props-in-your-maps) · [your own C# controller](docs/ADVANCED.md#custom-controllers-c)
- 📜 **Quests & Progression** — [playable classes](docs/PACKS.md#class-top-level-classes--a-new-playable-profession) · [world quests](docs/PACKS.md#quest-top-level-quests--a-world-quest) · [skills](docs/PACKS.md#skill-top-level-skills--a-new-player-skill) · [favour-tree nodes](docs/PACKS.md#favour-top-level-favours--a-new-favour-tree-node) (vanilla tree or your pack's **own fullscreen favour screen** via `screen: true`)
- 🏟️ **PvP, Arenas & Events** — [duel arenas](docs/PACKS.md#arenas-top-level-arenas--duel-events-on-pvp-maps) (on `pvp: true` maps; summon kills credit the summoner) · [world events](docs/PACKS.md#events-top-level-events--server-side-world-events) (waves, boss rush, hunts, raids — plus team battles, king-of-the-hill, checkpoint races)
- 🎨 **Visuals, Audio & Animation** — [palettes](docs/PACKS.md#palette-custom-dye-ramp) · [vfx](docs/PACKS.md#vfx-custom-sprite-animation-effect) · [particles & emitters](docs/PACKS.md#particles--emitters-top-level-particles--emitters) · [spac remixes](docs/PACKS.md#spacs-top-level-spacs--clone--reskinretime-a-vanilla-sprite-animation) · [from-scratch animations](docs/PACKS.md#from-scratch-spacs-no-base--brand-new-animations-from-your-pngs) (brand-new spacs from your PNGs) · [player animation retiming](docs/PACKS.md#player-animations-top-level-playeranimations--retime-the-rig-visual) · [drop-in sounds](docs/PACKS.md#sounds-top-level-sounds--drop-in-audio-files) · [FMOD banks](docs/ADVANCED.md#fmod-banks)
- 🧝 **Player Looks & Rigs** — [playerSkins](docs/PACKS.md#playerskins-top-level-playerskins--the-players-own-look) (new hair, worn looks, reskins, body-rig repaint/tint)
- ⚒️ **Forge tab** — the Skills & Favours window gains a third "Forge" toggle: installed packs, pack skills, learned pack favours.
- 🌳 **Pack Favours screen** — packs with `screen: true` favours get their own fullscreen favour tree (vanilla look) behind a fourth "Pack Favours" toggle ([COOKBOOK example](docs/COOKBOOK.md#your-own-favour-tree-screen)).
- 📣 **Announcement banners** — events/arena/handshake/two-handed messages show as big red text top-center; style, color, position and duration configurable in `RomesteadContentForge.cfg` ([PACKS.md — Mod settings](docs/PACKS.md#mod-settings-romesteadcontentforgecfg)).
- 🤝 **Co-op & Safety** — [co-op rules & save safety](docs/PACKS.md#co-op--saves) · the join handshake reports pack mismatches ([PACKS.md — Co-op & saves](docs/PACKS.md#co-op--saves)) · uninstall-safe favours, crops, items
- 🛠️ **Tooling** — [validators](docs/PACKS.md#validate) (Python + .NET) · GUI editor ([editor/README.md](editor/README.md)) · `tools/xnb2png.ps1` art extractor ([ADVANCED.md](docs/ADVANCED.md#art-extraction--sheet-discovery)) · id catalogs (`reference/*.txt`) · [cookbook](docs/COOKBOOK.md)

- `tools/asset_catalog.py` — generates `asset-catalog.html`, a browsable visual catalog of every game sprite + all referenceable ids.
