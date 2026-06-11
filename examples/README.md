# Content Forge — example packs

_What this is: the shipped example packs and what each demonstrates. What this isn't: a tutorial
([../docs/GETTING_STARTED.md](../docs/GETTING_STARTED.md)) or step-by-step walkthroughs of these packs
([../docs/COOKBOOK.md](../docs/COOKBOOK.md)). Docs index: [../README.md](../README.md)._

Self-contained packs that show how anyone builds content with the framework. Each is a normal pack — nothing
special, no code. **To try one:** copy **all of the folder's files** (the `.json` plus its `.png`/`.tmx`/
`.yarn`/`.csv`/`.wav` assets) into
`…\romestead\BepInEx\config\RomesteadContentForge\packs\`, then launch. **To make your own:** copy a folder,
rename the `namespace`, and edit (by hand or in the GUI editor).

## necromancer/
A necromancer who raises the dead — demonstrates **summons**, a **spell-tome**, an **on-swing staff**, **gear with
stat bonuses**, a **recipe**, **multi-language** names, and a **loot drop**:

- **Book of the Dead** (offhand spell-tome) — attack to raise **two skeleton warriors** (`SkeletonWarrior`, `count: 2`) that fight for you.
- **Bone Staff** (weapon, `castSpell` `OnUse`) — each swing raises a **skeleton warrior** (companion).
- **Necromancer Robe** (armor) — `+50 Energy`, `+25% Necro damage`.
- A **recipe** to craft the book, and a 2% **drop** from Fallen Swordsmen.

Summons join your faction (`entityType: PlayerSummon`) so they fight your enemies. Creature names come from
`reference/creature-ids.txt`.

## worldoftales/
A small **world pack**: a custom dungeon room (`realm.tmx`) with its own **music, ambience and dark
ambient light**, a dialogue **NPC** (the Keeper of Tales, with a Turkish translation sidecar), a token-pattern
**quest** and a **recipe** reward. The pattern to copy for story/exploration packs.

## test/
The mod's own **test bench** (namespace `cftest`) — one pack that exercises *every* framework feature
(items, spells, summons, maps, NPCs, placeables, crops, citizens, events, sounds, skins, palettes, …),
with assets showing each custom-art path (yarn + translations, tmx, wav, repaint PNGs). Each entry is named
`T## …` to match the rows in `TESTING.md`. It now also demos the marathon wave — **T75–T89**: the five new
AI presets (patrol/pack/summoner/healer/turret tomes), the composite **T84 Grove Shrine**, the **T85 Grand
Bakery** town building + work recipe, a `retrofit` entrance, the from-scratch **T89 Wisp** rig, and the
teamBattle/koth/race events. Not a style guide — a coverage net — but handy as a lookup for
"what does a working X look like."

> These examples are how a published pack looks. Authors list **Content Forge** as a dependency and ship just
> their pack folder — the framework (this mod) does the rest.
