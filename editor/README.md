# Content Forge — Pack Editor

_What this is: the GUI pack editor — run it, build it, troubleshoot it. What this isn't: the pack format itself
(see [../PACKS.md](../PACKS.md)). Docs index: [../README.md](../README.md)._

A cross-platform (Windows / macOS / Linux) visual editor for Content Forge packs. Builds its forms from
`schema.json` (the field manifest mirroring `ContentPack.cs` + `PACKS.md`), so it always matches the pack format.
Edits the same `.json` the mod loads and the validator (`validate.py`) checks.

## Run
```bash
pip install -r requirements.txt
python forge_editor.py
```
(Python 3.9+.) No Python? The release download on Nexus ships a prebuilt **`ForgeEditor.exe`** — it's the
same editor, packaged from this folder via `build.py` (see below).

## Use
- **Left** = content sections, **grouped like `PACKS.md`** (Items & Gear, Magic & Spells, Summons & Creatures,
  World & Maps, NPCs & Citizens, Placeables & Buildings, Quests & Progression, PvP & Events, Visuals & Audio,
  Player Looks, Advanced). **Every** `ContentPack.cs` section has a form — including `sounds`, `icons`,
  `strings`, `shopAdditions`, `citizenOverrides`, `playerAnimations`, `townBuildings`, `workRecipes`,
  from-scratch `spacs` (with the `animations` dict), and the EventsPlus2 event types (teamBattle / koth / race).
  Packs that use the `placeables` or `structures` alias keys open in the same Placeables section.
- **Middle** = entries in that section (**+ Add** / **Remove**).
- **Right** = the form for the selected entry — section title on top, every field from the manifest, with:
  - **dropdowns** for enums (weapon class, damage types, cast type/target, equipment type, event type…),
  - **catalog dropdowns** for id fields — icons, held looks, auras, items, drop tables, creatures, spacs, vfx,
    favours (parent), biomes, FMOD sound events, raids, music tracks, entities — sourced from `reference/*.txt`;
    they're **editable**, so your own new ids still type freely, and intra-pack refs (a `spellId` you defined, a
    `palette` you added, a `cf:sound:…` from your `sounds` block) appear automatically,
  - **checkboxes** to toggle optional blocks (equippable → weapon / shield / spellTome / display, usable, light),
  - **PNG pickers + thumbnails** for sheet/icon fields (also inside key→file maps like creature `sheets`),
  - **key→value rows** for damage, stat bonuses, recipe ingredients — and **key→object rows** for spac
    `animations`, `playerAnimations` and `citizenOverrides`,
  - a **point-list** editor for race `checkpoints` (one `x, y` per line),
  - **per-language** name/description editors (`nameLocalized` etc.) — a (language, text) row table whose
    language cell is a dropdown of the game's supported codes (`en` excluded — that's the plain `name`/
    `description` field), still free-typeable for future codes, with a **+ language** button,
  - an **Edit dialogue…** button on `.yarn` fields (NPCs and talking placeables) opening the built-in
    **yarn dialogue editor** (below),
  - help shown as a **subtle caption under each field** (plus tooltips).
- **Toolbar**: New (Ctrl+N) / Open (Ctrl+O) / Save (Ctrl+S) / Save As (Ctrl+Shift+S) / Validate (F5).
- **Status bar** shows the pack id, file name and how many sections/entries the pack has.
- **Validate** results land in an **inline panel** at the bottom — errors red, warnings amber; **click a line
  to jump** to the offending section/entry.
- The **editor's own UI is translatable** — a **Language** menu switches the whole interface (English / Türkçe /
  Deutsch / Español built in), including the **right-pane form field labels** (description, max stack, weapon
  stats…). The built-in toolbar/menu languages are **embedded in the editor itself**; to add or override one,
  create an optional `lang/<code>.json` (same keys) next to the editor — it's read at startup if present (the
  editor never writes it). The form field labels live in `labels.json` (`key -> { langcode -> label }`). Both
  fall back to English then the raw key.
- **Open / Save / Save As** a `pack.json`. Put your `.png` files next to it (the pickers store just the filename).
- **Validate** runs **in-process** (pure Python, `validate.py`) — no .NET needed — and checks your **current
  unsaved edits**: bad enum values, duplicate ids, broken refs, missing `.png` files, un-namespaced ids, missing
  dependencies, out-of-range drop chances. You can also run it standalone: `python validate.py <packs-folder>`.

