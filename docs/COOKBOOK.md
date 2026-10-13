# 🍳 Content Forge — Cookbook

**[« Docs index](../README.md)**

_What this is: copy-paste recipes, organized by goal — paste, rename the namespace, tweak. What this isn't: the
field spec (see [PACKS.md](PACKS.md)) or a tutorial (see [GETTING_STARTED.md](GETTING_STARTED.md)). Docs index:
[README.md](../README.md)._

Every recipe is a working pack fragment. Field meanings and valid values live in [PACKS.md](PACKS.md) — each
recipe links the sections it uses. For complete shipping-ready packs, see the two
[walkthroughs](#walkthrough-a-necromancer-class-pack) at the bottom and `examples/`.

> **Fragments cross-reference each other:** some recipes reuse spells defined in a neighboring recipe (the
> [Ember Maul](#a-heavy-weapon--stagger-charged-attack-stat-bonus-ember-maul) casts `mymod:ignite` from the
> [Flamebrand](#a-flaming-sword-that-ignites-on-hit-flamebrand) and `mymod:fireball` from the
> [Firestaff](#a-staff-that-casts-fireballs-firestaff)) — copy the referenced spells too, or ship both fragments.

## Contents

**Weapons & spells** — [Flaming sword (ignite on hit)](#a-flaming-sword-that-ignites-on-hit-flamebrand) ·
[Staff that casts fireballs](#a-staff-that-casts-fireballs-firestaff) ·
[Classic wand / scroll](#a-classic-wand-or-scroll) ·
[Two-handed staff](#a-two-handed-staff) ·
[Custom sword art in 3 commands](#custom-sword-art-in-3-commands) ·
[Weapon with a custom hit shape](#a-melee-weapon-with-a-custom-hit-shape) ·
[Chained spell (Stormbow)](#a-chained-spell--arrow-that-spawns-lightning-stormbow) ·
[Heavy weapon, full stats](#a-heavy-weapon--stagger-charged-attack-stat-bonus-ember-maul) ·
[Poison daggers, lifesteal & frost armor](#poison-daggers-lifesteal--frost-armor)
**Loot & looks** — [Drop your item from a mob](#drop-your-item-from-a-mob) ·
[Dyed / recolored gear](#dyed--recolored-gear) ·
[Reskin a hit effect (custom VFX)](#reskin-a-hit-effect-custom-vfx) ·
[Multi-language names](#multi-language-names--descriptions)
**Placeables & town** — [A healing shrine](#a-healing-shrine) · [A portal to your own world](#a-portal-to-your-own-world) ·
[Custom crafting station](#a-custom-crafting-station) ·
[A talking gravestone](#a-talking-gravestone) ·
[A multi-part monument](#a-multi-part-monument-composite) ·
[A custom town workshop](#a-custom-town-workshop)
**Creatures** — [A fully custom creature from one PNG](#a-fully-custom-creature-from-one-png)
**Progression** — [Your own favour tree screen](#your-own-favour-tree-screen)
**Walkthroughs** — [A themed world pack (worldoftales)](#walkthrough-a-themed-world-pack-worldoftales) ·
[A necromancer class pack](#walkthrough-a-necromancer-class-pack)

---

## A flaming sword that ignites on hit (Flamebrand)

Fire sword that ignites on hit, with a fiery trail:
```jsonc
"spells": [ { "id": "mymod:ignite", "displayName": "Ignite", "applyAura": "aura:general:burning" } ],
"items": [
  { "id": "mymod:flamebrand", "name": "Flamebrand", "icon": "sword:iron", "tier": 6,
    "equippable": { "equipmentType": "Weapon", "display": { "skinName": "Sword_long", "paletteRow": 7 },
      "weapon": { "class": "Sword", "minDamage": { "pyro": 60 }, "maxDamage": { "pyro": 90 },
        "castSpell": { "spellId": "mymod:ignite", "castType": "OnHit" },
        "trailColor": "#ff5500", "trailSheet": "fire_trail.png" } } }
]
```
`trailColor` recolors the vanilla slash; `trailSheet` replaces its *shape* with your repaint (omit it to keep
the vanilla slash shape and just tint). See **Custom trail art** under
[PACKS.md — Display](PACKS.md#display-equippable-display--custom-held-look) for the sheet spec.

## A staff that casts fireballs (Firestaff)

Swings a melee animation, launches a fireball every swing:
```jsonc
"spells": [ { "id": "mymod:fireball", "displayName": "Fireball", "projectileDisplayId": "fire_spell_new",
  "particleTrail": "small_fireball_blue", "attack": { "attackType": "Magic", "minDamage": { "pyro": 40 }, "maxDamage": { "pyro": 70 } } } ],
"items": [
  { "id": "mymod:firestaff", "name": "Firestaff", "icon": "scroll:brown",
    "equippable": { "equipmentType": "Weapon", "display": { "skinName": "Spear" },
      "weapon": { "class": "Sword", "minDamage": { "bludgeoning": 5 }, "maxDamage": { "bludgeoning": 10 },
        "castSpell": { "spellId": "mymod:fireball", "castType": "OnUse", "event": "OnAnyHit" },
        "hideTrail": true } } }
]
```
`hideTrail` drops the melee slash VFX — right for a staff/wand that only fires a spell (its swing is just the
cast animation, not a hit). Fields: [PACKS.md — Cast](PACKS.md#cast-weapon-castspell--fire-a-spell-from-a-weapon).

## A classic wand or scroll

The raise-tome pose instead of a swing: an Offhand
[`spellTome`](PACKS.md#spelltome-equippable-spelltome--classic-scrollwand) casting the same `mymod:fireball`:
```jsonc
{ "id": "mymod:fire_wand", "name": "Fire Wand", "icon": "scroll:brown",
  "equippable": { "equipmentType": "Offhand", "spellTome": { "spellId": "mymod:fireball", "energyCost": 10 } } }
```
**Heal spells need `"target": "Self"`** (vanilla heal scrolls do this).

## A melee weapon with a custom hit shape

The native swing's cone is class-fixed, but you can replace it entirely: give the weapon near-zero
`minDamage`/`maxDamage`, then `castSpell` with `castType: "OnUse"` pointing at your own
[`cone`](PACKS.md#spell-behaviors-spellsplus--casting-requirements) (any range/angle) or `line` (a thrust beam)
spell that carries the real damage. The weapon keeps its class swing animation, energy cost, charged attack and
the spell's `attack.skillId` trains a skill — only per-combo-step damage variation is lost. Pair with
`hideTrail` or a custom `trailSheet` so the visuals match the new shape.
```jsonc
"spells": [ { "id": "mymod:wide_sweep", "duration": 0.2,
  "cone": { "range": 64, "angleDegrees": 180 },
  "attack": { "attackType": "Melee", "maxTargets": 99, "minDamage": { "slashing": 40 }, "maxDamage": { "slashing": 60 } } } ],
"items": [
  { "id": "mymod:reaper_scythe", "name": "Reaper Scythe", "icon": "sword:iron",
    "equippable": { "equipmentType": "Weapon",
      "weapon": { "class": "Sword", "minDamage": { "slashing": 1 }, "maxDamage": { "slashing": 1 },
        "castSpell": { "spellId": "mymod:wide_sweep", "castType": "OnUse" }, "hideTrail": true } } }
]
```

## A chained spell — arrow that spawns lightning (Stormbow)

```jsonc
"spells": [
  { "id": "mymod:lightning", "displayName": "Lightning", "applyAura": "aura:general:stunned" },
  { "id": "mymod:stormshot", "projectileDisplayId": "arrow_spell",
    "attack": { "attackType": "Ranged", "minDamage": { "piercing": 25 }, "maxDamage": { "piercing": 35 } },
    "chain": [ { "event": "OnAnyHit", "spell": "mymod:lightning", "target": "Target" } ] }
]
```
The chained spell is just another spell — swap `mymod:lightning` for an explosion, a slow aura, a second
projectile… and chains nest (the chained spell can chain again) for multi-stage effects. Fields:
[PACKS.md — Chain](PACKS.md#chain-spell-chain--cast-on-hit).

## A heavy weapon — stagger, charged attack, stat bonus (Ember Maul)

Stagger + slow + charged attack + stat bonus (full weapon stats):
```jsonc
{ "id": "mymod:maul", "name": "Ember Maul", "icon": "sword:iron", "tier": 6,
  "equippable": { "equipmentType": "Weapon", "statBonuses": { "Health": { "additive": 50 } },
    "weapon": { "class": "Sledgehammer", "knockback": 130, "attackRange": 24, "stunPower": 0.6,
      "movementFactor": 0.5, "minDamage": { "bludgeoning": 120 }, "maxDamage": { "bludgeoning": 180 },
      "specialEnergyCost": 5,
      "castSpell": { "spellId": "mymod:ignite", "castType": "OnHit", "probability": 0.4,
                     "chargedSpellId": "mymod:fireball", "chargeTime": 0.4 } } } }
```
Every knob: [PACKS.md — Weapon](PACKS.md#weapon).

## Poison daggers, lifesteal & frost armor

**Poison dagger / lifesteal / frost armor / …** = the same shapes: weapon damage + an `applyAura` spell via
[`castSpell`](PACKS.md#cast-weapon-castspell--fire-a-spell-from-a-weapon) (use `target: "Self"` for
heal/buff-on-hit), or [`statBonuses`](PACKS.md#equippable) on gear.

## Drop your item from a mob

```jsonc
"drops": [ { "table": "enemy:fallen_sword", "item": "mymod:maul", "chance": 3, "min": 1, "max": 1 } ]
```
Table ids: `reference/droptable-ids.txt` (enemies, bosses, dungeons, biome chests). Fields:
[PACKS.md — Drop](PACKS.md#drop-mob--chest-loot).

## Dyed / recolored gear

No art needed — recolor a vanilla held shape through a palette row
([PACKS.md — Display](PACKS.md#display-equippable-display--custom-held-look)):
```jsonc
{ "id": "mymod:azure_blade", "name": "Azure Blade", "icon": "sword:iron",
  "equippable": { "equipmentType": "Weapon",
    "display": { "skinName": "Sword_long", "palette": "weapon", "paletteRow": 9 },   // 9 = blue
    "weapon": { "class": "Sword", "minDamage": { "slashing": 40 }, "maxDamage": { "slashing": 60 } } } }
```
Want a color the `weapon` palette doesn't have? Ship your own ramp with
[`palettes`](PACKS.md#palette-custom-dye-ramp) and reference it: `"palette": "mymod:fire", "paletteRow": 10`.
Worn armor dyes the same way — a [`playerSkins.looks`](PACKS.md#playerskins-top-level-playerskins--the-players-own-look)
fragment takes `palette` + `row` too. Add `trailColor` for a matching swing trail.

## Reskin a hit effect (custom VFX)

Drop a frame sheet and register it under a vanilla effect id —
```jsonc
"vfx": [ { "id": "blood_slash", "sheet": "my_slash.png", "frameWidth": 48, "frameHeight": 48, "frames": [0,1,2,3,4,5,6,7] } ]
```
Fields + the two ways to trigger one: [PACKS.md — Vfx](PACKS.md#vfx-custom-sprite-animation-effect).

## Multi-language names & descriptions

```jsonc
{ "id": "mymod:maul", "name": "Ember Maul",
  "nameLocalized": { "tr_TR": "Köz Balyozu", "de_DE": "Glut-Streitkolben" },
  "descriptionLocalized": { "tr_TR": "Ağır, sersemletici bir balyoz." }, ... }
```
Works on items, spells, placeables, quests, classes, creatures, maps, seeds, skills, favours, citizens — same
shape everywhere. Language codes + fallback rules: [PACKS.md — Item](PACKS.md#item).

## A healing shrine

A buildable shrine that heals on interact — the `forge_shrine` preset, zero C#. The complete copy-paste entry
(and its `heal`/`give-item`/`cooldown`/`vfx` params) is in
[PACKS.md — Interactive placeables](PACKS.md#interactive-placeables-controller-presets). Make it yours: pick a
fitting body via `cloneEntity` (browse `reference/entity-ids.txt`), add a
[`sprite`](PACKS.md#custom-sprite-placeables-your-own-art) for custom art, `give-item` for an offering altar,
`indestructible: true` for a quest temple.

## A portal to your own world

Two pieces: a [map](PACKS.md#map-top-level-maps--a-custom-dungeon-room) (your `.tmx` room, with its own
[music/ambience/light](PACKS.md#map-atmosphere-fields-on-maps-entries)) and a `forge_portal` placeable pointing
at it (`"controllerParams": { "map": "mymod:crypt" }`) — buildable anywhere, the map's exit returns the player
beside the portal. Copy-paste entry:
[PACKS.md — Interactive placeables](PACKS.md#interactive-placeables-controller-presets). A full worked version
is the [worldoftales walkthrough](#walkthrough-a-themed-world-pack-worldoftales) below.

## A talking gravestone

A placeable with a [`yarn`](PACKS.md#interactive-placeables-controller-presets) file is automatically a talking
object (`forge_dialogue`): interact opens the conversation. The dialogue language is plain-text YarnSpinner —
same syntax, commands (`<<ForgeGiveItem>>`, `<<ForgeTakeItem>>`, `<<ForgeHeal>>`) and translation sidecars as
[NPCs](PACKS.md#npc-top-level-npcs--a-talking-character-in-your-maps). Copy-paste entry ("Talking Stone"):
[PACKS.md — Interactive placeables](PACKS.md#interactive-placeables-controller-presets). For a gravestone, pick
a fitting body via `cloneEntity` (browse `reference/entity-ids.txt` for candidates) or ship a `sprite`.

## A two-handed staff

A staff needs both hands: `twoHanded` keeps the shield slot empty while it's held, `castSpell` OnUse
fires a projectile every swing, and `hideTrail` removes the melee slash a sword would draw.

```json
"items": [{
  "id": "mymod:pyrostaff", "name": "Pyrostaff", "icon": "sword:iron",
  "equippable": {
    "equipmentType": "Weapon",
    "display": { "skinName": "Spear", "palette": "weapon", "paletteRow": 5 },
    "weapon": {
      "class": "Sword", "swingTimer": 0.6, "twoHanded": true, "hideTrail": true,
      "energyCost": 6,
      "minDamage": { "bludgeoning": 5 }, "maxDamage": { "bludgeoning": 10 },
      "castSpell": { "spellId": "mymod:firebolt", "castType": "OnUse" }
    }
  }
}]
```

Equip it with a shield on: the shield jumps to your bag. Try equipping the shield back while holding
the staff: it bounces straight back. (Bows/crossbows are offhand items in this game — a two-handed
weapon blocks them too.)

## A custom crafting station

A Runeforge that crafts runes only at the forge — its recipes don't clutter the normal crafting menu.

```jsonc
{
  "id": "my", "namespace": "my",
  "placeables": [{
    "id": "my:runeforge", "name": "Runeforge", "icon": "scroll:brown",
    "cloneEntity": "WorkbenchGuid",
    "station": "my:runeforge",
    "materials": { "resource:stone": 10 }
  }],
  "items": [{ "id": "my:rune", "name": "Glowing Rune" }],
  "recipes": [{ "id": "my:r1", "crafted": "my:rune", "station": "my:runeforge",
                "ingredients": { "resource:stone": 2 } }]
}
```

Stations are plain strings in this game — a recipe with a `station` only appears in the window a
station opens for that id. Pressing E opens the vanilla crafting window scoped to your id (title and
icon from the placeable). Want it to double as a campfire? `"station": "my:runeforge,campfire"`.
A recipe whose station no placeable declares is never craftable.

## Custom sword art in 3 commands

```
# 1. see what held shapes exist
python tools/make_held_sheet.py --list-templates
# 2. generate the rig-aligned held sheet from ONE drawing (weapon pointing up, grip at bottom-center)
python tools/make_held_sheet.py art/flamebrand.png --template Sword
# 3. eyeball art/flamebrand_compare.png (orange ticks = check those frames), then wire the pack:
#    "display": { "skinName": "Flamebrand", "sheet": "art/flamebrand_sheet.png" }
```

Draw at 2–4× final size (40–60px tall) for the best downscale. Frames where vanilla paints a
motion-smear arc get your straight weapon at the right angle instead — usually fine in motion.
See ADVANCED.md for details.

> **Complete the weapon's feel — every knob in one list:**
> - **Held art** — this generated sheet (`display.sheet`), or recolor a vanilla shape (`display.palette`/`paletteRow`)
> - **Swing trail** — [`trailColor`](PACKS.md#weapon) (recolor the slash) · `trailSheet` (your own slash shape) · `hideTrail` (staves)
> - **Impact** — [`hitVfx`](PACKS.md#weapon) on every hit (vanilla effect or your own [`spacs`](PACKS.md#spacs-top-level-spacs--clone--reskinretime-a-vanilla-sprite-animation) clone) · `stunPower` + `knockback` for weight
> - **Sound** — `castSpell` spells take [`castSound`](PACKS.md#spell) — vanilla `event:/` or your own file via [`sounds`](PACKS.md#sounds-top-level-sounds--drop-in-audio-files)
> - **Swing shape & rhythm** — [`attackArcDegrees`](PACKS.md#weapon) (any cone width) · [`comboSteps`](PACKS.md#weapon) (per-hit damage/arc/timing) · [`twoHanded`](PACKS.md#weapon)
> - **Animation pacing** — [`playerAnimations`](PACKS.md#player-animations-top-level-playeranimations--retime-the-rig-visual) retimes the swing itself (visual)

## A multi-part monument (composite)

Two crystals, two collision boxes, a walkable gap between them — built, saved and demolished as **one**
construction-menu entry. List order = draw order; part 0's box sits on the placed entity itself, later
parts with `collision` become invisible blockers that live and die with it.

```jsonc
"placeables": [{
  "id": "mymod:monument", "name": "Twin Crystal Monument", "icon": "scroll:brown",
  "materials": { "resource:stone": 6 }, "width": 4, "height": 2,
  "parts": [
    { "sprite": "crystal.png", "offset": [-16, 0], "collision": [14, 10] },
    { "sprite": "crystal.png", "offset": [20, 4], "depth": 1,
      "collision": [14, 10], "collisionOffset": [20, 0] }
  ]
}]
```

`parts` can't be combined with `yarn`/`station`/`controller` (a composite is pure scenery), and the build
ghost previews the clone base's art — the layers appear once it's placed. Every knob:
[PACKS.md — Composite placeables](PACKS.md#composite-placeables-multi-part-parts).

## A custom town workshop

A real citizen-staffed job-building plus a new recipe in its workers' queue. Clone a vanilla workshop
(`base` keeps its maps, footprint, citizen slots and server controller — the parts that must stay
consistent) and re-skin the identity and economy; the work recipe teaches **every** bakery in the world
(recipes are per-job, not per-building).

```jsonc
"townBuildings": [{
  "id": "mymod:grand_bakery", "base": "building:bakery:0",
  "name": "Grand Bakery", "description": "Bread for the whole province.",
  "materials": { "resource:lumber": 4, "resource:brick": 2 }
}],
"workRecipes": [{
  "id": "recipe:mymod:garum_bread", "job": "job:baker", "level": 1,
  "output": "food:bread", "amount": 2, "ingredients": { "material:flour": 1 },
  "progress": 30, "experience": 5, "fuel": true
}]
```

Build hammer → **Buildings** → place + hammer up → assign a baker → the recipe shows in the work queue
(ingredients drain from the building's storage, output lands in its output inventory, job XP ticks).
Want a different worker in a bakery shell? Set `"job": "job:blacksmith"`. Fields:
[PACKS.md — Town building](PACKS.md#town-building-top-level-townbuildings--a-citizen-staffed-workshop) ·
[Work recipe](PACKS.md#work-recipe-top-level-workrecipes--what-citizens-produce).

## A fully custom creature from one PNG

A brand-new monster with **zero vanilla art** — the wisp pattern: a
[from-scratch spac](PACKS.md#from-scratch-spacs-no-base--brand-new-animations-from-your-pngs) is the whole
rig, `forge_creature_anim` drives it, and a built-in
[AI preset](PACKS.md#built-in-ai-presets-no-addon-dll-needed) is the brain. The `base` creature only
contributes stats and components — none of its art ever shows.

```jsonc
"spacs": [{
  "id": "mymod:wisp_anims", "directions": 1,
  "sheets": { "wisp": "wisp.png" }, "frameWidth": 32, "frameHeight": 32,
  "animations": {
    "idle":   { "frames": [0, 1], "tickLength": 12, "loop": true },
    "attack": { "frames": [2, 2], "tickLength": 4 }
  }
}],
"creatures": [{
  "id": "mymod:wisp", "base": "SkeletonWarrior", "name": "Glade Wisp",
  "healthMultiplier": 0.5,
  "controller": "forge_guard",
  "clientController": "forge_creature_anim",
  "controllerParams": {
    "spac": "mymod:wisp_anims",
    "anim-map": "0=idle:loop,1=idle:loop,2=attack",
    "walk-px-per-sec": "40", "death-size": "tiny"
  }
}],
"spells": [{
  "id": "mymod:summon_wisp", "displayName": "Summon Wisp",
  "summon": { "creature": "mymod:wisp" }
}]
```

Draw `wisp.png` as a grid of 32×32 cells (here: two idle frames + an attack frame), map the AI's animation
ints in `anim-map`, and pick any preset — `forge_guard` for a sentry, `forge_ranged` + a spell for a caster,
`forge_pack` for swarms. The shipped test pack's **T89 Wisp** is exactly this recipe.


## Your own favour tree screen

Give your pack its **own fullscreen favour tree** — the exact vanilla look (draggable node grid, gray
connection lines, point counter, confirm-to-unlock for 1 favour point), opened from a **"Pack Favours"**
button that appears in the Skills & Favours window (K) whenever a pack defines `screen: true` nodes:

```jsonc
{
  "id": "emberfaith", "namespace": "ember", "version": "1.0.0",
  "favours": [
    { "id": "ember:attunement", "name": "Ember Attunement", "icon": "anvil",
      "description": "Root of the Ember path: +10 max Health.",
      "screen": true, "position": [1, 0],
      "statBonuses": { "Health": { "additive": 10 } } },

    { "id": "ember:burning_hands", "name": "Burning Hands",
      "screen": true, "parent": "ember:attunement", "position": [0, 1],
      "statBonuses": { "PyroDamageModifier": { "additive": 0.10 } } },

    { "id": "ember:cinder_recipe", "name": "Cinder Forging",
      "description": "Unlocks the Cinder Blade recipe.",
      "screen": true, "parent": "ember:attunement", "position": [2, 1],
      "recipe": "ember:r_cinder_blade" }
  ]
}
```

- `screen: true` = the node lives on the pack screen, never in the vanilla tree.
- **No `parent` = a root** (always unlockable). With `parent`, the node stays greyed until the parent is
  learned; a connection line is drawn between their `position` cells.
- `position` is the pack screen's **own grid** — start at `[0,0]`; all packs share the one screen, so big
  packs should leave room for neighbors.
- Effects, costs (1 point), uninstall safety (auto strip + refund) and co-op rules are identical to
  vanilla-tree favour nodes — see [PACKS.md — Favour](PACKS.md#favour-top-level-favours--a-new-favour-tree-node).

## Walkthrough: a themed world pack (worldoftales)

The shipped **`examples/worldoftales/`** pack is the pattern for story/exploration packs — a private realm, a
character in it, and a reason to go. Five pieces, one json:

1. **The realm** — a [map](PACKS.md#map-top-level-maps--a-custom-dungeon-room) (`wot:tales_realm`, authored as
   `realm.tmx` in Tiled) with no overworld entrance (`biomes` omitted) and its own
   [atmosphere](PACKS.md#map-atmosphere-fields-on-maps-entries): dungeon music, `crypt` ambience, dim purple
   `ambientLight`.
2. **The way in** — a `forge_portal` [placeable](PACKS.md#interactive-placeables-controller-presets) (the
   Storyteller's Arch): buildable anywhere, teleports into the realm; the map's exit node returns you beside it.
3. **The character** — an [NPC](PACKS.md#npc-top-level-npcs--a-talking-character-in-your-maps) (the Keeper of
   Tales) standing in the realm: `sage.yarn` dialogue, a purple `tint` for a unique body, localized actor names
   + a `tr_TR` translation sidecar.
4. **The loop** — the Keeper trades 3 sticks for a **Storybook Ember**
   (`<<ForgeTakeItem>>`/`<<ForgeGiveItem>>`), and a [quest](PACKS.md#quest-top-level-quests--a-world-quest) with
   a `collect 1` objective on the ember — the **token pattern** that makes "go talk to my NPC" a trackable quest.
5. **The payoff** — the quest unlocks a [recipe](PACKS.md#recipe) that crafts the ember into a trinket with
   [stat bonuses](PACKS.md#equippable).

Copy the folder, rename the `namespace`, swap the tmx/yarn/items — the structure carries any theme.

## Walkthrough: a necromancer class pack

The shipped **`examples/necromancer/`** pack is the pattern for a themed gear-and-class pack:

1. **The summons** — two [summon spells](PACKS.md#summon-spell-summon--spawn-a-creature-that-fights-for-you):
   `raise_skeletons` (count 2, `guardian`) and `raise_skeleton` (`companion`), both capped with `maxActive: 6`.
2. **Two casters, two feels** — the **Book of the Dead**, an Offhand
   [`spellTome`](PACKS.md#spelltome-equippable-spelltome--classic-scrollwand) (raise-tome pose, 2 skeletons per
   cast), and the **Bone Staff**, a [weapon](PACKS.md#weapon) with
   `castSpell: { castType: "OnUse" }` + `hideTrail` (one skeleton per swing — the
   [Firestaff](#a-staff-that-casts-fireballs-firestaff) shape with a summon instead of a fireball).
3. **The gear identity** — the **Necromancer Robe**: [`statBonuses`](PACKS.md#equippable) `+50 Energy`,
   `+25% NecroDamageModifier`.
4. **Ways to get it** — a [recipe](PACKS.md#recipe) for the book, a 2% [drop](PACKS.md#drop-mob--chest-loot)
   from Fallen Swordsmen, and a kill-5 [quest](PACKS.md#quest-top-level-quests--a-world-quest) that unlocks the
   recipe.
5. **The class** — a [playable class](PACKS.md#class-top-level-classes--a-new-playable-profession) starting with
   the book, coal, and food (food fills the hotbar; gear auto-equips).
6. **The lair** — a basic [placeable](PACKS.md#basic-placeables-vanilla-art-via-spawnedid) (Bone Brazier,
   vanilla `brazier:2` art) for set dressing.

---

> **Don't want to hand-write JSON?** Use the **visual editor** — see `editor/README.md` (cross-platform, dropdowns
> for every id, per-language text, live validate). It writes exactly these packs.



