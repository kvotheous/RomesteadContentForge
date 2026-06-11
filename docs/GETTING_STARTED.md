# Content Forge — Getting Started

_What this is: install + your first pack in 5 minutes, through to shipping. What this isn't: the field spec
([PACKS.md](PACKS.md)) or a recipe collection ([COOKBOOK.md](COOKBOOK.md)). Docs index: [README.md](../README.md)._

You write a small **JSON file** (a "content pack"), drop it in a folder, and your items/spells/summons/quests/
classes/placeables/creatures/dungeons are **in the game**. No compiling, no game files touched.

## 1. Install (once)
There are **two downloads**: the **mod zip** (`RomesteadContentForge-<version>.zip` — the `BepInEx\plugins\`
DLLs; everyone needs this) and the **Authoring zip** (`...-Authoring-<version>.zip` — docs, GUI editor,
tools, examples, id catalogs; only pack **creators** need it, and it unzips anywhere).

1. Everything is on **Nexus Mods**: download **Content Forge** (the mod zip), install the **Romestead BepInEx
   Mod Loader** (linked in the mod's Requirements section), then put the zip's `BepInEx` folder over the
   game's so `RomesteadContentForge.dll` lands in `BepInEx\plugins\`.
   Optional but handy for testing: the **Romestead Item Spawner** mod (same Requirements section) lets you
   spawn any item with F1.
2. Launch the game once. The mod creates `BepInEx\config\RomesteadContentForge\packs\` and writes a working
   **`sample.json`** there — that file *is* the tutorial; open it.

## 2. Your first pack (5 minutes)
Create `BepInEx\config\RomesteadContentForge\packs\mypack.json`:

```json
{
  "id": "mypack",
  "namespace": "my",
  "author": "you",
  "version": "1.0.0",

  "items": [
    { "id": "my:doomblade", "name": "Doomblade",
      "description": "It whispers.",
      "icon": "sword:iron", "tier": 6, "maxStack": 1,
      "equippable": { "equipmentType": "Weapon", "material": "Iron", "displayId": "cdd:iron_sword",
        "weapon": { "class": "Sword", "swingTimer": 0.5, "attackRange": 26,
          "minDamage": { "necro": 60 }, "maxDamage": { "necro": 90 } } } }
  ]
}
```

Launch → spawn `my:doomblade` (easiest with the **Romestead Item Spawner** mod, F1) → swing it. That's the loop:
**edit json → relaunch → test.**

No Item Spawner? Add a cheap recipe to the pack and **craft** the blade from the basic crafting menu instead:

```json
"recipes": [ { "id": "my:recipe_doomblade", "crafted": "my:doomblade",
               "ingredients": { "material:wood_stick": 1 } } ]
```

Rules that keep packs from colliding:
- Every **new** id starts with **your `namespace` + `:`** (`my:doomblade`). Ids you merely *reference*
  keep their vanilla prefixes - `icon: "sword:iron"` and `displayId: "cdd:iron_sword"` above point at
  existing game assets (full lists in `reference/*.txt`).
- One pack = one json (+ its png/tmx/wav/yarn files next to it - same folder). Multiple packs coexist; a pack can depend on
  another via `"dependencies": ["otherpack"]` (the other pack's `id`).

## 3. Two ways to author
- **GUI editor** — `ForgeEditor.exe` from the release download, or run it from source:
  `pip install -r editor/requirements.txt`, then `python editor/forge_editor.py`. Every feature is a form —
  sections grouped like PACKS.md, dropdowns listing real game ids (icons, creatures, sprite animations a.k.a. "spacs", sounds, favours…),
  per-language translation tables with a + language picker, a built-in **yarn dialogue editor** (syntax
  highlighting, auto `#line:` tagging, lint, and a side-by-side translation grid that writes the csv
  sidecars for you), and a Validate (F5) panel whose errors click through to the offending entry.
  Open your json, edit, save.
- **By hand** — any text editor + **`PACKS.md`** (the complete field reference) + **`COOKBOOK.md`** (copy-paste
  recipes by goal). Validate from a terminal: `python editor/validate.py mypack.json` — takes a single pack
  file **or** a whole packs folder (no install needed).

## 4. Tools you need (by what you're adding)

First: the `editor/`, `tools/`, `examples/` and `reference/` folders mentioned everywhere ship in the
**Authoring zip** (a separate file on the mod's Nexus Files tab) - the main download is just the DLL, so
grab the Authoring zip too if you're creating packs. Where a tool needs the game's **`Content`** folder,
that's inside the game install (Steam -> right-click Romestead -> Manage -> **Browse local files**).

Everything below is **free** unless noted; nothing needs an account or an SDK. One **Python 3.10+** install
covers the validator and all `tools/` scripts (they're standard-library only — no `pip install` needed);
if you only use **`ForgeEditor.exe`**, you don't even need Python.

| You're adding | You need | Notes |
|---|---|---|
| **Anything (the pack json)** | a text editor **or** `ForgeEditor.exe` | The exe is self-contained — no installs. Editor from source needs Python + `pip install -r editor/requirements.txt` (PySide6). |
| **Validation** | nothing extra | Editor: press **F5**. CLI: `python editor/validate.py mypack.json` (plain Python, no packages). |
| **Custom art** (icons, items, creatures, placeables) | any pixel-art editor that saves **PNG with transparency** | [Aseprite](https://www.aseprite.org) (paid), or free: [LibreSprite](https://libresprite.github.io), [GIMP](https://www.gimp.org), [Krita](https://krita.org), [Piskel](https://www.piskelapp.com). Icons are **18×18**; sheet sizes are listed per feature in PACKS.md. |
| **Seeing the vanilla art** (to match style / find sheet layouts) | `tools/xnb2png.ps1` · `tools/asset_catalog.py` | `xnb2png.ps1` (plain Windows PowerShell; if scripts are blocked run `powershell -ExecutionPolicy Bypass -File` + the path) extracts any game texture to PNG. `asset_catalog.py` builds a **browsable HTML of every game sprite** (2000+) in seconds. Both read the game's `Content\` folder directly. |
| **Held weapon art** (sword/spear in hand) | `tools/make_held_sheet.py` | One drawing of your weapon → the full rig-aligned held sheet. See [ADVANCED.md](ADVANCED.md#generating-a-held-item-sheet-from-one-drawing). |
| **Maps & dungeons** (`.tmx`) | **[Tiled](https://www.mapeditor.org)** (free map editor) | The game's tilesets are plain Tiled files — add `Content\tilesets\*.tsx` to your Tiled project by reference. Full how-to: [PACKS.md — Map](PACKS.md#map-top-level-maps--a-custom-dungeon-room). |
| **NPC dialogue** (`.yarn`) | any text editor **or** the editor's built-in **yarn editor** | Yarn is plain text. The built-in editor adds syntax highlighting, auto `#line:` tags, lint, and writes the translation `.csv` sidecars for you. |
| **Sounds & music** | audio files (`wav` / `ogg` / `mp3` / `flac`) | Point `"sounds"` at the files — **no FMOD tooling needed**. Any recorder/DAW (e.g. [Audacity](https://www.audacityteam.org), free) works. |
| **C# addon DLLs** (custom AI/controllers beyond the presets) | .NET 8 SDK + a decompiler ([ILSpy](https://github.com/icsharpcode/ILSpy)) | Only for advanced cases — see [ADVANCED.md](ADVANCED.md#custom-controllers-c). Packs themselves never need compiling. |

## 5. What you can add
Items, weapons, spells, summons, creatures, dungeons, NPCs, quests, classes, placeables, events and more —
the full capability list, deep-linked per feature, is the [README.md capability index](../README.md#what-packs-can-do-capability-index--packsmd).
Every field: [PACKS.md](PACKS.md) · copy-paste recipes: [COOKBOOK.md](COOKBOOK.md) · valid ids: `reference/*.txt` ([REFERENCE.md](REFERENCE.md)).

## 6. Worked examples
- **`examples/necromancer/`** — a complete themed pack: class + book + staff + robe + recipe + quest + drop +
  placeable. Copy it, rename the namespace, make it yours.
- **`examples/test/`** — one pack that uses *every* feature (it's the mod's own test bench).

## 7. Testing tips
- `BepInEx\LogOutput.log` tells you what loaded: a per-pack load summary, plus a warning for anything skipped
  (with the reason). Search your namespace.
- Recipes/quests/classes need the right context (crafting window, a fresh world, character creation).
- **Dungeon entrances roll in NEW worlds** (vanilla rule) — create a new world to find yours, set `"retrofit": true` on the map to place one into an existing save, or use a `forge_portal` placeable.
- Removing a pack is save-safe: its items become harmless placeholders.

## 8. Co-op
The **host** must run the mod and the packs (content is server-authoritative). For maps, all players need the
pack. Easiest: everyone installs the same pack files — the load summary shows pack versions so you can compare.

## 9. Ship it
A pack is just your json + assets. Zip the files, post it anywhere (Nexus etc.), list **Romestead Content Forge**
as a requirement, and tell players: *"drop into `BepInEx\config\RomesteadContentForge\packs\`"*.