## Yarn dialogue editor
Click **Edit dialogue…** next to an NPC's (or talking placeable's) `yarn` field — the pack must be saved first
(paths resolve next to the pack json). If the `.yarn` doesn't exist yet you're offered a minimal starter
(`title: Start`, one speaker line with the entry's first actor, `===`). The modal editor gives you:
- **Left** — the node outline (every `title:`); click to jump to that node.
- **Center** — the yarn text in a monospace, syntax-highlighted editor (structure amber, `->` options bronze,
  `<<commands>>` blue, `speaker:` prefixes green, `#line:` tags and `//` comments dim).
- **Auto-tag lines** — appends a `#line:<file>_NN` tag to every translatable line missing one (speaker lines
  and `->` options; never command-only or structural lines). Idempotent — run it as often as you like.
- **Lint** — structural checks (every node has `title:` + `===`), `<<Actor>>` ids and speaker prefixes checked
  against the entry's `actors` map, untagged-translatable-line warnings, and Forge command arity
  (`ForgeGiveItem`/`ForgeTakeItem` take 2 args, `ForgeHeal` takes 1). Results in a colored list, like Validate.
- **Right** — the **translations grid**: pick a locale (or **+ language** a new one), and translate line by line
  — line id | original (read-only) | translation. Untranslated rows are highlighted dim-amber and a
  **translated n/m** coverage counter tracks progress. Speaker lines **must keep** the `actorid: ` prefix in
  the translation — empty cells come **prefilled with it**. Backed by the `line:id,text` sidecar csvs next to
  the pack (UTF-8 no BOM, first-comma split — commas and diacritics in the text are safe).
- **Save** writes the `.yarn` plus every locale csv, and the pack entry's `translations` map gains new locales
  automatically (save the pack itself to persist that). The yarn parsing/tagging/lint/csv logic lives in
  `yarn_tools.py` (pure Python, unit-tested).

## Do you even need to build it?
**Mac & Linux already ship with Python** — those users can just `pip install PySide6` and run
`python forge_editor.py` (or double-click it). Packaging into a standalone binary is mainly a **Windows**
convenience (Windows often has no Python). So: ship the **Windows `.exe`**, and tell Mac/Linux users to run the
script directly.

## Package as a double-click app (no Python for end users)
```bash
pip install pyinstaller
python build.py
```
Produces `dist/ForgeEditor` (`.exe` on Windows, `.app` on macOS, a binary on Linux) — a single double-clickable
file that **bundles Python, Qt, the schema, the id catalogs, and the validator**. `build.py` handles the per-OS
details (data-file separators, icon, hidden imports).

**One binary per OS** — there's no universal cross-platform executable (PyInstaller can't cross-compile; macOS
also requires a Mac per Apple). Options:
- **`python build.py`** — builds for the OS you run it on (Windows/macOS/Linux).
- **`./build.ps1`** (Windows PowerShell or `pwsh`) — wraps `build.py` for the local OS, and `-Linux` also builds a
  Linux binary via Docker. macOS still needs a Mac.
- **CI does all three automatically:** `.github/workflows/build-editor.yml` builds Windows / macOS / Linux in
  parallel and uploads them as artifacts (trigger manually, or push an `editor-v*` tag). This is the only way to
  get a Mac build without a Mac.

## Tests
```bash
pip install pytest
python -m pytest editor/tests -q
```
Pure pytest + stdlib (the headless Qt tests run with `QT_QPA_PLATFORM=offscreen`, set automatically):
- `test_validate.py` — the shipped example packs validate with **0 errors**, and ~25 deliberately broken packs
  each trip their **specific** error/warning message.
- `test_schema.py` — schema sanity (idFields, no duplicate keys, every dropdown `source` resolves, every
  catalog file exists) **plus the parity gate**: it regex-parses `ContentPack.cs` and fails if a top-level
  section is missing from `schema.json`. Add a section to the DTO and this test reminds you to mirror it.
- `test_editor_headless.py` — the editor builds every section form (empty, blank-entry and real-pack), and a
  full pack survives load → render everything → save **byte-for-byte semantically equal**.
- `test_yarn_tools.py` — yarn node split, auto-tag idempotence, lint findings on a deliberately bad yarn,
  translation-csv round-trips (commas, diacritics, quoted values), original/prefix extraction.
- `test_yarn_dialog.py` — the LocalizedEditor language table (en excluded, free-typeable, JSON round-trip)
  and the YarnDialog offscreen (grid, prefix prefill, auto-tag/lint buttons, save writes yarn + csvs and
  grows the entry's `translations`).

## Keeping it in sync
When the mod's `ContentPack.cs` gains a field, add the same field to `schema.json` — the editor picks it up with
no code change. (Same single-source principle as the validator.) `test_schema.py` enforces this for whole
sections; run the suite before shipping.
