# 📜 Content Forge — Complete Pack Reference

**[« Docs index](../README.md)**

_What this is: the full field-by-field spec of the pack format. What this isn't: a tutorial
([GETTING_STARTED.md](GETTING_STARTED.md)), copy-paste recipes ([COOKBOOK.md](COOKBOOK.md)), or C#/tooling
workflows ([ADVANCED.md](ADVANCED.md)). Docs index: [README.md](../README.md)._

**New here? Read `GETTING_STARTED.md` first** — install, a 5-minute first pack, and a feature tour. This file
is the full field-by-field spec you come back to.

Every field, every valid value, what each does. Big **id lists** (icons, held-visuals, auras, item ids) are in
`REFERENCE.md` + the `reference/*.txt` dumps; this file is the authoritative **field spec**.

A **content pack** = one `.json` file in `BepInEx\config\RomesteadContentForge\packs\`. Put custom `.png` files
beside it. Many packs allowed; a bad one logs an error and is skipped. Restart the game to reload.
**Tip:** namespace your ids (`mymod:thing`) to avoid collisions. Custom items auto-appear in Item Spawner (F1).
Validate a pack before launching: `python editor/validate.py <pack.json or folder>` (cross-platform, no .NET) or the editor's **Validate** button. See [Validate](#validate).
**Prefer a GUI?** The cross-platform **visual editor** (`editor/`, see `editor/README.md`) writes these same packs
with dropdowns, previews, per-language text, and live validation — no hand-written JSON required.

## Contents

**Start here:** [Conventions](#conventions) (units, scales, terms — read once) ·
[Top level](#top-level) — the pack manifest (id/namespace/version/deps) + every content
section at a glance. The sections below are grouped the same way as the `README.md` index.

- **Items & Gear** — [Item](#item) · [Equippable](#equippable) (slots, stat bonuses) · [Usable](#usable-item-usable--consumable--potion) · [Recipe](#recipe) · [Drop (mob/chest loot)](#drop-mob--chest-loot)
- **Combat & Weapons** — [Weapon](#weapon) · [Cast (weapon `castSpell`)](#cast-weapon-castspell--fire-a-spell-from-a-weapon) · [SpellTome](#spelltome-equippable-spelltome--classic-scrollwand) · [Shield](#shield-equippable-shield) · [Display (held looks + trails)](#display-equippable-display--custom-held-look)
- **Magic & Spells** — [Spell](#spell) (+ [Chain](#chain-spell-chain--cast-on-hit) · [Attack](#attack-spell-attack--damage)) · [Spell behaviors (SpellsPlus) & `requires`](#spell-behaviors-spellsplus--casting-requirements)
- **Summons** — [Summon](#summon-spell-summon--spawn-a-creature-that-fights-for-you) · [Creature](#creature-top-level-creatures--a-custom-creature)
- **World, Maps & Dungeons** — [Map](#map-top-level-maps--a-custom-dungeon-room) (+ [atmosphere](#map-atmosphere-fields-on-maps-entries) · [pre-placed doodads](#pre-placed-doodads-props-in-your-maps)) · [Shop](#shop-top-level-shops--sell-your-items-at-the-market) · [Crop](#crop-top-level-crops--plantable-crops)
- **NPCs, Dialogue & Citizens** — [NPC](#npc-top-level-npcs--a-talking-character-in-your-maps) · [Citizens](#citizens-top-level-citizens--citizenoverrides--town-job-workers)
- **Placeables & Buildings** — [Placeables](#placeables-top-level-placeables-aliases-furniture--structures): [basic](#basic-placeables-vanilla-art-via-spawnedid) · [custom-sprite](#custom-sprite-placeables-your-own-art) · [composite (multi-part)](#composite-placeables-multi-part-parts) · [interactive (presets)](#interactive-placeables-controller-presets) · your own C# controller → [ADVANCED.md](ADVANCED.md#custom-controllers-c) · [Town buildings](#town-building-top-level-townbuildings--a-citizen-staffed-workshop) · [Work recipes](#work-recipe-top-level-workrecipes--what-citizens-produce)
- **Quests & Progression** — [Class](#class-top-level-classes--a-new-playable-profession) · [Quest](#quest-top-level-quests--a-world-quest) · [Skill](#skill-top-level-skills--a-new-player-skill) · [Favour](#favour-top-level-favours--a-new-favour-tree-node)
- **PvP, Arenas & Events** — [Arenas](#arenas-top-level-arenas--duel-events-on-pvp-maps) · [Events](#events-top-level-events--server-side-world-events)
- **Visuals, Audio & Animation** — [Palette](#palette-custom-dye-ramp) · [Vfx](#vfx-custom-sprite-animation-effect) · [Particles & Emitters](#particles--emitters-top-level-particles--emitters) · [Spacs](#spacs-top-level-spacs--clone--reskinretime-a-vanilla-sprite-animation) (+ [from-scratch spacs](#from-scratch-spacs-no-base--brand-new-animations-from-your-pngs)) · [Player animations](#player-animations-top-level-playeranimations--retime-the-rig-visual) · [Sounds (drop-in audio)](#sounds-top-level-sounds--drop-in-audio-files) · FMOD banks → [ADVANCED.md](ADVANCED.md#fmod-banks)
- **Player Looks & Rigs** — [PlayerSkins](#playerskins-top-level-playerskins--the-players-own-look)
- **Cookbook** — moved to [COOKBOOK.md](COOKBOOK.md) (copy-paste recipes by goal: flame sword, staff, chains, shrines, portals, walkthroughs…)
- **Co-op & Safety** — [Co-op & saves](#co-op--saves)
- **Tooling** — [Validate](#validate) · GUI editor: `editor/README.md` · id catalogs: `REFERENCE.md` + `reference/*.txt`

---

## Conventions

- **Pack asset paths** (`icons`, `sheet`, `sprite`, `tmx`, `yarn`, sounds, ...) are relative to the pack's
  folder — subfolders work (`"art/blade.png"`, forward slashes).

Units & terms used everywhere in this spec — read once, then the field tables make sense:

- **`0` usually means "unset — use the vanilla value", not "zero effect":** most numeric weapon fields
  (`attackRange`, `attackArcDegrees`, `knockback`…) default to `0` and fall back to the vanilla number.
- **Percent vs. fraction** — two scales, easy to mix up:

  | Field | Scale | Example |
  |---|---|---|
  | drop `chance`, crop drop `chance` | **percent** `0..100` | `5` = 5% |
  | weapon cast `probability` | **fraction** `0..1` | `0.25` = 25% |
  | percentage stats in `statBonuses` (`CritChance`, `*Modifier`, `*Resistance`, `MovementSpeed`, `AttackSpeed`) | **fraction** `0..1` | `0.1` = +10% |

- **Two color scales:** creature `color`, NPC `tint`, rig `tint` = **0–255 ints** (`[180, 120, 255]`);
  map `ambientLight` and particle colors = **0–1 floats** (`[0.15, 0.1, 0.2]`).
- **Three timing units:** `vfx.frameDuration` = **ticks** (60 ticks/sec) · particle `frameDuration` =
  **seconds** · spac / player-animation `frameDurationScale` = a **multiplier** on the vanilla timing.
- **16 pixels = 1 tile.** Radii, ranges and map positions are in px; placeable footprints are in tiles.
- **Case:** ids are **case-sensitive**, exactly as listed in `reference/*.txt`; enum-ish fields
  (`equipmentType`, `material`, weapon `class`, `castType`…) are case-insensitive.
- **Terms:** **`cdd:`** = CharacterDisplayData id — the game's "what a held/worn thing looks like"
  registry (catalog: `reference/display-ids.txt`) · **spac** = the game's sprite-animation collection
  format (multi-direction animated sprite sheets) · **doodad** = the game's term for a placed world
  object/entity instance · **POI** = point of interest (an overworld map feature, e.g. a dungeon entrance).

---

## Top level

**Manifest** (identity — so many authors' packs coexist):

| Field | Type | What it does |
|---|---|---|
| `id` | string | **Pack id** — unique across all installed packs. Used in logs, dependencies, the co-op load summary. Defaults to the filename. |
| `namespace` | string | **Your prefix**, e.g. `acme`. If set, every content id should be `"<namespace>:..."`; the loader **warns** on any id that isn't, so two authors never collide. Defaults to `id`. |
| `author` | string | Shown in the load summary. |
| `version` | string | Your pack's version (co-op mismatch warnings). |
| `description` | string | Free text, shown in the summary. |
| `dependencies` | array of string | Pack `id`s this pack needs. A missing dependency **skips this pack** with a clear warning; present deps load **before** this pack (so you can reference their items/recipes). |
| `apiVersion` | int | Content-format version this pack targets (future-proofing). Default `1`. |

**Content** (each section optional):

| Field | Type | What it does |
|---|---|---|
| `strings` | object (key→text) | **Optional/advanced.** Register raw localization keys yourself. Item/spell names go inline (below) — you rarely need this. |
| `icons` | object (iconId→file) | Custom PNG icons: `"mymod:ic": "ic.png"`. File sits next to the pack. **Author 18×18** (native icon size) with a transparent margin. |
| `palettes` | array of **Palette** | Custom dye ramps (recolor held looks). See Palette below. |
| `vfx` | array of **Vfx** | Custom sprite-animation effects (impacts, bursts, auras). See Vfx below. |
| `spells` | array of **Spell** | Custom spells (cast by scrolls/staves/weapons). |
| `items` | array of **Item** | The items. |
| `recipes` | array of **Recipe** | Crafting recipes (makes items craftable). |
| `drops` | array of **Drop** | Add items to mob/chest loot tables with a % chance. See Drop below. |
| `classes` | array of **Class** | New playable professions (appear in character creation). See Class below. |
| `quests` | array of **Quest** | World quests that auto-activate + reward recipes. See Quest below. |
| `placeables` | array of **Placeable** | Anything buildable from the construction menu — decor, shrines, levers, talking stones. Aliases: `furniture`, `structures`. See Placeables below. |
| `creatures` | array of **Creature** | Custom creatures (clone + restat/rename/recolor a vanilla one). See Creature below. |
| `maps` | array of **Map** | Custom dungeon rooms authored in the free Tiled editor. See Map below. |
| `shops` | array of **Shop** | New market stock sections (the merchant sells them). See Shop below. |
| `shopAdditions` | array | Add items into an existing vanilla shop section. See Shop below. |
| `crops` | array of **Crop** | Plantable crops with growth stages + auto-generated seed items. See Crop below. |
| `particles` / `emitters` | arrays | Custom particle effects (usable as spell trails etc.). See Particles below. |
| `npcs` | array of **NPC** | Talking characters (YarnSpinner dialogue) standing in your maps. See NPC below. |
| `skills` | array of **Skill** | New player skills (skills screen, level 0–100, trained by your weapons/spells). See Skill below. |
| `favours` | array of **Favour** | New favour-tree nodes (gods' favour tree, 1 point each, stat auras / recipe unlocks; uninstall-safe). See Favour below. |
| `sounds` | object (name→file) | Raw audio files next to the pack: `"ding": "ding.wav"` → playable as `cf:sound:<namespace>:ding`. See Sounds below. |
| `arenas` | array of **Arena** | Duel arenas bound to this pack's `pvp: true` maps (kill feed, timed matches, rewards). See Arenas below. |
| `events` | array of **Event** | Server-side world events: wave defense, boss rush, hunt contests, scheduled raids. See Events below. |
| `citizens` / `citizenOverrides` | array / object | Custom town citizens (fixed name/look/job levels, arrival weighting) + edits to vanilla citizen types. See Citizens below. |
| `townBuildings` | array of **TownBuilding** | Citizen-staffed job-buildings (bakery/forge tier) cloned from a vanilla one — new buildable workshops. See Town building below. |
| `workRecipes` | array of **WorkRecipe** | New work recipes for a job's work queue (what a baker bakes / a smith forges). See Work recipe below. |
| `spacs` | array of **Spac** | Clone+reskin/retime a vanilla sprite animation — **or build one from scratch** (no `base`) from your own PNGs; the id works anywhere a spac/vfx id is taken. See Spacs below. |
| `playerAnimations` | object | Retime player animation states (visual). See Player animations below. |
| `playerSkins` | object | Player reskins, new hair styles, new worn armor looks. See PlayerSkins below. |

**Multi-language coverage:** `nameLocalized` (+ `descriptionLocalized` where there's a description) works on
**items, spells, placeables, quests, classes, creatures, maps (dungeon tooltip), crop seeds, skills, favours,
and citizens** — same shape everywhere: `{ "tr_TR": "...", "de_DE": "..." }`, falling back to the plain `name`. NPC **dialogue lines**
localize via the `translations` sidecar files; NPC **actor names** via `actorsLocalized`
(`{ "tr_TR": { "key": "name" } }`). All of it follows the game language and live-switches.

**Custom sounds:** drop FMOD `.bank` files in the packs folder or a `banks\` subfolder — they load at startup
and their events play as `event:/yourmod/...` anywhere a sound id is taken (e.g. spell `castSound`). Reusing
the game's existing `event:/...` ids needs no bank; authoring a bank needs FMOD Studio — guide:
[ADVANCED.md — FMOD banks](ADVANCED.md#fmod-banks). *(For simple one-shots
there's also a drop-in-a-wav path with no FMOD Studio at all — the `sounds` map, see Sounds below.)*

---

# ─ Items & Gear ─

## Item

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — (required) | Unique item id. | namespaced, e.g. `mymod:flameblade` |
| `name` | string | id | **Literal** display name (default / fallback). | any text |
| `nameLocalized` | object | none | **Per-language** name: `{ "tr_TR": "...", "de_DE": "..." }`. The mod shows the entry for the current game language, falling back to `name`. | language codes below |
| `description` | string | none | **Literal** tooltip description (default / fallback). | any text |
| `descriptionLocalized` | object | none | Per-language description, keyed by language code. | language codes below |
| `icon` | string | `sam` | Inventory icon. | an existing icon id **or** your custom `icons` id. List: `reference/icon-ids.txt` (520) — e.g. `sword:iron`, `ring:red`, `apple:red`, `scroll:brown`, `shield:iron`, `crossbow` |
| `maxStack` | int | 1 | Max stack size. | ≥1 |
| `tier` | int | none | Rarity tier (cosmetic). | typically 0–6 |
| `flags` | string | none | Item category flags (comma-sep). | `Food`, `Material`, `Ammunition`, `Money`, `Millable`, `Quest`, `Seed`, `Offering` |
| `price` | int | none | Sell/buy value (makes it tradable). | ≥0 |
| `light` | **Light** | none | Makes it emit light (torch). | see Light |
| `equippable` | **Equippable** | none | Makes it wearable (gear/weapon/shield/scroll). | see Equippable |
| `usable` | **Usable** | none | Makes it a consumable (food/potion). | see Usable |

> **Multi-language:** `nameLocalized` / `descriptionLocalized` (and a spell's `displayNameLocalized`) take a
> map of **language code → text**. The mod re-applies text whenever the game language changes, picking your
> translation for the active language and falling back to the plain `name`/`description`. Language codes:
> `en`, `zh_CN`, `zh_TW`, `de_DE`, `es_ES`, `fr_FR`, `pl_PL`, `pt_BR`, `ru_RU`, `ja_JP`, `tr_TR`, `ko_KR`, `sk_SK`.
> The GUI editor has a per-language name/description editor (pick language → type the translation).

### Light
| Field | Type | Default | What |
|---|---|---|---|
| `radius` | float | 128 | Light radius **in pixels** (the vanilla torch is 128). |
| `strength` | float | 1 | Light intensity. |

To make a **holdable torch**, also give the item `equippable: { "equipmentType": "LightSource",
"statBonuses": { "LightSource": { "additive": 1 } } }` — that's how the vanilla torch works (it equips in the
torch slot and lights up while held). A `light` block alone just marks the item as a light source.

## Equippable

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `equipmentType` | string | — | Which slot it goes in. | see **Equipment slots** below |
| `material` | string | `Steel` | Cosmetic material tier (affects some tooltips). | `Flint`, `Copper`, `Bronze`, `Iron`, `Silver`, `Gold`, `Steel`, `Cheat`, `Rusted` |
| `displayId` | string | none | Held/equipped look (reuse a vanilla model). | a `cdd:…` id — list `reference/display-ids.txt` (191), e.g. `cdd:iron_sword`, `cdd:shield_iron`, `cdd:crossbow_leather` |
| `display` | **Display** | none | OR compose a held look from a shape + recolor (overrides `displayId`). | see Display |
| `entityAuraId` | string | none | Apply an existing aura while equipped (a passive buff). | an `aura:…` id — list `reference/aura-ids.txt` (149) |
| `statBonuses` | object (stat→**StatBonus**) | none | Flat/multiplier stat boosts. | keys = **stat keys** below |
| `weapon` | **Weapon** | none | Makes it a melee/ranged weapon (damage). | see Weapon |
| `shield` | **Shield** | none | Makes it a shield. | see Shield |
| `spellTome` | **SpellTome** | none | Makes it a scroll/wand: equip + attack casts a spell (static "raise" pose). | see SpellTome |

### Equipment slots (`equipmentType`)
| Value | Use |
|---|---|
| `Helmet` | head armor |
| `Armor` | chest armor |
| `Boots` | feet armor |
| `Trinket` | accessory (rings/idols) |
| `Back` | cape/backpack |
| `Weapon` | **main-hand melee** weapon (swords, spears, hammers…) |
| `Offhand` | off-hand: **ranged** (bows/crossbows, need ammo), **scrolls/tomes**, **shields** |
| `Ammunition` | arrows/javelins |
| `LightSource` | torches |
| `LumberAxe` / `Pickaxe` / `FishingRod` | tools |

### StatBonus (value of a `statBonuses` entry)
| Field | Type | Default | What |
|---|---|---|---|
| `additive` | float | 0 | Flat add (most common; e.g. `Health: {additive:100}`, `CritChance: {additive:0.1}`). |
| `multiplier` | float | 0 | Final multiplier. |
| `additiveMultiplier` / `baseMultiplier` / `bonusMultiplier` | float | 0 | Advanced multiplier stages. |

**Stat keys:** `Health`, `Energy`, `EnergyRegeneration`, `Armor`, `CritChance`, `AttackSpeed`, `MovementSpeed`,
`MeleeDamage`, `RangedDamage`, `MagicDamage`, `ThrowingDamage`, `PickaxePower`, `AxePower`,
`HealingDoneModifier`, `Knockback`, `KnockbackResistance`, `MagicDamageModifier`, `RangedDamageModifier`,
`ThrowingDamageModifier`, `BludgeoningDamageModifier`, `PiercingDamageModifier`, `NecroDamageModifier`,
`ChloroDamageModifier`, `PyroDamageModifier`, `CosmoDamageModifier`, `PyroResistance`, `AquaResistance`,
`ChloroResistance`, `CosmoResistance`, `NecroResistance`, `PiercingResistance`, `BludgeoningResistance`,
`MagicResistance`.

> ⚠️ **Units:** flat stats (`Health`, `Energy`, `Armor`, `MeleeDamage`…) take plain numbers (`additive: 100` = +100).
> **Percentage stats** — `MovementSpeed`, `AttackSpeed`, `CritChance`, every `*Modifier` and `*Resistance` — are
> **fractions of 1**: `additive: 0.25` = **+25%**. (`MovementSpeed: {additive: 50}` is +5000%, not +50.)

## Usable (item `usable`) — consumable / potion
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `type` | string | `Special` | Consumable kind. | `Healing` = eat/drink consumable with built-in healing (auto-usable from inventory/hotbar). `Special` = **no built-in effect** — using it does nothing unless `spellId` is set. For an offensive "use", a weapon/`spellTome` usually fits better. |
| `instantHeal` | float | 0 | Heal-on-use (with `type: Healing`). | ≥0 |
| `slowHeal` / `slowHealTime` | float | 0 | Heal-over-time amount / duration. | ≥0 |
| `spellId` | string | none | Cast a spell on use — fires for **both** `type` values (a `Healing` item heals *and* casts; a `Special` item's only effect is the cast). | a spell id |
| `usesMax` | int | 1 | Uses before consumed (0 = infinite). | ≥0 |
| `cooldown` | float | 0 | Seconds between uses. | ≥0 |

## Recipe

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique recipe id. | namespaced |
| `crafted` | string | — | Item id this recipe produces. | your item id (or a vanilla one) |
| `amount` | int | 1 | How many produced. | ≥1 |
| `station` | string | none | Where it's crafted. | omit/`null` = basic crafting · `campfire` · `cauldron` · `clay-barrel` · `war-table` · **a pack station id** (a placeable with `station: "my:runeforge"` — see Placeables) |
| `ingredients` | object (itemId→int) | none | Required items → counts. | item ids — list `reference/item-ids.txt` (675), e.g. `material:iron_bar`, `material:coal`, `food:apple` |

**Visibility:** your recipes show in the crafting menu automatically — **except** a recipe listed in a quest's
`rewardRecipes`, which stays hidden until that quest completes (so quest rewards mean something).

## Drop (mob / chest loot)
Add an item to an **existing** loot table (a mob's, a world-chest's…) with a chance. Server-authoritative
(the host's tables) — fits co-op. Table ids: `reference/droptable-ids.txt` (117) — e.g. `enemy:fallen_sword`,
`enemy:burning_fallen`, `boss:cyclops`, `plains:major` (biome chest).

> **Note:** wildlife (bears, wolves, deer…) has **no shared drop table** — animal loot is baked into each
> creature's entity, so the `table` path can't reach animals. For those, use **`creature`** instead of `table`
> (below): it appends the drop to that creature's own baked-in loot.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `table` | string | — | The loot table to add into (must already exist). | a drop-table id (see reference) |
| `creature` | string | — | **Alternative to `table`** (set one or the other): append the drop to a CREATURE's own baked-in loot — the only way to add **wildlife** drops. Every later spawn of that creature carries it. Server-side (host's prototypes). Vanilla rule: kills **without a player killing blow** only roll loot 5% of the time. | a pack creature id, or an EntityIds name from `reference/creature-ids.txt` (e.g. `Bear`) |
| `item` | string | — | Item to drop (yours or vanilla). | an item id |
| `chance` | float | 5 | Drop chance, in **percent** (`5` = 5% — *not* a 0..1 fraction; see [Conventions](#conventions)). | `0..100` |
| `min` / `max` | int | 1 | Stack size dropped. | ≥1 |
| `aura` | string | none | Optional aura applied to the dropped item. | an aura id |

```jsonc
"drops": [ { "table": "enemy:fallen_sword", "item": "mymod:flamebrand", "chance": 5, "min": 1, "max": 1 } ]
```

---

# ─ Combat & Weapons ─

*These are all blocks on an item's `equippable` — see [Equippable](#equippable) above.*

## Weapon
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `class` | string | `Sword` | Weapon type → **attack animation + skill**. | see **Weapon classes** |
| `swingTimer` | float | 1 | Seconds per attack (lower = faster). | >0 |
| `knockback` | float | 0 | Knockback on hit. | ≥0 |
| `attackRange` | float | 0 | Reach (melee). | ≥0 |
| `energyCost` | float | 0 | Energy per attack (0 = free). | ≥0 |
| `minDamage` / `maxDamage` | object (type→float) | none | Damage range by **damage type**. | keys = damage types below |
| `stunPower` | float | 0 | **Stagger strength** applied on hit. Vanilla weapons use `0..1` (`0.5` = mace); higher values exaggerate the stagger. | ≥0 |
| `movementFactor` | float | 1 | Move-speed **while attacking/charging**. | `1` free · `<1` slower (heavy) · `0` rooted |
| `damageVfx` | string | `Normal` | Hit-impact VFX size. | `Normal` / `Small` / `None` |
| `hitVfx` | string | none | **Extra VFX played at the hit position** when this weapon damages an enemy — fire bursts, holy flashes… Plays for remote players' hits too. | one of your `vfx` ids, or a vanilla spac id (`hit:crit`, `hit:med`…) |
| `hitVfxScale` | float | 1 | Scale for `hitVfx` (sprite-animation vfx only). | >0 |
| `attackArcDegrees` | float | 0 = unset → vanilla 144 ([Conventions](#conventions)) | **Swing-cone width** in degrees — e.g. `300` = near-circle swipe. Cone melee only (Sword/Dagger/Sledgehammer swings); **spears stab in a line and ignore it**; the sword whirlwind perk keeps its full 360°. | >0..360 |
| `attackArcOffsetPx` | float | 0 | **Forward offset (px) of the swing cone's origin** — negative pulls the hit zone toward your body (vanilla daggers use `-24`). Spears ignore it. | any |
| `twoHanded` | bool | false | Main-hand weapons only. While this weapon is in a set's weapon slot, that set's offhand slot is kept empty: equipping the weapon ejects the current offhand to your bag, and equipping an offhand while holding it bounces back **to the bag** after a brief flicker (bag full = dropped at your feet; never destroyed). Enforced on the host for both weapon sets — swapping sets never false-ejects, and a two-handed weapon parked in the INACTIVE set keeps that set's offhand empty too. Unmodded co-op clients simply see the item move (the host does the enforcement). Note: bows/crossbows are offhand items in this game, so a two-handed weapon also blocks them. | true/false |
| `comboSteps` | array | none | Per-combo-step tuning: index = hit of the chain, each `{ "damageMultiplier": 1.0, "arcDegrees": 0, "swingTimerScale": 1.0 }`. `damageMultiplier` REPLACES the class's stance multiplier for that hit (sword's vanilla 3rd hit = 1.25); `arcDegrees` overrides `attackArcDegrees` for that hit; `swingTimerScale` stretches that hit's swing. **Sword** = 3 steps (whirlwind's 360° spin stays vanilla); **Dagger** cycles per stab; **Sledgehammer** = step 0 only (charged smash vanilla); **Spears** inert. Client-side. | array |
| `specialEnergyCost` | float | 0 | Energy for the **charged** attack (see `castSpell.chargedSpellId`). | ≥0 |
| `skillId` | string | the class's skill | The skill this weapon **trains and scales with** — e.g. your pack's `skill:runeology`. Omit = the weapon class's vanilla skill (`skill:swords`, …). | a skill id (yours or vanilla) |
| `castSpell` | **Cast** | none | Weapon casts a spell on hit or on swing. | see Cast |
| *(`statBonuses`)* | object | none | **Also works on weapons** (same block as gear) — e.g. `+CritChance`/`+Health` while held. Put it on `equippable`, not `weapon`. | see Stat keys |
| `trailColor` | string (hex) | none | **Recolor** the swing-trail VFX (vanilla slash shape, new color). | `#RRGGBB` or `#RRGGBBAA`, e.g. `#ff5500` (fire) |
| `trailSheet` | string | none | **Custom-shape** swing trail: a `.png` next to the pack — your own slash art. Combine with `trailColor` to also tint it. | a repainted trail sheet (spec below) |
| `hideTrail` | bool | `false` | **Suppress the swing trail entirely** — for a staff/wand that doesn't melee (no slash effect). Overrides `trailColor`/`trailSheet`. | `true` / `false` |

### Weapon classes (`class`) — slot + animation
| Value | Slot | Animation / note |
|---|---|---|
| `Sword` | Weapon | side swing |
| `Sword` variants via `display` | Weapon | shapes `Sword_leaf`, `Sword_long` |
| `Spear` | Weapon | forward thrust |
| `Dagger` | Weapon | quick stab |
| `Sledgehammer` | Weapon | heavy overhead |
| `Fists` | Weapon | punch |
| `Bow` | Offhand | draw + loose — **needs arrows** |
| `Crossbow` | Offhand | aim + fire — **needs arrows** |
| `Javelin` | Weapon | throw |
| `Shield` | Offhand | (use the `shield` block instead) |
| `SpellTome` | Offhand | the scroll/wand "raise" pose (use the `spellTome` block instead) |

**Damage types** (keys for `minDamage`/`maxDamage`/spell damage):
`true`, `slashing` (alias `slash`), `piercing` (`pierce`), `bludgeoning` (`bludgeon`), `pyro` (`fire`),
`chloro` (`nature`), `aqua` (`water`), `cosmo`, `necro`.

## Cast (weapon `castSpell`) — fire a spell from a weapon
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `spellId` | string | — | The spell to cast. | a spell id (yours or vanilla) |
| `castType` | string | `OnHit` | **When** it casts. | `OnHit` = procs only when the attack **connects with a target** (ignite-on-hit, poison weapons). `OnUse` = casts on **every attack/swing** regardless of hitting — use this for a **STAFF/wand** that launches a projectile each swing (keep a melee `class` for the swing animation). |
| `event` | string | `OnEntityHit` | Which hits trigger an `OnHit` proc. | `OnEntityHit` = only when hitting an **enemy/entity**. `OnAnyHit` = any hit, **including terrain**. |
| `probability` | float | always | **Proc chance** for the cast. | A **fraction** `0..1` (`0.25` = 25% — *not* percent; see [Conventions](#conventions)). Omit = always. |
| `target` | string | `Target` | Who the cast affects. | `Target` = the enemy hit (debuff/ignite). `Self` = **you** — for heal-on-hit / haste-on-hit (lifesteal-style; pair with a beneficial spell/aura). |
| `chargedSpellId` | string | none | A **stronger spell** fired by the **charged/special attack** (hold the attack). | a spell id. Pair with `weapon.specialEnergyCost` + `chargeTime`. |
| `chargeTime` | float | 0.3 | Seconds to charge before the charged spell fires. | >0 |

## SpellTome (equippable `spellTome`) — classic scroll/wand
Equip in **Offhand**; attack casts the spell with the static "raise tome" pose (no swing). For a swinging staff,
use a `weapon` with `castSpell.castType: "OnUse"` instead.
| Field | Type | Default | What |
|---|---|---|---|
| `spellId` | string | — | Spell cast on attack. |
| `target` | string | `Target` | Where the spell lands: `Target` / `Self` / `TargetGround`. **Heal spells need `Self`** (vanilla heal scrolls do this). |
| `chargedSpellId` | string | none | Optional spell for the charged/special attack. |
| `chargeTime` | float | 0.3 | Charge time for the special. |
| `swingTimer` | float | 0.5 | Cast cadence. |
| `energyCost` / `specialEnergyCost` | float | 0 | Energy per normal / special cast. |
| `movementFactor` | float | 0.5 | Move-speed factor while casting. |

## Shield (equippable `shield`)
Equip in **Offhand**.
| Field | Type | Default | What | Range |
|---|---|---|---|---|
| `blockStrength` | float | 0.6 | Fraction of damage blocked. | 0–1 |
| `blockArcDegrees` | float | 60 | Block arc width. | degrees |
| `strongBlockArcDegrees` | float | 50 | Perfect-block arc. | degrees |
| `enterCost` | float | 0 | Energy to raise the shield. | ≥0 |

## Display (equippable `display`) — custom held look
Two modes (overrides `displayId`): **recolor** an existing shape, or inject **your own sprite sheet**.
| Field | Type | Default | What | Valid values |
|---|---|---|---|---|
| `skinName` | string | — / auto | **Recolor:** the existing weapon **shape**. **Custom art:** the new skin's name (auto if blank). | main-hand (`skinTag` 8): `Sword`, `Sword_leaf`, `Sword_long`, `Dagger`, `Spear`, `Hammer`, `Hammer_axe`, `Cestus`, `Bow`; offhand (`skinTag` 11): `Crossbow`, `javelin`. *(No staff/wand shape — `Spear`/`Sword_long` is the closest.)* |
| `skinTag` | int | 8 | Hand slot. | `8` = main-hand, `11` = offhand |
| `palette` | string | `weapon` | **Recolor only:** palette. | `weapon` (also `metal`, `leather`, `fabric`, `details`) |
| `paletteRow` | int | 0 | **Recolor only:** color/material variant. | `weapon` palette rows (16×10): **0** tan · **2** orange-rust · **5** muted/dark-red · **7** orange-gold (*fire*) · **8** olive · **9** blue. **`metal` palette is only 5×7 (rows 0–6!)**: 1 bronze-gold · 2 silver · 3 gray-violet · 4 dark blue-gray · 5 tan-gold · 6 olive-green; an out-of-range row renders the base coppery color. (Others trial-and-error; extract `palettes\weapon.xnb` to see all.) |
| `sheet` | string | none | **Custom art:** a `.png` next to the pack — your own weapon look. | a rig-aligned sprite sheet (spec below) |
| `sheets` | object | none | Multi-sheet rigs: vanilla sheet basename → your PNG (e.g. `"skeletonidle": "bonelord_idle.png"`). The mod **logs the base's sheet names + sizes** on the creature's first appearance — repaint from those. | basename → file |
| `frameWidth` / `frameHeight` | int | 48 | Frame size in the sheet. | main-hand weapons = 48×48 |

**Custom held-art sprite-sheet spec (`sheet`):** the skin is index-locked to the character's animation, so your
PNG must match a vanilla sheet's grid exactly. **Don't draw from scratch — extract the matching vanilla sheet
and paint over it** with the included `tools\xnb2png.ps1` (extraction guide + copyright notes:
[ADVANCED.md — Art extraction & sheet discovery](ADVANCED.md#art-extraction--sheet-discovery)).
Frame size is 48×48; **sheet dimensions depend on the shape**, e.g. main-hand sword/dagger/spear/hammer =
**1104×240** (23×5 frames), crossbow = **96×240**, javelin = **96×32**, shields = **192×240**. Set
`frameWidth`/`frameHeight` if not 48×48. Edit the matching template (keep every sprite's position), save,
point `sheet` at it. Transparent background, no palette. *(Only the held model — the inventory icon is the
separate `icon` PNG.)*

**Custom trail art (`trailSheet`):** the swing trail is a SPAC animation hardcoded per weapon class
(`vfx_swordslash`, `vfx_dagger`, `vfx_spear`, `vfx_sledgehammer`, …). `trailSheet` lets you replace its
*art* without authoring any animation data: the mod **clones the vanilla trail spac your weapon's class
plays and swaps in your PNG**, reusing all of the vanilla timing/offset/direction/animation-name data. So
every attack variant (slash, slash2, whirlwind, stab…) just works — you only repaint the sheet.

- **Keep the frame grid.** Your PNG must match the vanilla trail sheet's cell size and have at least as many
  frames. Extract the trail sheets with the same tool, pointed at the VFX folders (paths:
  [ADVANCED.md — Art extraction & sheet discovery](ADVANCED.md#art-extraction--sheet-discovery)).
  Sword trail = `slash.png` / `slash2.png` (frame 80×48), dagger = `dagger.png`, spear = `spear2.png`,
  sledgehammer = `sledge.png`. Repaint one (keep every frame's position), save, point `trailSheet` at it.
- **Don't know which sheet your class uses?** Swing once in-game — the log prints the exact vanilla sheet
  name + frame size it cloned (`built custom trail 'cf_trail_…' from 'vfx_swordslash' (vanilla sheet '…',
  frame 80x48; your sheet WxH)`). Repaint that one if your first guess was off.
- Transparent background. If a frame index falls outside your sheet it renders blank — make the sheet at
  least as large as the vanilla one (repainting the *bigger* `slash2.png` covers all sword variants).
- Combine with `trailColor` to tint your custom shape on top.

---

# ─ Magic & Spells ─

## Spell

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique spell id. | namespaced |
| `displayName` | string | id | **Literal** spell name (shown in tooltips/on-hit text). | any text |
| `displayNameLocalized` | object | none | **Per-language** spell name: `{ "tr_TR": "...", "de_DE": "..." }`, falling back to `displayName`. | language codes (see Item) |
| `spellTypeId` | string | `spelltype:single_projectile_attack` | The spell **behavior**. You normally **never set this directly** — Content Forge picks it from the blocks you use. | Default = `spelltype:single_projectile_attack` (a forward projectile). `applyAura` → `instant_single_target`; `summon` → `summon_creature`; and each **behavior block** (below + the SpellsPlus section) sets its own: `aoe` → `area_of_effect`, `explosion`, `cone` → `cone_attack`, `line` → `line_attack`, `multiProjectile` → `multiple_projectile_attack`, `heal`, `massHeal` → `mass_heal`. |
| `projectileDisplayId` | string | `arrow_spell` | Projectile visual. | list in REFERENCE — e.g. `arrow_spell`, `fire_spell_new`, `fireball_new`, `poison_bolt`, `projectile_lightning_bolt` |
| `particleTrail` | string | none | Trail emitter. | e.g. `small_fireball_blue`, `small_fireball_flame`, `fireball_flame`, `big_waterball` (REFERENCE) |
| `castSound` | string | none | FMOD cast sound. | e.g. `event:/spells/cast_fireball`, `event:/spells/cast_spell_cosmo`, `…lightning`, `…nature`, `…necro`, `…water` (REFERENCE) |
| `duration` | float | 1.5 | Spell lifetime. | >0 |
| `projectileVelocity` | float | 320 | Projectile speed. | >0 |
| `collisionRadius` | float | 6 | Projectile hit radius. | >0 |
| `attack` | **Attack** | none | The damage. Omit = a harmless projectile / pure aura. | see Attack |
| `applyAura` | string | none | Instead of a projectile: apply an **aura** (DoT/buff) to the target. Great with a weapon `castSpell`. | an `aura:…` id (REFERENCE) — e.g. `aura:general:burning`, `aura:general:weak_poisoned` |
| `chain` | array of **Chain** | none | **Composed effects:** when this spell hits (or ends), **cast other spells** — an arrow that spawns lightning on impact, a fireball that also ignites. | each = `{ event, spell, target }` (below) |
| `summon` | **Summon** | none | Makes this a **summon spell** — spawns a creature that fights for the caster (a necromancer's skeletons). | `{ creature, entityType }` (below) |
| `aoe` / `explosion` / `cone` / `line` / `multiProjectile` / `heal` / `massHeal` | object | none | **Behavior blocks** — set **exactly one** to replace the default projectile behavior. | see [Spell behaviors (SpellsPlus)](#spell-behaviors-spellsplus--casting-requirements) |
| `requires` | **Requires** | none | Gate casting behind skill level / favour / quest / class (red message when blocked). | see [casting requirements](#spell-behaviors-spellsplus--casting-requirements) |

### Chain (spell `chain`) — cast-on-hit
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `event` | string | `OnAnyHit` | When the chained spell fires. | `OnEntityHit` (hit a creature) · `OnTerrainHit` (hit ground/wall) · `OnAnyHit` · `OnSpellEnded` |
| `spell` | string | — | The spell to cast (yours or vanilla). | a spell id |
| `target` | string | `Target` | Where it casts. | `Target` (what was hit) · `Self` · `TargetGround` · `ProjectilePosition` |

```jsonc
// arrow that spawns stunning lightning on impact:
"spells": [
  { "id": "mymod:lightning", "applyAura": "aura:general:stunned" },
  { "id": "mymod:arrow", "projectileDisplayId": "arrow_spell", "attack": { "minDamage": { "piercing": 20 }, "maxDamage": { "piercing": 30 } },
    "chain": [ { "event": "OnAnyHit", "spell": "mymod:lightning", "target": "Target" } ] }
]
```

### Attack (spell `attack`) — damage
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `multiplier` | float | 1 | Damage multiplier (scales with stats). | >0 |
| `skillId` | string | none | A skill that **scales this spell's damage and gains XP** from its hits (e.g. your pack's `skill:runeology`). | a skill id |
| `attackType` | string | `Magic` | Which damage/scaling category. | `Magic`, `Melee`, `Ranged` |
| `canCrit` | bool | true | Can it crit? | true/false |
| `maxTargets` | int | 1 | How many targets it hits. | ≥1 |
| `knockback` | float | 0 | Knockback on hit. | ≥0 |
| `minDamage` / `maxDamage` | object (type→float) | none | Damage range by **damage type** (see list above). | e.g. `{ "pyro": 40 }` |

## Spell behaviors (SpellsPlus) & casting requirements

Beyond the default projectile / `applyAura` / `summon` / `chain`, a spell may set **exactly one** behavior block:

| Block | What it casts | Key fields |
|---|---|---|
| `multiProjectile` | a fan of projectiles (each uses the spell's normal projectile fields) | `count` (3), `spreadDegrees` (24 — the bow multishot) |
| `aoe` | a lingering damage circle (burning pool / poison cloud). **Chain it off a projectile** (`target: "TargetGround"`) for cursor placement — the vanilla fireball pattern. | `radius` (48), `height` (16), `tickSeconds` (0.5), `applyAura` (aura per tick), `lightsource` (true — fire pools glow), `affectsEnvironment` (also damage destructibles), `explosiveKnockback` (knock away from center) |
| `explosion` | an instant blast — ideal chain step. The engine draws its own explosion sized to radius; `projectileDisplayId: "no_vfx"` mutes all visuals. | `radius` (32), `spacVfx` (extra spac anim, e.g. `vfx_explosion1`) |
| `heal` | heals the caster (cast with tome `target: "Self"`) — **server-authoritative**, scaled by HealingDoneModifier | `amount` (instant HP) or `regenAmount`+`regenSeconds` (vanilla regen: 20 over 10) |
| `massHeal` | heals all players in a radius (players only, not summons) — server-authoritative | `amount` or `percentOfMaxHealth` (wins when both set), `radius` (80) |
| `cone` | a melee-style sweep in front of the caster — invisible by itself, pair with `castSound`; keep `duration` short (~0.15–0.5s) *(experimental — no vanilla scroll precedent)* | `range` (48), `angleDegrees` (90), `distanceOffset` (shift forward/back), `tickSeconds` (0 = hit once; >0 = re-hit) |
| `line` | a damaging beam (fire breath) *(experimental; growth needs a `particleTrail`)* | `length` (128), `width` (25), `growSeconds` (0 = instant full length), `tickSeconds` (0.6) |

Damage for all of them comes from the spell's normal `attack` block (use `maxTargets: 99` for areas — the
builder bumps it automatically if you forget).

**`requires`** gates the player's casting of the spell (red message when blocked, nothing spent):

```json
"requires": { "skill": "skill:runeology", "skillLevel": 3,
              "favour": null, "quest": null, "class": null,
              "message": "The runes reject you..." }
```
Any combination; all set conditions must pass. Chained spells aren't re-gated (the parent cast was).

> **Want a melee weapon with a CUSTOM hit shape?** A `cone`/`line` spell can replace the native swing's
> class-fixed cone entirely — recipe: [COOKBOOK.md — A melee weapon with a custom hit shape](COOKBOOK.md#a-melee-weapon-with-a-custom-hit-shape).

---

# ─ Summons ─

## Summon (spell `summon`) — spawn a creature that fights for you
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `creature` | string | — | The creature to spawn. | a name from `reference/creature-ids.txt` — e.g. `JumpingSkull`, `SkeletonWarrior`, `Fallen`, `Wolf` |
| `count` | int | 1 | How many to spawn per cast (e.g. `2` skeletons). | ≥1 |
| `maxActive` | int | 0 | Cap on how many of this summon stay alive — the **oldest despawns** past the limit. `0` = unlimited. | ≥0 |
| `behavior` | string | `guardian` | `guardian` = stays where summoned (its own AI fights what comes near). `companion` = **teleports back to your side** when it has no enemy nearby and you've wandered off, so it keeps up — without the walk-animation glitches that following caused. | `guardian` / `companion` |
| `dismissOnUnequip` | bool | `false` | Despawn this spell's summons when the item that casts it leaves the player's equipment (incl. weapon-set swaps). | `true` / `false` |
| `entityType` | string | `PlayerSummon` | Allegiance/AI type. | `PlayerSummon` (fights for you) · `HostileSummon` · `Hostile` · `Friendly` · `Neutral` |
| `parameters` | object (string→string) | none | **Advanced:** extra spawn parameters passed to the spawned creature's controller (vanilla doodad-style params). Rarely needed. | key → value strings |

An **ally** summon (`entityType` `PlayerSummon`/`Friendly`) **joins the caster's faction** — it fights your
enemies, you can't damage it (friendly-fire protection, no phantom hit numbers), and its projectiles can
never hit you, even flying right past. A **hostile-intent** summon (`Hostile`/`HostileSummon` — e.g. a tome
that spawns a monster to fight) keeps its **own creature's faction**: it aggros you, and your hits land on
it normally. Bind the spell to an Offhand
`spellTome` (a book you raise) or a weapon `castSpell` (`OnUse` = summon each swing); `count` controls how
many spawn per cast. See the full **`examples/necromancer/`** pack for a worked example (book + staff + robe + recipe — walkthrough:
[COOKBOOK.md](COOKBOOK.md#walkthrough-a-necromancer-class-pack)).

### How a creature fights vs. how it follows — two separate things

These are **orthogonal** — a common point of confusion (a `guardian` skull still hunts aggressively):

- **Combat = the creature's own species AI.** Each creature keeps its vanilla brain. A **JumpingSkull** lunges and
  seeks out nearby enemies aggressively; a **Wolf** chases; a **SkeletonWarrior** / **SkeletonArcher** holds and
  fights what comes into range. You pick the *feel* by picking the `creature` — the mod does **not** change how it
  targets or attacks.
- **Allegiance.** The JumpingSkull and Wolf controllers read the summoner's faction natively; **skeletons don't**,
  so the mod force-sets the spawned entity's faction. Either way your summon treats **you and your other summons as
  allies** and won't turn on you (this is why an unpatched skeleton used to attack its summoner).
- **`behavior` = positioning only, not aggression.** `guardian` = stays where summoned (a sentry — fine for a
  roaming, self-hunting skull). `companion` = blinks back to your side when no enemy is near and you've walked off,
  so it keeps up. Facing after a blink is left to the creature's AI (forcing it fought the walk animation).

So **`guardian` does not mean passive** — it means "doesn't follow you." An aggressive skull set to `guardian` will
still chase enemies; it just won't trail you across the map. Want it glued to you? Use `companion`.

Two more behaviors that are **by design**:
- **Summons persist after you unequip/switch the item.** They live until they die or their own spell's `maxActive`
  pushes the oldest out. Each spell's cap is **separate** — summoning from three different books keeps three groups.
- **A skeleton's death spawns a jumping skull** (vanilla mechanic). The mod keeps that skull **on your side** — it
  inherits your faction instead of turning on you.

## Creature (top-level `creatures`) — a custom creature

Clones a vanilla creature's **entity** (its AI, animations, attacks, abilities) under a **new id**, then lets you
override a few things — **name, max health, color tint**. Summon it from a spell by setting `summon.creature` to
your creature's `id` (instead of a vanilla name).

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique creature id — **summon by this**. | namespaced (e.g. `mymod:tough_skeleton`) |
| `base` | string | — | **Which vanilla creature to clone** (AI + art come from it). | a name from `reference/creature-ids.txt` — e.g. `SkeletonWarrior`, `Wolf`, `JumpingSkull` |
| `name` | string | base's | Display name. | any text |
| `nameLocalized` | object | none | Per-language display name (picked by game language). | langcode → text |
| `healthMultiplier` | float | `1` | Scale the base's max health. | `3` = triple health |
| `color` | array `[r,g,b]` | none | Multiplicative **sprite tint** (0–255 each). | e.g. `[180, 120, 255]` for purple |
| `sheet` | string | none | **Repaint the rig**: a PNG beside the pack replacing the base spac's FIRST sprite sheet (1:1 — same size + frame grid). Single-sheet bases only. | a repaint PNG |
| `controller` | string | none | **Custom AI**: swap the clone's SERVER controller — a vanilla controller name (`skeletonarcher`, `wolf`, `boar`, ... — strip the "Server"/"Controller" parts of the class name) or one your addon DLL registered ([ADVANCED.md](ADVANCED.md#custom-controllers-c) — same registry as placeable controllers). The base's CLIENT controller stays, so visuals/animations are unchanged; pick an AI from the same family so the animation states match (an archer brain on a warrior skeleton works; a wolf brain on a skeleton freezes animations). `controllerParams` overrides merge over the base's. Base params are copied automatically. |
| `clientController` | string | none | **Custom client visuals**: swap the clone's CLIENT controller. Set `"forge_creature_anim"` for the data-driven animation controller — it lets a **cross-family** `controller` drive working animations (without it, a wolf brain on a skeleton freezes the anims). Configure via `controllerParams` (`spac` required — the base family's spac id; `anim-map` like `"0=idle,1=walk,2=attack"`; `animmsg-map`, `anim-default`, `anim-walk`, `walk-px-per-sec`, `footsteps`, `anim-death`, `death-time`, `death-size`, `death-sfx`). The log prints the spac's animation names on first spawn. Unset = keep the base's client controller. |

#### Built-in AI presets (no addon DLL needed)

Eight server-AI presets ship with the mod — put them straight into a creature's `controller`. Built from
the game's own behavior-machine pieces (the same wander/chase/threat/spell-cast nodes vanilla skeletons
use). All `controllerParams` values are strings.

| preset | key params (default) | behavior |
|---|---|---|
| `forge_guard` | `guard-radius` (160) · `aggro-radius` (200) · `melee-spell`/`melee-range`/timings (base's) | A post guard: wanders only near its spawn, chases+melees intruders, **breaks off and walks back** when the target leaves the leash. Never roams. |
| `forge_coward` | `flee-threshold` (0.35) · `flee-safe-distance` (260) | Fights normally until health drops below the threshold — then RUNS, regenerates when safe, re-engages healthy. |
| `forge_ranged` | `ranged-spell` (**required**) · `preferred-range` (140) · `min-range`/`max-range` · `attack-cooldown` (2.0) | A kiting caster: keeps its distance band and casts the spell at the target (line of sight required). **Any creature becomes a caster** — base Wolf + `"ranged-spell": "mymod:firebolt"` = a fire-spitting wolf. |
| `forge_patrol` | `patrol-points` (**required** — `"x1,y1;x2,y2;…"` px) · `patrol-relative` (false — points relative to the spawn) · `patrol-pingpong` (false — loop vs. back-and-forth) · `patrol-pause` (1.5 s per point) · `aggro-radius` (200) · melee params (forge_guard's) | A route walker: visits the waypoints in order, pausing at each (even outside the fog of war — that's the point of a route). Aggros + melees like `forge_guard`, then **resumes the route at the NEAREST waypoint**. An unreachable point is skipped, not wedged on. |
| `forge_pack` | `pack-radius` (240) · `pack-threat` (100) · melee params | A pack hunter: standard wander/chase/melee, but the moment it enters combat it **alerts every same-base packmate** within `pack-radius` (their threat tables get its target). Each alerted packmate re-alerts as IT engages, so the call **cascades** through spread-out packs. |
| `forge_summoner` | `minion` (**required** — a pack creature id or an EntityIds name) · `minion-count` (1 per summon) · `max-minions` (3 **alive** at once) · `summon-cooldown` (8 s) · `summon-windup` (0.5 s) · `ranged-spell` (optional filler shot) · `preferred-range` (140) / `min-range`/`max-range` · `attack-cooldown` (2.0) | A kiting conjurer: keeps `forge_ranged`'s distance band, but its main attack **spawns `minion` creatures** near itself. `max-minions` counts only LIVING minions (a dead one frees a slot); `ranged-spell` fires between summons. No valid minion and no spell = it just kites (warns once). |
| `forge_healer` | `ally-radius` (200) · `heal-amount` (10 HP) · `heal-cooldown` (3 s) · `heal-windup` (0.35 s) · `follow-distance` (150 — regroups with its flock past this) · `heal-vfx` (optional spac on the patient) · `heal-spell` (optional cosmetic cast) | A flock medic: wanders with its group, walks back when it strays, and heals the **most injured ally** in range (same base creature, or any hostile creature — never players). The heal is direct and server-side (syncs like any health change). Threatened, it **always flees** — healers are fragile. |
| `forge_turret` | `ranged-spell` (**required**) · `attack-radius` (260) · `attack-cooldown` (1.5 s) · `attack-windup` (0.35) · `attack-lock` (0.3) · `los-width` (6) · `aggro-radius` (defaults to `attack-radius`) | A living ballista/totem: **never moves** (no wander, chase, flee, or leash — though knockback still shoves it). Casts the spell at any threat within `attack-radius`, line of sight required. |

**Animation ints** (`anim-idle`/`anim-walk`/`anim-attack`, defaults 0/1/2): the server drives the client's
animation through small state ints whose meaning is decided by the BASE's client controller — skeleton
bases work out of the box; Wolf has no attack int (use `"anim-attack": "0"`). For every other base, look it
up in **`reference/anim-state-ints.txt`** (the animation ints of 73 vanilla client controllers, mapped —
no more trial-and-error), or pair with `clientController: "forge_creature_anim"` for full control.


```json
"creatures": [
  { "id": "mymod:bone_lord", "base": "SkeletonWarrior", "name": "Bone Lord",
    "healthMultiplier": 3, "color": [170, 110, 255] }
],
"spells": [
  { "id": "mymod:raise_bone_lord", "displayName": "Raise Bone Lord",
    "summon": { "creature": "mymod:bone_lord", "maxActive": 2, "behavior": "companion" } }
]
```

> **What you can change:** name, max health, color tint, a **full rig repaint** (`sheet`/`sheets` — same frame
> grid, your pixels), the **server AI** (`controller` — a vanilla brain or one of the 8 presets above), the
> **client animations** (`clientController: "forge_creature_anim"`), and (via the summon)
> faction/count/cap/behavior. By default the clone reuses the base's AI and animations — a "repainted/elite
> variant" — but combining an AI preset with a
> [from-scratch spac](#from-scratch-spacs-no-base--brand-new-animations-from-your-pngs) rig gives you a
> genuinely **brand-new monster with zero vanilla art**
> ([COOKBOOK.md](COOKBOOK.md#a-fully-custom-creature-from-one-png)). Registered into both the server and
> client entity tables, so it spawns and renders in single-player and co-op (host runs the mod).

---

# ─ World, Maps & Dungeons ─

## Map (top-level `maps`) — a custom dungeon room

Author the room in the **free [Tiled](https://www.mapeditor.org) editor** — no game tools needed. The mod
generates the game's binary world file from your `.tmx`, registers the room as a **handcrafted dungeon**, and
(if `biomes` is set) adds an **overworld entrance POI** that newly-generated worlds roll like any vanilla dungeon.

**Authoring the `.tmx`** (ship it next to your pack json). A room is **two kinds of layers**:
- **Data layers (collision — never drawn):** a tile layer named **`ground`** using the `world_tile_ground`
  tileset = walkable floor data; optionally **`structure`** using `world_tile_structure` = walls (blocks movement).
- **Visible layers (the art — what the player sees):** at least one normally-named layer (e.g. `Base`, `floor`,
  `Details`) painted with a regular tileset — e.g. `dungeon_full.tsx` (the vanilla cave/dungeon art; tile 112 is
  the standard cave floor), `stone_dungeon.tsx`, `greybox.tsx` (prototyping blocks). If you ship no visible layer, **the mod auto-generates one** (`cf_autofloor`:
  the plain cave floor under every `ground` tile) so the room is walkable and visible instead of pitch
  black — a featureless floor, though; paint your own layer for real art. `ground`/`structure` stay data-only.
- Get the tilesets from the game: everything in `Content\tilesets\*.tsx` is a plain Tiled file — add them to
  your Tiled project by reference (keep relative `source` paths; the loader resolves them by name).
- Layer-name conventions the engine honors: layers starting with `OVERLAY` draw above entities (darkness vignettes
  etc.), `top*` draws on top.
- Leave a gap in the wall where the exit box sits (default: bottom-center) so the player can reach it.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique room id. | namespaced (e.g. `mymod:crypt`) |
| `name` | string | id | Display name (dungeon tooltip). | any text |
| `nameLocalized` | object | none | Per-language display name. | langcode → text |
| `tmx` | string | — | The Tiled map file, next to the pack. | a `.tmx` filename |
| `biomes` | string[] | none | Overworld biomes whose POI roll can place the entrance. **Omit = no auto-entrance** (room still registered). | `biome:plains`, `biome:forest`, `biome:desert`, `biome:volcanic`, … |
| `weight` | float | 100 | POI roll weight (vanilla dungeons ≈100; higher = more common). | >0 |
| `icon` | string | `dungeon_skull` | Overworld map icon — uses the game's **map-icon set** (dungeon icons), *not* item icon ids. The default `dungeon_skull` works in-game. | a map-icon id |
| `lootTable` | string | none | Dungeon loot-table id for pre-rolled loot. | a drop-table id |
| `exitDirection` | int | 1 | Which side the overworld entrance faces. | `0`=E `1`=S `2`=W `3`=N |
| `exitNode` | float[4] | bottom-center | `[x, y, w, h]` in px — walking into this box leaves the dungeon. | inside the map |
| `exit` | bool | `true` | `false` = no overworld exit — an **inner room** reached only via doors. | true/false |
| `pvp` | bool | `false` | **PvP zone**: players can damage each other inside this map's world, and dying there spawns **no grave** (no item loss). Outside pvp maps the server rejects player→player damage. Both fighters must run the mod + the host's packs (the join handshake checks this). | true/false |
| `replace` | string | none | **Replace a vanilla dungeon room's interior** with this map: same entrance/POI/name/loot, your room inside. `exitDirection` must match the vanilla room's (the log tells you the value). Fully consistent in dungeons not yet visited. Prefer tiny handcrafted rooms (`animal_den:bear`, `tree_room:chest`, `prison`, …); avoid boss arenas and `cavern_*` challenge rooms. Omit `biomes` unless you ALSO want your own entrances. | a vanilla room id |
| `exteriorMap` | string | `maps/poi/forest/td_animal_den` | The vanilla **exterior map** stamped on the overworld as the entrance (the prop the player walks into). Only matters with `biomes` (an auto-entrance). | a vanilla POI map path |
| `retrofit` | bool | `false` | **Also place the entrance into EXISTING saves** — once per save, on world load (host). The mod searches for a clear spot in one of your `biomes` (structure-free, no entities/crops/roads/farmland, far from the spawn, towns, buildings, players and other entrances) and stamps the exterior there. **Needs `biomes`.** No clear spot found this load = logged, retried next load. A save whose world-gen already rolled the entrance is just marked done. | true/false |
| `doors` | array of **Door** | none | Doors into OTHER rooms of this pack → **multi-room dungeons**. | see below |
| `music` / `ambience` / `musicVolume` / `ambientLight` | — | — | Per-map atmosphere — see [Map atmosphere](#map-atmosphere-fields-on-maps-entries) below. | — |

### Door (a map's `doors` entry)
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `edge` | int | 3 | Which side of THIS room the door sits on. | `0`=E `1`=S `2`=W `3`=N |
| `target` | string | — | The map id the door leads to. | another `maps` id in this pack |
| `node` | float[4] | edge-center | `[x,y,w,h]` px trigger box. | inside the map |

Rules: pair doors on **opposite edges** (A's north door ↔ B's south door); leave wall gaps at both; a door
may **not** share an edge with the room's `exitDirection` (the engine reserves that side for "leave the
dungeon" — the validator errors on it). Inner rooms: `"exit": false` + an `exitDirection` pointing at a
door-free side.

```json
"maps": [
  { "id": "mymod:crypt", "name": "Forgotten Crypt", "tmx": "crypt.tmx",
    "biomes": ["biome:forest"], "weight": 100 }
]
```

**Important notes:**
- **Entrances roll in NEW worlds** — POIs are rolled at world generation (vanilla behavior). To grow the
  entrance in an **existing** save too, set `retrofit: true` (above). The room itself is registered everywhere.
- The player **enters** next to the exit box and **leaves** by walking into it (back out to the overworld).
- The room renders via the game's overworld autotiler — functional dungeon visuals, not the hand-painted
  vanilla dungeon art (those use baked meshes an external editor made).
- **Co-op:** every player needs the pack installed (map files load client-side too).

## Map atmosphere (fields on `maps` entries)

| Field | What it does |
|---|---|
| `music` / `ambience` | Track ids played in this map: a vanilla **track id** (music: `dungeon`, `dungeon_volcanic_additive`; ambience: `cave`, `crypt`, `volcanic workshop`, `pyzifax arena`, `talos arena`) **or** a raw `event:/...` FMOD path (auto-registered — including your own bank's events). Full catalog: `reference/music-track-ids.txt`. An unknown bare id plays the game's "music not available" placeholder. |
| `musicVolume` | 0..1. |
| `ambientLight` | `[r,g,b]` 0..1 — a dark realm is `[0.15, 0.1, 0.2]`. |

## Shop (top-level `shops`) — sell your items at the Market

The Market building's **merchant citizen** sells every shop section in the database whose gates pass — a new
section appears automatically at every market, no per-vendor wiring. Items need a `price` to be buyable
(buy = price × world factor; sell = 20% of price). Buying grants the merchant job XP.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique section id. | namespaced (e.g. `mymod:wares:1`) |
| `items` | string[] | — | Items for sale (yours or vanilla; **must have a `price`**). | item ids |
| `merchantLevel` | int | 1 | Min merchant job level to unlock the section. | ≥0 |
| `biomeCategory` | string | any | Only markets in this biome category offer it. | `plains` / `forest` / `desert` / `volcanic` / … |
| `extraGoodsWorship` | bool | false | Requires the market-extra-goods worship unlock. | true/false |

**`shopAdditions`** appends into an existing vanilla section instead:
```json
"shopAdditions": [ { "shop": "all:1", "items": ["mymod:potion"] } ]
```
Vanilla section ids: `all:1`…`all:6` (any biome, by tier) and per-biome tiers like `plains:1`, `forest:2`.

## Crop (top-level `crops`) — plantable crops

A full farming crop: the mod auto-generates the **seed item** (plant it on farmland), the crop grows through
your **stages**, harvest yields your **drops** — and **farmsteads farm it automatically** (a crop whose drops
include its own seed is fully self-sustaining). Save-safe: uninstalling removes planted crops harmlessly.

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Unique crop id (namespaced). |
| `dryGrowthMinutes` / `wetGrowthMinutes` | float | 40 / 20 | Total growth time unwatered / watered. |
| `stageCount` | int | — | Growth stages. **Must equal `display.stages` length** (validator enforces). |
| `biomes` | string[] | any | Biome **categories** (`plains`, `forest`, `desert`, `volcanic`). |
| `anyDirt` | bool | false | Plantable on any dirt (herbs), not just farmland. |
| `repeatable` | bool | false | Regrows after harvest (grapes/olives style). |
| `cropType` | string | `Plant` | `Plant` / `Mushroom` / `Tree` / `FruitTree` / `Flower` (affects harvest bonuses). |
| `name` | string | — | Informational only (crops have no name UI string — name the **seed** instead). |
| `drops` / `wildDrops` | array | — | `{ item, min, max, chance }` harvest yield (farmed / found wild; `chance` in percent). |
| `display` | object | — | `sheet` (vanilla path, e.g. `media/sprites/flora/wheat_animated`) **or** `file` (your PNG grid); `frameWidth`/`frameHeight` (default 16×32); `offset3D` (whole-crop draw offset — wheat uses `[0,-1,-1]`); `disableRandomFlip` (true = never mirror); `stages: [{ frames, duration, horizontal, offset3D }]` — one per growth stage (`duration` = total animation seconds, 0 = static; `horizontal` = drawn flat on the ground, stage 0 seeds; `offset3D` = per-stage draw offset). **Vanilla sheets store each growth stage as a block of 4 sway frames** — wheat stage 1 = `[0,1,2,3]`, stage 2 = `[8,9,10,11]`, etc. Don't use consecutive single frames (0,1,2 are the *same* stage swaying). |
| `seed` | object | — | The auto-generated seed item: `{ id, name, nameLocalized, description, descriptionLocalized, icon, price, tier, maxStack }` (defaults: price 10, tier 1, maxStack 100, icon `sam`). |

---

## Pre-placed doodads (props in your maps)

A map can pre-place props via `doodads` — vanilla entities OR your own PNGs:

```jsonc
"maps": [{ "id": "mymod:realm", "tmx": "realm.tmx",
  "doodads": [
    { "entity": "CampFire",       "position": [120, 80] },
    { "entity": "Chest",          "position": [200, 90] },
    { "sprite": "gravestone.png", "position": [64, 200],
      "collision": [16, 10],
      "spriteFrames": [0, 1], "spriteFrameDuration": 0.2 }
  ] }]
```

| field | meaning |
|---|---|
| `entity` | A vanilla entity — an EntityIds name (`reference/entity-ids.txt`, e.g. `CampFire`, `Chest`) or a raw Guid — or one of YOUR pack's creature ids / `cloneEntity` placeable ids. The entity behaves exactly like its vanilla self (a campfire burns; `Chest` is an empty storage chest, `LootChestMinor`/`LootChestMajor` roll vanilla loot). **A pack CREATURE id works too and keeps its base's hostility** — a `SkeletonWarrior`-based monster placed this way attacks on sight, its `drops[].creature` loot applies, and (maps regenerate each launch) it respawns next session after being slain. Maps regenerate every launch, so doodad chest contents RESET each session — decoration, not storage. `collision` is ignored here (the entity keeps its own). |
| `sprite` | OR: a PNG next to the pack — the mod builds an inert decor prop from it. Supports the same `spriteFrameWidth`/`spriteFrameHeight`/`spriteFrames`/`spriteFrameDuration`/`spriteOffset3D` fields as custom-sprite placeables. |
| `position` | **Required.** `[x, y]` px from the map's top-left. |
| `collision` | Sprite props only: `[w, h]` px blocking box centered on the position. Omit = pure walk-through decor. |
| `id` | Optional stable id (else derived from the map id + list position). |

Props placed this way are **not demolishable or attackable** (sprite props have no health and aren't
constructions); entity doodads keep their vanilla behavior. Co-op: every player needs the pack; maps
regenerate each launch, nothing persists into saves.

### Visual placement with Tiled (no coordinates needed)

You're already in Tiled — place the props there instead of typing pixel positions:

1. Open your map's `.tmx` in Tiled → **Layer ▸ New ▸ Object Layer**, name it exactly `doodads`.
2. **Insert Point** (P) and click where the prop goes, **or Insert Rectangle** (R) and drag a box.
3. Set the object's **Name** to the entity id (`CampFire`, `Chest`, a Guid, one of your creature /
   `cloneEntity` placeable ids) — or add a custom property `sprite` = a PNG filename next to your pack.
4. Save. Done — every object on the layer becomes a doodad, exactly as if written in the json.

**Where it lands:** a **point** marks the exact spot; a **rectangle** places the prop at the rectangle's
*center*, and for **sprite props** the rectangle's size *is* the collision box — the box you draw is the
box that blocks. Zero-size rectangle or a point = walk-through. Entity doodads always keep their own
vanilla collision (the rectangle only fixes the center). Objects or layers hidden in Tiled (the eye icon)
are skipped — toggle props off without deleting them.

Custom properties (right-click the object ▸ Object Properties ▸ +; all optional, `string` type is fine):

| property | meaning |
|---|---|
| `entity` | Entity id — overrides using the object name (handy when the name is taken as a label). |
| `sprite` | PNG filename next to the pack → an inert sprite prop instead of an entity. |
| `id` | Stable doodad id (else: sprite props use the object name; unnamed ones the list index). |
| `spriteFrames` | csv cell indices for the ambient animation, e.g. `0,1,2,1`. |
| `spriteFrameDuration` | seconds per frame, e.g. `0.2`. |
| `spriteFrameWidth` / `spriteFrameHeight` | frame cell size px (omit = whole PNG is one frame). |
| `offset3D` | csv render offset `x,up,depth`, e.g. `0,-4,0`. |

Tiled object layers and the json `doodads` list combine (json entries first); the game itself ignores
these layers entirely. One rule: keep property values single-line (multiline string properties would
break the game's map renderer).


# ─ NPCs, Dialogue & Citizens ─

## NPC (top-level `npcs`) — a talking character in your maps

A dialogue NPC standing in one of **this pack's `maps`**. You write the conversation as a plain-text
**[YarnSpinner](https://yarnspinner.dev) `.yarn` file** next to the pack — lines, `->` options, `<<Actor>>`
portraits, conditions — and the mod compiles it when the game loads (no tools needed). The NPC body reuses the
game's unkillable neutral-satyr base.

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Unique, namespaced. |
| `map` | string | — | Which of this pack's maps the NPC stands in. |
| `position` | float[2] | — | `[x, y]` px from the map's top-left — optional if the map's tmx places this npc on an `npcs` object layer (see below). |
| `yarn` | string | — | The `.yarn` dialogue file next to the pack. |
| `startNode` | string | `Start` | Node the conversation opens at. |
| `dialogueDistance` | float | 32 | Walk-away distance that closes the window. |
| `actors` | object | none | `<<Actor key …>>` key → speaker display name. |
| `actorsLocalized` | object | none | langcode → (actor key → name) — translated name plates. |
| `translations` | object | none | locale → csv file of `line:id,text` rows (needs explicit `#line:` tags in the yarn). |
| `tint` | int[3] | none | **Unique body**: `[r,g,b]` 0–255 multiplied over the NPC's body sheet (like creature `color`). |
| `sheets` | object | none | **Unique body**: `{"no_weapons": "yourbody.png"}` — a 1:1 repaint of the satyr body sheet. The dialogue-NPC body has exactly **one** sheet, `no_weapons` (`media/sprites/creatures/hostiles/goblin_bandit2/no_weapons`, 48×48 single frame — extract a template: [ADVANCED.md](ADVANCED.md#art-extraction--sheet-discovery)). Either field alone gives the NPC its own look. |

```yarn
title: Start
---
<<Actor mymod_hermit>>
mymod_hermit: Welcome, traveler. #line:hermit_01
-> Who are you?
    mymod_hermit: Just an old hermit.
-> Farewell.
===
```

> **Visual placement with Tiled:** add an object layer named `npcs` to the map's `.tmx`, insert a Point,
> and set its **Name** to the npc's id (e.g. `mymod:hermit`). That position **overrides** the npc's json
> `position` — and if the json omits `position` entirely, the Tiled point supplies it. The npc entry
> itself (with `yarn`, `actors`, …) must still exist in the json: the Tiled object is only the spawn spot.

**Conventions that matter:** the name plate only shows for **speaker-prefixed lines** (`actorid: text`), and the
displayed name is whatever `actors` maps the **actor id** to (`"actors": { "mymod_hermit": "Old Hermit" }`).
Translations require explicit `#line:` tags, and the translated text must keep the speaker prefix.

**Translation CSV rules** (the `translations` sidecar files): save as **UTF-8** — non-ASCII text
(Turkish `ç ğ ş ı ö ü`, etc.) works fine. Each row is `line:id,text` and splits at the **first** comma
or tab only, so commas inside the text are safe as-is; double-quoted values (with `""` escaping) are
also tolerated. A header row starting with `id` is skipped automatically.

> **The GUI editor can do this for you:** the *Edit dialogue…* button on an NPC's (or talking placeable's) `yarn` field opens a dialogue editor that auto-tags translatable lines with `#line:` ids, lints node structure / actor ids / Forge-command arity, and edits the per-locale translation csvs in a grid (speaker prefixes prefilled, coverage tracked) — see `editor/README.md`.

Dialogue can also use the game's own Yarn commands — `<<HandInItem ...>>`, `<<StartNpcQuest ...>>`, and ~25
query functions (inventory amounts, quest state, god worship…). `$g_`-prefixed Yarn variables persist to the
save. Dialogue runs client-side (all players need the pack).

**Forge dialogue commands** (Content Forge additions — the game's own API can check and take, these complete it):

| Command | What it does |
|---|---|
| `<<ForgeGiveItem "item:id" 3>>` | Puts items in the player's bag. |
| `<<ForgeTakeItem "material:coal" 2>>` | Removes items — gate it with `<<if GetInventoryItemAmount("material:coal") >= 2>>`. |
| `<<ForgeHeal 25>>` | Heals over ~5 seconds (the shrine's aura). |

```yarn
-> Have you anything to trade?
    <<if GetInventoryItemAmount("material:coal") >= 2>>
        Take this relic.
        <<ForgeTakeItem "material:coal" 2>>
        <<ForgeGiveItem "mymod:relic" 1>>
    <<else>>
        Bring me two lumps of coal first.
    <<endif>>
```

> Forge commands act through the in-process server — they work in single-player and for the **host** in co-op
> (a non-host client's dialogue can't reach server state; design co-op packs accordingly).

## Citizens (top-level `citizens` / `citizenOverrides`) — town job-workers

A citizen TYPE is a stat template + portrait icon; Content Forge adds arrival weighting, an optional
fixed person name, starting job levels and an optional fixed look.

```jsonc
"citizens": [{
  "id": "mymod:blacksmith_anna",
  "name": "Anna",                          // FIXED name (localizable); omit = random roman names
  "icon": "mymod:anna_icon",
  "stats": { "efficiency": 12, "expertise": 14, "foodCost": 10 },  // six keys; pack-citizen defaults 8/8/0/10/1/1
  "weight": 100,                           // arrival share vs vanilla generic (100). 0 = never arrives
  "jobLevels": { "job:blacksmith": 4 },    // starting job LEVEL (0-4), like legendary wild recruits
  "look": { "male": false, "hair": 5, "hairColor": 10, "eyeColor": 2, "body": 3,
            "tunic": 1, "tunicColor": 4, "shoeColor": 1,
            "displayIds": ["cdd:armour:flamines"] }   // all optional; unset = random roll.
            // hair/tunic = style indexes (valid ranges aren't published — trial-and-error);
            // *Color = palette rows (hair/eyes/fabric/leather);
            // body = skin-tone row (vanilla rolls 0-6); displayIds = worn cdd: looks (yours or vanilla)
}],
"citizenOverrides": {
  "citizen:generic":   { "weight": 50, "icon": "mymod:plebicon" },
  "citizen:blacksmith": { "stats": { "efficiency": 13 } }
}
```

**How they arrive:** every new citizen (a recruited jobless wild wanderer, or the add-citizen cheat)
rolls the type — vanilla generic at weight 100 vs every pack citizen's weight. Skilled wild recruits
(Good/Legendary) **bypass the roll** and keep their rolled craft. A fixed-name citizen **can arrive more
than once** (each arrival rolls the type independently) — keep `weight` low if "Anna" should stay rare.
Want only pack citizens? `"citizenOverrides": { "citizen:generic": { "weight": 0 } }` is valid and stops
generic arrivals entirely (the skilled Good/Legendary recruits still come — they skip the roll).

**Jobs:** `jobLevels` sets the **starting job level only** — it doesn't employ anyone. Assigning the
citizen to a workplace is normal town management (the player does it in-game); the head start shows when
they work that job.
Job ids: job:baker/blacksmith/carpenter/carrier/digger/farmer/leatherworker/lumberjack/merchant/miner/philosopher/potter/priest/sculptor/unemployed.

**Stats** apply at creation; existing citizens keep saved stats. Six stat keys: efficiency, expertise,
happiness, foodCost, experienceGain, loyaltyGain. Unset keys on a **pack citizen** default to
`8/8/0/10/1/1` (in that key order); note vanilla `citizen:generic` itself runs `8/8/0/8/1/1` (foodCost 8)
— baselines per type: `reference/citizen-ids.txt`.

Co-op: server-authoritative (host needs the pack); clients without it see fallback portrait/name/random
look. Uninstall-safe: saved citizens keep working (visuals degrade to fallbacks).

---

# ─ Placeables & Buildings ─

## Placeables (top-level `placeables`; aliases `furniture` / `structures`)

> One section, three names: **`placeables`** is the canonical key — **anything buildable from the construction
> menu** (decorations, shrines, levers, talking stones, portals). `furniture` and `structures` are full aliases
> (same fields, merged at load); use whichever reads best for your content. *(Returning readers: older docs
> called these "furniture Tier 1/2/3" — the three flavors are now simply **basic**, **custom-sprite**, and
> **interactive** placeables.)*

Every placeable appears in the in-game **construction/build menu**; the build-recipe auto-unlocks on login,
so it's always available (no in-game research step). Pick a flavor by what you need:

| You want | Use | See |
|---|---|---|
| Pure decoration, vanilla art | `spawnedId` | [Basic placeables](#basic-placeables-vanilla-art-via-spawnedid) |
| Your own PNG art (optionally animated) | `cloneEntity` + `sprite` | [Custom-sprite placeables](#custom-sprite-placeables-your-own-art) |
| Several PNG layers + collision boxes as ONE building | `parts` | [Composite placeables](#composite-placeables-multi-part-parts) |
| Something that *does* something on interact | `cloneEntity` + `controller` | [Interactive placeables](#interactive-placeables-controller-presets) |

### Basic placeables (vanilla art via `spawnedId`)

The simplest kind: it **reuses an existing vanilla decoration's art** (`spawnedId`) — no custom sprite needed,
so it just works. Build cost, name, icon, footprint and menu entry are all yours.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique placeable id. | namespaced (e.g. `mymod:bone_brazier`) |
| `name` | string | `id` | Name shown in the build menu. | any text |
| `description` | string | none | Tooltip text. | any text |
| `spawnedId` | string | — | **Which vanilla decoration's art to place.** | a name from `reference/decoration-ids.txt` — e.g. `brazier:2`, `monument:0`, `vulcan_statue:0`, `bonfire:1` |
| `icon` | string | `sam` | Build-menu icon. | a vanilla icon id, or your own custom icon id |
| `materials` | object (resourceId→int) | none | Build cost. Empty = free to build. | resource/item ids, e.g. `{ "resource:stone": 5, "material:coal": 3 }` |
| `width` | int | 2 | Tile footprint width — **match the decoration art's real size** (the game can't infer it; a too-small footprint lets things overlap the sprite). | ≥1 |
| `height` | int | 2 | Tile footprint height (same note). | ≥1 |
| `nameLocalized` | object (lang→text) | none | Per-language name. | langcode → text |
| `descriptionLocalized` | object (lang→text) | none | Per-language description. | langcode → text |

```json
"placeables": [
  { "id": "mymod:bone_brazier", "name": "Bone Brazier", "spawnedId": "brazier:2",
    "materials": { "resource:stone": 3, "material:coal": 5 }, "width": 1, "height": 1 }
]
```

### Custom-sprite placeables (your own art)

Ship **your own art** instead of a vanilla decoration: drop a PNG next to the pack and set `sprite`. A
custom-sprite placeable clones a vanilla **entity** as its body (`cloneEntity` — required), then renders your
PNG over it. With no `controller` set it gets the inert `forge_prop` controller automatically (pure decor —
add `controllerParams` `tooltip` for hover text, or combine `sprite` with any interactive preset below).

| Field | What it does |
|---|---|
| `cloneEntity` | **Required.** The vanilla entity whose body to clone — an EntityIds name (e.g. `WorkbenchGuid`) or a raw Guid. Catalog: `reference/entity-ids.txt` (188 names). (Decorations can't be cloned — must be an entity.) |
| `sprite` | A PNG next to the pack — replaces the cloned entity's look. |
| `spriteFrameWidth` / `spriteFrameHeight` | Cell size if the PNG is a frame grid (omit = the whole PNG is one frame). |
| `spriteFrames` | Sheet cell indices in play order (`[0,1,2,3]`) = a looping **ambient animation**. |
| `spriteFrameDuration` | Seconds per animation frame (default 0.15). |
| `spriteOffset3D` | `[x, up, depth]` px render nudge. |

```json
{ "id": "mymod:crystal", "name": "Forge Crystal", "icon": "sam",
  "materials": { "resource:stone": 2 },
  "cloneEntity": "WorkbenchGuid", "sprite": "crystal.png",
  "spriteFrameWidth": 32, "spriteFrameHeight": 48, "spriteFrames": [0, 1, 2, 3],
  "controllerParams": { "tooltip": "It hums faintly." } }
```

### Composite placeables (multi-part `parts`)

A **multi-part building as ONE buildable**: give the placeable a `parts` array and each part is a pack PNG
drawn at its own offset, with a controlled draw order and its **own collision box** — a monument of several
sprites with walkable gaps between them, placed and demolished as a single unit. Save/reload keeps every part.

Each `parts` entry:

| Field | Type | Default | What it does |
|---|---|---|---|
| `sprite` | string | — required | This layer's PNG, next to the pack. |
| `offset` | float[2] | `[0,0]` | `[x, y-up]` px from the anchor, **bottom-anchored** (x right, y up the screen). For `flat` parts it's `[x, depth]` on the ground plane instead. |
| `depth` | float | 0 | Draw-sort bias in px between this composite and **nearby entities** (positive = sorts deeper/behind). A per-part bias, not true per-part world depth. |
| `scale` | float | 1 | Render scale. |
| `opacity` | float | 1 | 0..1. |
| `angle` | int | 0 | Rotation in degrees, clockwise. |
| `flip` | bool | false | Mirror horizontally. |
| `flat` | bool | false | Lay the sprite flat on the ground (rugs, floors, painted shadows). |
| `frameWidth` / `frameHeight` | int | whole PNG | Sheet cell size if the PNG is a grid. |
| `frame` | int | 0 | Which sheet cell to draw. |
| `collision` | float[2] | none | `[w, h]` px box this part blocks. Omit = a walk-through layer. |
| `collisionOffset` | float[2] | `[offset.x, 0]` | `[x, y]` px **world** offset of that box (y = down-screen/south — the ground plane, not the visual y-up). |

Rules worth knowing:
- **List order = draw order** — later parts draw in front of earlier ones.
- **Part 0's collision box sits on the placed entity itself** (that's also what you target to demolish);
  parts 1+ with `collision` become invisible blocker children, spawned and removed with the parent —
  demolishing the composite removes everything.
- `parts` **can't be combined with `yarn` / `station` / `controller`** (one controller slot per entity) —
  a composite is scenery, with no interact prompt.
- The **build ghost previews the clone base's art** (the composite layers appear once it's placed).
  `cloneEntity` is optional (defaults to the standard workbench base).
- The footprint stays the single `width` × `height` tile rectangle, and the whole composite depth-sorts at
  its anchor like any tall single-sprite building.

```jsonc
{ "id": "mymod:monument", "name": "Twin Crystal Monument", "icon": "sam",
  "materials": { "resource:stone": 6 }, "width": 4, "height": 2,
  "parts": [
    { "sprite": "crystal.png", "offset": [-16, 0], "collision": [14, 10] },
    { "sprite": "crystal.png", "offset": [20, 4], "depth": 1,
      "collision": [14, 10], "collisionOffset": [20, 0] }
  ] }
```

### Interactive placeables (controller presets)

Give the entry a `controller` (instead of `spawnedId`) and the construction places a **real interactive
entity** — press E and the effect runs **server-authoritatively** (through the actual game systems, co-op-safe).
The interactive system lives in the mod's `Interactive.cs`.

**What you get out of the box** — five presets, usable from pack JSON with zero C#:

| I want… | Preset | Notes |
|---|---|---|
| a healing shrine / item-giving altar | `forge_shrine` | heal and/or give an item, per-shrine cooldown, vfx/sfx |
| a switch / lever | `forge_lever` | toggles replicated, save-persisted State 0↔1; other mod code can poll it |
| a talking object | `forge_dialogue` | or just set `yarn` — the controller is picked automatically |
| pure decor with hover text | `forge_prop` | inert; pairs with a custom `sprite` |
| a teleporter into one of your maps | `forge_portal` | works from anywhere; the map's exit returns the player beside the portal |

**Fields:**

| Field | What it does |
|---|---|
| `cloneEntity` | The vanilla **entity** whose body/art to clone — an EntityIds name (e.g. `WorkbenchGuid`) or a raw Guid. Catalog: `reference/entity-ids.txt` (188 names). (Decorations can't be cloned — must be an entity.) |
| `controller` | A **Forge preset** (table above — full parameter reference below), a controller name registered by a **C# addon** (see [ADVANCED.md — Custom controllers (C#)](ADVANCED.md#custom-controllers-c)), or even a vanilla controller name (built optimistically; unresolved names warn + skip). |
| `controllerParams` | Controller config, **all values are strings** (`"heal": "25"`). Per-preset keys below. |
| `sprite` (+ `spriteFrame*`, `spriteOffset3D`) | Optional **custom art** for the entity — same fields as [Custom-sprite placeables](#custom-sprite-placeables-your-own-art). |
| `indestructible` | `true` = can't be demolished or destroyed — for quest shrines/temples. (Demolishing normal interactive placeables is clean: no item drops; rebuild from the menu anytime.) |
| `station` | Makes this placeable a **crafting station**: interacting opens a crafting window listing only recipes whose `station` matches this id (e.g. `"my:runeforge"`). Requires `cloneEntity`; the controller defaults to `forge_station`. Comma list (`"my:runeforge,campfire"`) = the window also lists that vanilla station's recipes. Window title/icon come from the placeable's `name`/`icon`. Under the hood this auto-assigns the `forge_station` controller (its only extra `controllerParams` knob: `"distance"`, window auto-close range, default 64). A comma list merges that station's **recipe tabs only** — it does not make your placeable emit light, cook, or act as a heat source. Note: like vanilla, station gating is a client-side convenience — crafting itself is the normal (client-trusted) craft path. |
| `yarn` | **Talking placeable**: a `.yarn` dialogue file next to the pack — interact opens the conversation (controller defaults to `forge_dialogue`). `startNode`, `actors`, `actorsLocalized` work like the NPC fields; Forge dialogue commands work inside. |

**One complete, runnable example per preset** (copy-paste into your pack's `placeables` array):

```jsonc
// forge_shrine — interact: heal 30 HP (10s cooldown per shrine)
{ "id": "mymod:healing_shrine", "name": "Healing Shrine", "icon": "sam",
  "materials": { "resource:stone": 5 },
  "cloneEntity": "WorkbenchGuid", "controller": "forge_shrine",
  "controllerParams": { "heal": "30", "cooldown": "10", "tooltip": "Healing Shrine" } }
```

```jsonc
// forge_lever — interact: toggle a replicated on/off state
{ "id": "mymod:secret_lever", "name": "Secret Lever", "icon": "sam",
  "materials": { "resource:stone": 2 },
  "cloneEntity": "WorkbenchGuid", "controller": "forge_lever",
  "controllerParams": { "tooltip": "Pull me", "message": "Click.", "vfx": "dust_poof_big" } }
```

```jsonc
// forge_dialogue — interact: opens a Yarn conversation (just set `yarn`; the controller is implied)
{ "id": "mymod:talking_stone", "name": "Talking Stone", "icon": "sam",
  "materials": { "resource:stone": 3 },
  "cloneEntity": "WorkbenchGuid", "yarn": "stone.yarn",
  "actors": { "mymod_stone": "The Old Stone" } }
```

```jsonc
// forge_prop — inert decor with hover text (here with vanilla art; add `sprite` for your own)
{ "id": "mymod:relic_stand", "name": "Relic Stand", "icon": "sam",
  "materials": { "resource:stone": 2 },
  "cloneEntity": "WorkbenchGuid", "controller": "forge_prop",
  "controllerParams": { "tooltip": "An empty stand." } }
```

```jsonc
// forge_portal — interact: teleport into one of this pack's maps
{ "id": "mymod:crypt_portal", "name": "Crypt Portal", "icon": "sam",
  "materials": { "resource:stone": 1 },
  "cloneEntity": "WorkbenchGuid", "controller": "forge_portal",
  "controllerParams": { "map": "mymod:crypt", "vfx": "dust_poof_big" } }
```

> Saved worlds containing interactive placeables need the mod installed to load (the save stores the
> controller type — verified working: place → save → reload). All co-op players should run the pack.

#### Controller presets — every `controllerParams` key

**Every preset** (the shared client side) reads these presentation params:

| Key | Type | Default | What it does |
|---|---|---|---|
| `tooltip` | string | none | Hover tooltip text. |
| `tooltip-color` | `#rrggbb` | white | Tooltip color. |
| `message` | string | none | Floating text shown on interact. |
| `message-color` | `#rrggbb` | light green | Floating-text color. |
| `sprite-frames` | csv ints | none | Custom-sprite ambient animation cell indices (`"0,1,2,3"`) — normally set via the `spriteFrames` field, but settable directly too. |
| `sprite-frame-duration` | float (sec) | 0.15 | Seconds per ambient-animation frame. |

**`forge_shrine`** — interact (per placed shrine, with cooldown): heal and/or give an item, vfx/sfx on the shrine. Server-authoritative.

| Key | Type | Default | What it does |
|---|---|---|---|
| `cooldown` | float (sec) | `30` | Seconds between uses (shared per placed shrine). |
| `heal` | float | `0` (off) | Total HP restored — applied as the vanilla healing-over-time aura over 5s (replicated, clamped to max health). |
| `give-item` | item id | `""` (off) | Item added to the interacting player's inventory. |
| `give-amount` | int | `1` | How many of `give-item`. |
| `vfx` | spac id | `dust_poof_big` | VFX played on the shrine for everyone (`""` = off). |
| `sfx` | FMOD event / `cf:sound:` id | `""` (off) | Sound played on the shrine for everyone. |

**`forge_lever`** — interact: toggles controller State 0↔1 (replicated to every client + persisted in the save; other mod code can poll `ServerGameState.Entities[id].ControllerState`).

| Key | Type | Default | What it does |
|---|---|---|---|
| `vfx` | spac id | `""` (off) | VFX played on the lever on every toggle. |

**`forge_dialogue`** — interact opens a Yarn dialogue window (set automatically when the placeable has `yarn`; the framework injects `yarn-path`/`yarn-node` from `yarn`/`startNode` — don't set those by hand).

| Key | Type | Default | What it does |
|---|---|---|---|
| `dialogue-distance` | float (px) | `32` | Walk-away distance that closes the window. |

**`forge_prop`** — inert custom-sprite decor; only the shared presentation params above.

**`forge_portal`** — interact: teleports the interacting player into a pack map (from anywhere — overworld, building, another room). The map's exit node returns them next to the portal.

| Key | Type | Default | What it does |
|---|---|---|---|
| `map` | map id | — **required** | Destination: a `maps` id from a pack (vanilla dungeon-room ids work too). |
| `x` / `y` | float (px) | arrival point just inside the room's exit node (no exit node: `160,160`) | Explicit arrival position inside the map. |
| `vfx` | spac id | `""` (off) | VFX played on the portal for everyone on use. |
| `sfx` | FMOD event / `cf:sound:` id | `""` (off) | Sound played on the portal for everyone on use. |

> **Need custom behavior the presets can't express** (a loot chest, a wave trigger, a puzzle lock…)?
> Write your own controller in a tiny C# addon and use its name in `controller` —
> see [ADVANCED.md — Custom controllers (C#)](ADVANCED.md#custom-controllers-c).

## Town building (top-level `townBuildings`) — a citizen-staffed workshop

A real **town job-building** (the bakery/blacksmith tier — workshops citizens staff and work in), cloned from
a vanilla one. The clone keeps the base's exterior/interior maps, footprint, citizen-slot positions, logistics
and server controller (so the furnace, storage and work-order ticking all stay valid); you re-skin the
**identity and economy** around it: name, icon, build cost, hammering effort, appeal cap, town defence — and
optionally the **job** that staffs it (a bakery-shaped building worked by a blacksmith is fine). It appears in
the build menu's **Buildings** category like any vanilla workshop.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — required | The construction id (namespaced); the building data id becomes `building:<id>`. | e.g. `mymod:grand_bakery` |
| `base` | string | — required | The vanilla building to clone — maps, footprint, slots and controller come from it. | a vanilla BuildingData id: `building:bakery:0`, `building:blacksmith:0`, `building:farmstead:1`, … |
| `name` | string | id | Build-menu name. | any text |
| `nameLocalized` | object | none | Per-language name. | langcode → text |
| `description` | string | none | Build-menu description. | any text |
| `descriptionLocalized` | object | none | Per-language description. | langcode → text |
| `icon` | string | base's | Build-menu/building icon. | a vanilla icon id or your pack icon |
| `materials` | object (id→int) | base's | Construction cost. | e.g. `{ "resource:lumber": 5 }` |
| `buildProgress` | float | base's | Hammering effort to construct it. | >0 |
| `appeal` | float | base's | Max building appeal (the decoration cap). | ≥0 |
| `job` | string | base's | Replace the citizen-slot **job** — the worker type employed inside (a bakery staffed by `job:blacksmith` works; work-order ticking is job-generic). | a job id (list under [Citizens](#citizens-top-level-citizens--citizenoverrides--town-job-workers)) |
| `townDefence` | int | base's | Town defence contribution. | ≥0 |

```jsonc
"townBuildings": [
  { "id": "mymod:grand_bakery", "base": "building:bakery:0", "name": "Grand Bakery",
    "materials": { "resource:lumber": 4, "resource:brick": 2 } }
]
```

**Co-op & uninstall:** building spawn + production are server-authoritative (host-only). Clients **without**
the pack see the building work (the maps are vanilla assets), but its name shows as a raw loc key and its
work recipes as "missing" entries in the job UI — ship the pack to everyone for clean UI. **Demolish placed
pack buildings before removing the pack**: like every interactive placeable, a saved world that still contains
one needs the content to resolve it.

## Work recipe (top-level `workRecipes`) — what citizens produce

A new **work recipe** in a job's work queue (what a baker bakes, what a smith forges), unlocked at a job
level. Recipes are **per-job, not per-building** — adding one to `job:baker` level 1 teaches every bakery in
the world. The producing building pulls `ingredients` from its own storage and puts the output in its output
inventory; completions grant job XP.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — required | Unique recipe id (vanilla convention starts with `recipe:`). | e.g. `recipe:mymod:garum_bread` |
| `job` | string | — required | The job that learns it. | `job:baker`, `job:blacksmith`, … |
| `level` | int | 1 | Job level that unlocks it. | 1–4 |
| `output` | string | — required | The crafted item id (yours or vanilla) — or a construction **resource** id with `resourceOutput`. | an item id |
| `amount` | int | 1 | Produced per completion. | ≥1 |
| `ingredients` | object (item→int) | none | Items pulled from the building's storage. | item ids → counts |
| `resources` | object (resource→int) | none | Construction resources pulled from the building's internal resource storage. | e.g. `{ "resource:lumber": 1 }` |
| `resourceOutput` | bool | false | `true` = `output` is a construction RESOURCE (`resource:…` tier), not an item. | true/false |
| `progress` | float | 30 | Work effort (vanilla bread = 30, armor ≈ 60–150). | >0 |
| `experience` | float | 3 | Job XP per completion. | ≥0 |
| `fuel` | bool | false | `true` = needs the building's furnace lit (bakery/blacksmith have one). | true/false |
| `unrepeatable` | bool | false | `true` = can't be set to repeat in the work queue. | true/false |
| `name` / `nameLocalized` | string / object | the output item's | Display name in the work queue. | any text |
| `description` / `descriptionLocalized` | string / object | none | Tooltip text. | any text |
| `icon` | string | the output item's | Work-queue icon. | an icon id |

```jsonc
"workRecipes": [
  { "id": "recipe:mymod:garum_bread", "job": "job:baker", "level": 1,
    "output": "food:bread", "amount": 2, "ingredients": { "material:flour": 1 },
    "progress": 30, "experience": 5, "fuel": true }
]
```

---

# ─ Quests & Progression ─

## Class (top-level `classes`) — a new playable profession
Appears in the **character-creation picker**. Its whole effect is applied **once at creation** — a skill
head-start + starting gear/clothes/favour. There's no persistent per-class behavior (give it identity through
its starting items, e.g. a custom weapon).

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique, namespaced. | e.g. `mymod:class:necromancer` |
| `name` | string | id | Picker label. | any text |
| `nameLocalized` | object | none | Per-language picker label. | langcode → text |
| `bonusSkill` | string | `skill:swords` | A skill set to **level 5**. **Must be valid** or character creation errors. | `skill:swords`, `skill:spears`, `skill:daggers`, `skill:bows`, `skill:tomes`, `skill:mining`, `skill:woodcutting`, … |
| `skillBonuses` | object (skill→int) | none | Extra starting skill levels. | skill ids → level |
| `startingInventory` | object (item→int) | none | Items you start with (yours or vanilla). | item ids → amount |
| `startingClothes` | string[] | none | Clothing/armor equipped at start. | clothing/armor ids (e.g. `armor:civilian:1`) |
| `startingFavourPoints` | int | none | Favour points to start with. | ≥0 |

> **Hotbar tip:** a class doesn't define hotbar slots. At character creation the game auto-adds only **healing
> usables** (food/potions) from `startingInventory` to the hotbar; **weapons/gear auto-EQUIP** instead (that's why
> vanilla classes feel "complete" — their meat rations fill the hotbar, their axe is equipped). Give your class
> some food (e.g. `"food:meat_small": 5`) or the new character's hotbar starts empty.

```jsonc
"classes": [ { "id": "mymod:class:necromancer", "name": "Necromancer", "bonusSkill": "skill:swords",
  "startingInventory": { "mymod:book_of_dead": 1 }, "startingClothes": ["armor:civilian:1"], "startingFavourPoints": 2 } ]
```

## Quest (top-level `quests`) — a world quest
A **World quest** that **activates automatically** (no NPC/dialogue), tracks its objectives, and can unlock
crafting recipes when finished.

| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Unique, namespaced. | e.g. `mymod:quest_x` |
| `name` / `description` | string | id / none | Quest title + text. | any |
| `nameLocalized` / `descriptionLocalized` | object | none | Per-language title/text. | langcode → text |
| `icon` | string | none | Quest icon. | an icon id |
| `objectives` | array of **Objective** | — | Goals — **all** must be met. | see below |
| `rewardItems` | object | none | Items **granted on completion** (server-side, into the bag): itemId → amount. World quests pay **every online player**; granted once per world (a `repeatable` quest pays only the first time). | `{ "mymod:relic": 2 }` |
| `rewardRecipes` | string[] | none | Crafting recipe ids **unlocked** on completion. | recipe ids |
| `repeatable` | bool | false | Can be done again. | `true`/`false` |

**Objective:** `{ type, target, amount }`.
| `type` | `target` is… |
|---|---|
| `kill` | a creature name (`reference/creature-ids.txt`) — yours or vanilla |
| `collect` / `loot` / `craft` / `harvest` | an item id |
| `handIn` | an item id — the player must **deliver** the items (consumed) |
| `chop` | anything (chop N trees) |
| `build` | a building type id — construct it |
| `equip` | an equipment slot type (`Weapon`, `Helmet`, `Armor`, …) |
| `interact` | a vanilla interaction target: `TownCore` / `Altar` / `Citizen` / `Leatherworker` / `FoodStorage` |

**"Go talk to my NPC" / "go find my temple" — the token pattern.** The engine has no arbitrary
talk-to/discover objective, but it composes from shipped pieces: give the quest a `collect 1` objective for a
**token item**, and have the NPC's dialogue (or the temple's talking stone) hand it over with
`<<ForgeGiveItem "mymod:hermits_blessing" 1>>` — reaching and talking to the target completes the quest.
Dialogue can also gate, start, and finish quests directly (`<<StartNpcQuest>>`, `<<TryFinishQuest>>`,
`IsQuestActive(...)` conditions).

```jsonc
"quests": [ { "id": "mymod:initiation", "name": "Initiation", "description": "Slay 5 Fallen.",
  "objectives": [ { "type": "kill", "target": "FallenSword", "amount": 5 } ],
  "rewardItems": { "mymod:relic": 1 },
  "rewardRecipes": [ "mymod:recipe_book" ] } ]
```
Both reward kinds work together: `rewardItems` grants items directly through the game's reward pipeline
(every online player, once per world), `rewardRecipes` unlocks crafting.

## Skill (top-level `skills`) — a new player skill

Shows in the **skills screen** (own icon/name, localized), levels 0–100 on the shared XP curve, and feeds
favour points like vanilla skills. Train it via your content: a weapon's `skillId` (each hit grants XP **and**
the skill's level scales that weapon's damage) or a spell's `attack.skillId`.

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Keep the `skill:` prefix (e.g. `skill:runeology`). |
| `name` / `nameLocalized` / `description` / `descriptionLocalized` | strings | — | Skills-screen text. |
| `icon` | string | `sam` | Vanilla icon or your custom icon. |
| `value` | float | 0.5 | Per-level effect scalar (damage bonus = value × level/100). |
| `xpGainFactor` | float | 1 | XP multiplier. |

> Co-op note: skill XP is client-trusted (vanilla model); custom skills are backfilled into existing
> characters automatically. Uninstall-safe (a leftover skill shows as a harmless dead entry).

## Favour (top-level `favours`) — a new favour-tree node

Adds a node to the gods' **favour tree**, attached under any existing node (vanilla or one of yours),
costing 1 favour point like every node. Carries **one** effect.

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Required, namespaced (e.g. `mymod:ember_heart`). |
| `name` / `nameLocalized` / `description` / `descriptionLocalized` | strings | — | Tooltip text. With `statBonuses` you can omit the description — the stat lines auto-describe. In a description, `{0}`/`{1}` expand to `flagValue1`/`flagValue2` (avoid other literal `{`). |
| `icon` | string | `sam` | Vanilla icon id or your custom icon. |
| `parent` | string | — | Required: the node to attach under — a vanilla id (`<god>_favour:<name>`, e.g. `minerva_favour:lumber_axe_power`) or another of **your** nodes defined earlier. The node becomes unlockable once its parent is learned. With `screen: true` the parent is **optional** (empty = root node) and must be another `screen` node defined earlier. |
| `position` | `[col,row]` | — | Required tree-grid cell. Vanilla fills columns 0–14 of rows 0–9 — **row 10+ is always free**; pick a cell next to your parent (a connecting line is drawn). Adjacency is **cosmetic** — only the connecting line; the unlock requirement is the **`parent`** link, not the grid cell. A taken cell skips the node with a log warning. With `screen: true` the cell is on the pack screen's **own grid** — start at `[0,0]`, vanilla cells don't matter. |
| `screen` | bool | `false` | `true` = the node lives on the **custom "Pack Favours" fullscreen tree** instead of the vanilla tree (see below). |
| `god` | string | `minerva` | Which god's tab lists it in the skills-window favours view. |
| `statBonuses` | object | — | Permanent stat bonuses while learned — same shape/keys as gear (`"Health": {"additive": 25}`). |
| `aura` | string | — | OR: an existing entity-aura id applied while learned. |
| `recipe` | string | — | OR: a crafting recipe id (yours or vanilla) unlocked while learned. |
| `flagId` + `flagValue1/2` | string+floats | — | OR (advanced): set a vanilla character flag the game's code reacts to. |

**The custom favour screen (`screen: true`).** Nodes with `screen: true` get their **own fullscreen favour
tree** — same look and feel as the vanilla one (black fullscreen, draggable node grid, connection lines,
favour-point counter, click → confirm → unlock for 1 point) — opened by a **"Pack Favours"** button that
appears in the Skills & Favours window (next to Skills / Favours / Forge) whenever any pack defines screen
nodes. All screen nodes from all packs share the one screen, so namespace your grid area if you expect to
coexist. Everything else (effects, costs, strip-on-save, co-op rules) is identical to vanilla-tree nodes;
the vanilla tree simply never shows them. Unlock requirement = the `parent` link (parent learned), checked
client-side exactly like the vanilla tree's reachability.

```jsonc
"favours": [
  { "id": "mymod:attunement", "name": "Forge Attunement", "icon": "anvil",
    "screen": true, "position": [1, 0],                       // root: no parent
    "statBonuses": { "Health": { "additive": 10 } } },
  { "id": "mymod:ember_vein", "name": "Ember Vein",
    "screen": true, "parent": "mymod:attunement", "position": [0, 1],   // child, line drawn to root
    "statBonuses": { "MeleeDamage": { "additive": 5 } } },
  { "id": "mymod:stone_skin", "name": "Stone Skin",
    "screen": true, "parent": "mymod:attunement", "position": [2, 1],
    "statBonuses": { "Armor": { "additive": 15 } } }
]
```

**Uninstall safety (automatic).** The game **refuses login** for a character whose save contains an unknown
favour — so the mod never lets a modded favour reach the disk: at every save the node is stripped, the
point refunded, and an inert `forge:favour:<id>` marker written instead; at login the marker is converted
back into the learned node (and the point re-charged). Remove the pack (or the whole mod) and the save is
plain vanilla: **login keeps working and the spent point is already refunded** — a clean automatic respec.
Re-install the pack and the node is learned again. The canteen respec also works normally.

> Co-op note: favours are **server-authoritative** — the **host** needs the pack for the node to be
> unlockable, and clients need it too (otherwise the tree won't show the node). Don't let mod-less clients
> play characters with learned modded favours: without the mod their saves aren't strip-protected (the
> co-op handshake warns about pack mismatches).

---

# ─ PvP, Arenas & Events ─

## Arenas (top-level `arenas`) — duel events on PvP maps

A duel arena bound to one of your pack's maps. The map must be flagged `"pvp": true` —
the arena is simply that map's world: anyone who walks in can fight.

**Getting players in:** the arena map is entered like any pack map — build a
[`forge_portal` placeable](#interactive-placeables-controller-presets) pointing at it, or give the map
`biomes` for an overworld entrance ([Map](#map-top-level-maps--a-custom-dungeon-room)).

```jsonc
"arenas": [{
  "id": "mymod:colosseum",          // required, namespaced
  "name": "the Colosseum",          // shown in chat lines (default: id tail after ':')
  "map": "mymod:arena_map",         // required: a map in THIS pack with pvp:true
  "announceKills": true,            // chat lines: "A slew B in the Colosseum!"
  "winnerReward": "mymod:champion_belt",  // item handed to the match winner (omit for none)
  "rewardAmount": 1,
  "minPlayers": 2,                  // fighters needed inside before a match starts
  "durationSeconds": 300            // match length; 0 = endless free-for-all
}]
```

**Timed matches** (`durationSeconds > 0`): when at least `minPlayers` players are inside the
arena, a match starts (announced in chat). Kills are counted per player. The match ends when
the timer runs out or only one fighter remains; the winner — most kills, ties broken by whose
last kill landed first, sole survivor if nobody scored — is announced and receives
`winnerReward` x `rewardAmount` directly into their bag. The arena then cools down until the
world is empty, after which it re-arms for the next match.

**Endless arenas** (`durationSeconds: 0`): no matches — just kill announcements, and if
`winnerReward` is set it is paid to the killer on every kill.

Host/server-side only: matches run on the host; co-op clients just see the chat lines.
One arena per map. Kill detection rides on the pvp-zone system, so a non-pvp map gets a
startup warning and a dead arena. Kill credit is exact (the server-simulated killing blow,
DoT-aware) — and a killing blow landed by **your summon credits YOU**, the summoner.

## Events (top-level `events`) — server-side world events

Seven event types run by the host (co-op clients just see chat lines and the spawned enemies).
`creature` values are a pack creature id or a `Shared.EntityIds` name (`reference/creature-ids.txt`)
— the same ids a spell's `summon.creature` takes. Event maps do NOT need `pvp: true` —
except `teamBattle`, which does.

```jsonc
"events": [
 { "id": "mymod:waves", "type": "waveDefense", "map": "mymod:arena",
   "waves": [
     { "creatures": { "FallenSword": 3 },                    "delaySeconds": 10 },
     { "creatures": { "FallenSword": 4, "FallenSpear": 2 },  "delaySeconds": 15 }
   ],
   "reward": "mymod:trophy", "rewardAmount": 1,
   "announceWaves": true, "minPlayers": 1 },
 { "id": "mymod:bossrush", "type": "bossRush", "map": "mymod:arena",
   "bosses": ["FallenCenturion", "Medusa", "Wraith"],
   "reward": "mymod:crown", "minPlayers": 1 },
 { "id": "mymod:hunt", "type": "huntContest", "durationSeconds": 600,
   "target": "Wolf", "schedule": "night",
   "reward": "mymod:hunters_mark", "minPlayers": 2, "cooldownSeconds": 300 },
 { "id": "mymod:raidnight", "type": "scheduledRaid", "schedule": "newDay", "everyNDays": 3,
   "raidData": "raid:satyr", "immediate": false },
 { "id": "mymod:teamfight", "type": "teamBattle", "map": "mymod:arena_map",   // needs pvp:true
   "killsToWin": 10, "reward": "mymod:laurels", "minPlayers": 2 },
 { "id": "mymod:hill", "type": "koth", "map": "mymod:arena",
   "point": [160, 120], "hillRadius": 80, "scoreToWin": 60, "reward": "mymod:crown_of_hills" },
 { "id": "mymod:dash", "type": "race", "map": "mymod:arena",
   "checkpoints": [[64, 64], [256, 64], [256, 192]], "checkpointRadius": 40,
   "timeLimitSeconds": 300, "reward": "mymod:winged_boots" }
]
```

**Event fields:**

| Field | Type | Used by | Default | What it does |
|---|---|---|---|---|
| `id` | string | all | — required | Unique, namespaced. |
| `type` | string | all | — required | `waveDefense` · `bossRush` · `huntContest` · `scheduledRaid` · `teamBattle` · `koth` · `race`. |
| `name` | string | all | id tail after `:` | Name used in the chat lines. |
| `minPlayers` | int | all | 1 (teamBattle: 2) | Players needed (inside the map's world for waves/boss/teamBattle/koth/race; online for hunt/raid). |
| `reward` / `rewardAmount` | item id / int | all but scheduledRaid | none / 1 | Item paid out (waves/boss: every survivor; hunt/koth/race: the winner; teamBattle: every winning-team member still online). |
| `map` | map id | waveDefense, bossRush, teamBattle, koth, race | — required | One of this pack's maps (does **not** need `pvp: true` — except `teamBattle`, which requires it). |
| `waves` | array | waveDefense | — required | In order: `{ "creatures": { "<name>": n }, "delaySeconds": s }` (`delaySeconds` default 10 — breather before the wave). |
| `announceWaves` | bool | waveDefense, bossRush | `true` | Chat line per wave/boss. |
| `bosses` | string[] | bossRush | — required | Creature names, fought one at a time (next spawns on kill). |
| `target` | creature | huntContest | none | The creature to hunt — **omit = any creature kill counts**. |
| `durationSeconds` | int | huntContest | 600 | Contest length. |
| `cooldownSeconds` | int | huntContest | 300 | Schedule-less hunts only: pause between contests. |
| `schedule` | string | huntContest, scheduledRaid | hunt: none · raid: `newDay` | `newDay` (~05:00) · `night` (22:00) · `day` (night's end). Hunt with **no schedule = runs whenever `minPlayers` are online** (re-arming after `cooldownSeconds`). |
| `everyNDays` | int | scheduledRaid | 1 | At most one raid per N in-game days. |
| `raidData` | raid id | scheduledRaid | — required | A vanilla raid id — `reference/raid-ids.txt` (e.g. `raid:satyr`). |
| `immediate` | bool | scheduledRaid | `false` | `true` = war-declaration style: short approach, the raid marches at once instead of the normal delayed approach. |
| `killsToWin` | int | teamBattle | 10 | Team kills needed to win. |
| `point` | float[2] | koth | — required | Hill center `[x, y]` in **map px** (the same space as doodad/NPC positions). |
| `hillRadius` | float | koth | 80 | Hill radius in px. |
| `scoreToWin` | int | koth | 60 | Hill-**seconds** needed to win. |
| `checkpoints` | array of `[x,y]` | race | — required | The checkpoints, **in order**, in map px. |
| `checkpointRadius` | float | race | 40 | A checkpoint counts as passed within this many px. |
| `timeLimitSeconds` | int | race | 300 | The race aborts if nobody finishes in time. |

**waveDefense / bossRush** bind to one of your pack's maps: when `minPlayers` players stand in a
world of that map, the run starts and waves spawn in a ring around the players (3–6 tiles out).
Clear the last wave and **everyone still inside** gets the reward. If the world empties mid-run the
event aborts, despawns its leftovers, and re-arms.

**huntContest** is global (any world). Most kills wins — ties go to whoever reached their score
first. Kill credit comes from the game's own quest kill-credit event (kills your pets/DoTs finish
may not credit). Player deaths never score.

**scheduledRaid** fires a vanilla raid at the town nearest an online player — at most one per
`everyNDays`. Vanilla raid ids: `raid:rats` `raid:potmen` `raid:grey_satyr` `raid:fallen:1`
`raid:pyzifax` (T1) · `raid:satyr` `raid:gorgons` (T2) · `raid:cyclopelli` `raid:fallen:3` (T3).

**teamBattle** needs a `pvp: true` map. When `minPlayers` (≥2) stand inside, the players split into **two
teams** — alternating by join order, so the split stays balanced — and the rosters are announced in chat.
Kills come from the same exact-attribution feed as arenas; first team to `killsToWin` wins, and **every
winning-team member still online** gets the reward. Players walking in mid-battle join the smaller team;
if everyone else leaves, the remaining fighter's team takes it. While a battle runs the server **swallows
same-team damage** — but a teammate-attacker's own screen may still flash a predicted hit that never
applies or relays (a known cosmetic artifact: the client-side gate can't know teams). A teammate downed by
a mob doesn't score for anyone.

**koth** (king of the hill) works on any map (pvp optional): a hill at `point` with `hillRadius`. The
**sole** player on the hill earns 1 point per second — two or more on it is *contested* and nobody scores
(on a pvp map, fighting settles it). First to `scoreToWin` hill-seconds wins + reward. Lead changes are
announced (throttled to one per ~10s).

**race** — when `minPlayers` stand in the map's world, a 10-second countdown announces, then GO: each
player must pass within `checkpointRadius` of every `checkpoints[i]` **in order** (positions are polled
every tick, so sprinting can't skip one). First through the last checkpoint wins + reward;
`timeLimitSeconds` aborts a race nobody finishes.

For all three: host-only like every event; progress/scores are keyed to the **session** player id, so a
player who relogs mid-event starts that event over; the server emptying aborts the event back to idle.

Schedules: `newDay` = dawn after the day changes (~05:00), `night` = nightfall (22:00), `day` = night's end.
Every event also takes a `name` (used in the chat lines; defaults to the id's tail after `:`).

---

# ─ Visuals, Audio & Animation ─

## Palette (custom dye ramp)
| Field | Type | What it does | Valid values |
|---|---|---|---|
| `id` | string | Palette id; use it as `display.palette`. | namespaced, e.g. `acme:fire` |
| `sheet` | string | PNG color ramp next to the pack. | see below |

**How a palette works:** the swap reads a small image where **row 0 = the source/key colors** of the sheet
you're recoloring and **each lower row = the replacement colors** for that variant. To recolor a vanilla
held weapon, **start from the vanilla `weapon` palette and add a row**: extract it with the art tool
([ADVANCED.md](ADVANCED.md#art-extraction--sheet-discovery)), which gives `weapon.png` (16×10 —
rows are the material variants named in the [Display](#display-equippable-display--custom-held-look)
table: 0 tan … 7 orange-gold (*fire*), 9 blue). Append your own row of colors (keep row 0 unchanged), save,
and reference it with `display.palette: "acme:fire", paletteRow: 10` (the new row's index). Your row's colors
must sit in the **same columns** as the keys they replace.

## Vfx (custom sprite-animation effect)
| Field | Type | Default | What it does | Valid values |
|---|---|---|---|---|
| `id` | string | — | Register id. A **new** id (trigger it via weapon `hitVfx`, explosion `spacVfx`, placeable `vfx` params) **or** a **vanilla vfx id to override/reskin** (visible immediately). | e.g. `acme:burst`, or `blood_explosion` to override |
| `sheet` | string | — | PNG sheet of frames next to the pack (a grid of cells). | a frames sheet |
| `frameWidth` / `frameHeight` | int | 48 | Cell size in the sheet. | >0 |
| `frames` | int[] | — | Sheet **cell indices** in play order (row-major). | e.g. `[0,1,2,3,4,5,6,7]` |
| `frameDuration` | int | 3 | Ticks each frame shows (60 ticks/sec). | ≥1 |
| `scale` | float | 1 | Render scale. | >0 |

Two ways to trigger one: register it under a **vanilla effect id** so the game plays your art automatically
(e.g. `id: "blood_explosion"` → shown on creature death), or register a **new id** and point a trigger at it —
a weapon's `hitVfx`, an explosion's `spacVfx`, an interactive placeable's `vfx` param. Extract vanilla vfx
sheets to trace ([ADVANCED.md](ADVANCED.md#art-extraction--sheet-discovery)); cells are usually 48×48.

## Particles & Emitters (top-level `particles` / `emitters`)

Custom particle effects. A **particle** = one sprite (your PNG or a vanilla sheet) + optional behaviors
(animation, fade, gravity, friction, attached light, HSL drift) — each block you include enables that
behavior (omit it and the particle doesn't pay for it). An **emitter** = which particles to spew (weighted;
yours or vanilla ids like `dust:small`), how often, from what area, with what velocity/rotation/acceleration.

Use an emitter id anywhere the game takes one: a spell's **`particleTrail`**, projectile displays, etc.

**Particle fields:**

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Required, namespaced (e.g. `mymod:ember`). |
| `texture` | string | — | Your PNG sheet next to the pack (a grid of frames) — **or:** |
| `vanillaSheet` | string | — | Reuse a vanilla sheet by path (e.g. `media/particles/dust3`). |
| `frameWidth` / `frameHeight` | int | 8 | Cell size in the sheet. |
| `frames` | int[] | none | Cell indices in play order (`[0,1,2,…]`) = animated. Omit = static frame 0. |
| `frameDuration` | float | 0.06 | Seconds per frame. |
| `loop` | bool | false | Loop the animation (else it plays once). |
| `removeOnFinish` | bool | true | Animated only: despawn when the animation ends. |
| `color` | float[4] | white | `[r,g,b,a]` 0..1 tint — values **>1 = HDR glow**. |
| `emissiveness` | float | 0 | 0..1 self-illumination. |
| `vertical` | bool | true | Billboard standing up (`false` = flat on the ground). |
| `maxLife` | float | 1.5 | Seconds before despawn (the animation may remove it earlier). |
| `fade` | object | none | Alpha fade: `{ "type": "fadeOverTime"/"fadeOut"/"fadeIn"/"fadeInAndOut", "time": 0.5 }` (seconds). |
| `gravity` | float | none | Gravity scale (`1` = normal fall; omit = none). |
| `friction` | object | none | Velocity damping: `{ "ground": …, "air": … }`. |
| `light` | object | none | Attached light: `{ "radius": px, "color": [r,g,b] 0..1 }`. |
| `hslDrift` | object | none | Hue/sat/lum shift: `{ "h", "s", "l" }` initial offset + `{ "hDrift", "sDrift", "lDrift" }` per-second drift. |

**Emitter fields:**

| Field | Type | Default | What it does |
|---|---|---|---|
| `id` | string | — | Required, namespaced (e.g. `mymod:ember_fountain`). |
| `particles` | array | — | Required: `[{ "id": "<particle, yours or vanilla>", "weight": 100 }]` — weighted pick per spawn. |
| `spawnFrequency` | float | 0.05 | **Seconds between spawn ticks** (≤0 = every frame — don't). |
| `emitterLifetime` | float | -1 | Seconds before the emitter stops (`-1` = forever). |
| `amountSpawned` | int | 1 | Particles per spawn tick. |
| `offset` | float[3] | none | `[x,y,z]` px offset from the emitter position (z = up). |
| `spawnArea` | object | a point | `{ "type": "point"/"cube"/"cylinder"/"line"/"sphere", "radius": px (cylinder/sphere), "size": [x,y,z] (cube/line; cylinder height = size[2]) }`. |
| `velocity` | object | none | Initial velocity: `{ "min", "max" }` px/sec along `"direction"` (default `[0,0,1]` = straight up). |
| `rotation` | object | none | Initial sprite rotation span: `{ "minDeg", "maxDeg" }`. |
| `acceleration` | object | none | Constant acceleration: `{ "min", "max" }` px/sec² along `"direction"` (default `[0,0,1]`). |

```json
"particles": [ { "id": "mymod:ember", "vanillaSheet": "media/particles/dust3",
  "frameWidth": 16, "frameHeight": 16, "frames": [0,1,2,3,4,5,6,7,8,9],
  "color": [1, 0.4, 0.1, 1], "fade": { "type": "fadeOut", "time": 0.3 },
  "light": { "radius": 16, "color": [1, 0.5, 0.2] } } ],
"emitters": [ { "id": "mymod:ember_trail",
  "particles": [ { "id": "mymod:ember", "weight": 100 } ],
  "spawnFrequency": 0.03, "spawnArea": { "type": "sphere", "radius": 3 },
  "velocity": { "min": 4, "max": 12, "direction": [0,0,1] } } ],
"spells": [ { "id": "mymod:fire_arrow", "particleTrail": "mymod:ember_trail", ... } ]
```

> The particle budget is global (50k) — keep `spawnFrequency` ≥ 0.01s (the validator warns).

## Spacs (top-level `spacs`) — clone + reskin/retime a vanilla sprite animation

```json
"spacs": [ { "id": "mymod:bigslash", "base": "vfx_swordslash",
             "sheets": { "vfx_swordslash": "myart.png" },
             "frameDurationScale": 1.5, "frames": [0,1,2,3] } ]
```

Registers a NEW spac cloned from `base`: same animations/offsets/triggers, your sheets. Wrong `sheets`
key once → the log prints the real sheet names + frame sizes (repaint same grid). `frameDurationScale`
multiplies every frame's duration (2 = half speed); `frames` (optional) replays the base frames in your
order — for simple one-animation vfx spacs. Frame-attached VFX triggers move with their frames. The new
id works anywhere a vanilla spac/vfx id is accepted (weapon `hitVfx`, explosion `spacVfx`, …). New ids
only — to repaint a vanilla effect in place use `vfx`.

### From-scratch spacs (no `base`) — brand-new animations from your PNGs

A `spacs` entry **without `base`** builds a brand-new animation from scratch: your PNG sheet(s) + named
frame lists, no vanilla art anywhere. The new id works **(a)** anywhere a spac/vfx id is accepted (weapon
`hitVfx`, explosion `spacVfx`, …) and **(b)** as a custom creature's **entire rig** via
`forge_creature_anim`'s `spac` param — a fully new monster from one PNG (recipe:
[COOKBOOK.md](COOKBOOK.md#a-fully-custom-creature-from-one-png)).

```jsonc
"spacs": [
  { "id": "mymod:wisp_anims", "directions": 1,
    "sheets": { "wisp": "wisp.png" }, "frameWidth": 32, "frameHeight": 32,
    "animations": {
      "idle":   { "frames": [0, 1], "tickLength": 12, "loop": true },
      "attack": { "frames": [2, 2], "tickLength": 4 }
    } }
]
```

| Field | Type | Default | What it does |
|---|---|---|---|
| `directions` | int | 1 | Facings: `1` (no facing), `4` or `8`. With directions > 1, **each sheet ROW is one direction** — row order `4`: E, S, W, N · `8`: E, SE, S, SW, W, NW, N, NE (the game's int-direction convention). |
| `sheets` | object (name→PNG) | — required | The sheet PNG(s) next to the pack. One is normal; multi-sheet spacs name which sheet each animation draws from (the vanilla one-sheet-per-animation pattern). |
| `frameWidth` / `frameHeight` | int | whole PNG | Cell size — the PNG is cut into a grid. Center the art in each cell. |
| `animations` | object (name→anim) | — required | Named animations (below). For a creature rig, name them `idle` / `walk` / `attack` / `death` and `forge_creature_anim` plugs in with zero extra config. |

Each `animations` entry:

| Field | Type | Default | What it does |
|---|---|---|---|
| `frames` | int[] | `[0]` | Cell indices in play order. `directions: 1` = **absolute** cell indices (row-major, anywhere in the grid); `directions` > 1 = **column** indices within each direction's row. |
| `tickLength` | float | 8 | Ticks each frame shows (60 ticks = 1 second; `8` ≈ 7.5 fps). |
| `loop` | bool | false | **Advisory only** — looping is decided where the spac is *used*: playback by vfx id is always one-shot; `forge_creature_anim` uses its `anim-map` `:loop`/`:once` flags + the idle/walk/run name heuristic. Set it to document intent. |
| `sheet` | string | the first sheet | Multi-sheet spacs: which `sheets` key this animation draws from. |

Notes: the **vfx id plays the FIRST listed animation**, one-shot. From-scratch sprites render with **flat
lighting** (the engine's neutral normal map — no normal-mapped relief). The log prints the registered
grid + animation names; an id that already exists (vanilla or another pack) is skipped with a warning.

## Player animations (top-level `playerAnimations`) — retime the rig (visual)

```json
"playerAnimations": { "attack1": { "frameDurationScale": 2 } }
```

Retimes one player animation state (`attack1/2/3`, `dagger_attack`, `hammer_attack1/2`, `run`, `idle`,
`bow_pull`, … — 47 states; `frameDurationScale` must be > 0 and ≠ 1). 2 = half speed. Frame ORDER is untouched, so frame-keyed gameplay (spear
cast at frame 2, per-frame VFX) stays correct — just retimed. Visual-only and client-side: pair with
`swingTimer`/`comboSteps.swingTimerScale` if you want gameplay speed to match. Citizens untouched.

## Sounds (top-level `sounds`) — drop-in audio files

No FMOD Studio needed: put a `.wav` / `.ogg` / `.mp3` / `.flac` next to your pack json and map it:

```json
"sounds": { "ding": "ding.wav", "boom": "explosion.ogg" }
```

Each entry becomes the id **`cf:sound:<namespace>:<name>`** (e.g. `cf:sound:mymod:ding`) and works
anywhere the game takes a sound event id — spell `castSound`, placeable `controllerParams` `sfx`,
portal `sfx`, ... Vanilla `event:/...` ids keep working unchanged.
**`<namespace>` is the pack's `namespace` field, not the pack `id`** (they differ if you set both).
Sounds entries have **no volume/pitch/loop knobs** — the file plays as-authored (bake loudness into
the file; for loops/parameters use an FMOD bank).

- Played through the game's own FMOD engine, routed into the **sound effects** volume slider.
- Positional ids (castSound, shrine sfx) are 3D: full volume within ~16 px, fading out by ~640 px.
- Keep one-shots short; files load whole into memory (compressed) on first play. For full music
  tracks or authored loops/parameters, use a real FMOD `.bank` instead — authoring guide:
  [ADVANCED.md — FMOD banks](ADVANCED.md#fmod-banks).
- A missing/broken file logs **one** warning with the id and plays nothing — it never crashes.

---

# ─ Player Looks & Rigs ─

## PlayerSkins (top-level `playerSkins`) — the player's own look

| Key | What it does |
|---|---|
| `reskins[]` | `{tag, part, sheet, sheet2}` — 1:1 repaint of a vanilla character part. `tag` ∈ `hair`, `eyes`, `tunic`, `armour`, `shoulders`, `decoration`, `sleeves`, `legwear`, `footwear`, `headwearBack`/`headwearMiddle`/`headwearFront`, `back`, `shield`; `part` = the game's file name (e.g. `Braid`, `Pants_metal`); the sheet must match the vanilla pixel size. `sheet2` = the `-2` facing variation. |
| `hair[]` | `{name, sheet, sheet2}` — a **new hair style in character creation** (recolors through the hair palette). Sheets: 2640×240 + 2688×240, 48×48 cells. Append-only across pack versions (saved as an index). |
| `parts[]` | `{tag, name, sheet, sheet2}` — a new worn part for `looks` to draw. |
| `looks[]` | `{id, fragments[], hideHair}` — a worn appearance; items wear it via `equippable.displayId`. Fragment = `{tag, part, palette, row}` (dye through any palette, vanilla or your pack's). `hideHair: true` for helmets. |
| `rig` | `{sheets, tint}` — repaint the player **body itself** (the animated base rig, not worn parts). `sheets`: `{"<vanilla sheet basename>": "mybody.png"}` — 1:1 repaints of the player animation sheets (same size, same frame grid). `tint`: `[r,g,b]` 0–255 multiplied over the body sheets at load (works alone — no art needed). One rig globally (last pack wins). |

**Discovering the body sheet names + extracting repaint templates:** see
[ADVANCED.md — Art extraction & sheet discovery](ADVANCED.md#art-extraction--sheet-discovery). The rig applies
to **all** player characters this client renders (you and co-op partners); citizens and the
character-creation/select previews keep vanilla art; unmodded co-op peers see vanilla bodies.

Body-type (skin) reskins aren't supported (multi-slice). Co-op: peers without the pack see no-draw for
unknown hair (no crash) and a logged warning for unknown looks.

---

# ─ Co-op & Safety ─

## Co-op & saves
- Modded content is **server-authoritative**: the **host** must run the mod (ideally all players, with the same packs, so visuals/icons match). Against an unmodded host, modded content isn't present.
- **Join handshake:** when a modded client joins, it reports its pack list to the host — mismatched/missing packs get a chat warning, and unmodded clients are gated out of PvP maps.
- **Save-safe — uninstall semantics per content type:**

| Content | Pack removed (mod kept) | Whole mod removed | Reinstall |
|---|---|---|---|
| Items / gear | saved instances become harmless placeholders | same — the loader tolerates unknown item ids exactly like pack removal | items are real again |
| Skills | a harmless dead entry in the skills screen | same | accumulated levels **return** — level data is keyed by id and never deleted |
| Favours | never reach the save: auto-strip at save + point refund ([Favour](#favour-top-level-favours--a-new-favour-tree-node)) | same — the save is plain vanilla. (Favours are the only category the game would *reject*; the strip-on-save safety means the save never holds one.) | node re-learned, point re-charged |
| Crops | planted crops removed harmlessly | same | replant |
| Interactive placeables | world loads as long as the **mod** stays installed | **the one exception:** a world containing one needs the mod installed to load (the save stores the controller type) | loads again |
| Town buildings | **demolish placed pack buildings first** — a saved world still containing one can't resolve its building/construction data | same | resolves again |

---

## Mod settings (`RomesteadContentForge.cfg`)

Player-side options (not pack content) in `BepInEx\config\RomesteadContentForge.cfg`, created on first
launch. `[Notifications]` controls where the mod's announcements (events, arena, handshake, two-handed
ejects) appear:

| Key | Default | What |
|---|---|---|
| `Style` | `banner` | `banner` = big text top-center · `toast` = the vanilla notification window (bottom-right). |
| `BannerColor` | `235,60,50` | Banner text color, `R,G,B` (0–255). |
| `BannerYPercent` | `16` | Banner vertical position, % of screen height from the top (0–80). |
| `BannerSeconds` | `3.5` | How long a banner stays (1–15; the last 0.5 s fades out). |

---

# ─ Tooling ─

## Validate
Catch mistakes (bad enum values, duplicate ids, broken refs, missing files, un-namespaced ids, missing
dependencies) **before** launching. Two ways, same checks:
- **CLI:** `python editor/validate.py <pack.json or folder>` — cross-platform, no .NET; takes a **single pack
  file or a whole packs folder** (it detects which).
- **GUI:** click **Validate** in the GUI editor (runs in-process, checks your current unsaved edits).

Errors mean it won't work; warnings are likely mistakes. (The mod also skips bad packs at load and logs why —
the validator just tells you up front.)
