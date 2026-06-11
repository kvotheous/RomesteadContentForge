# Content Forge — Value Reference (id catalogs)

_What this is: the long id catalogs (icons, items, displays, auras, drop tables…) to copy values from. What
this isn't: the field spec — what each field means lives in [PACKS.md](PACKS.md). Docs index: [README.md](../README.md)._

Companion to **`PACKS.md`** — that file is the full **field-by-field spec** (every field, type, default, meaning,
and the valid enum values explained). This file holds the long **id catalogs** to copy from.
Big lists are dumped to text files in `reference/` — open them to browse/copy:

| File | Count | Used by |
|---|---|---|
| `icon-ids.txt` | 520 | any `icon` field (items, skills, favours, citizens, build menu) |
| `display-ids.txt` | 191 | `equippable.displayId` held/worn looks (`cdd:…`), citizen `look.displayIds` |
| `aura-ids.txt` | 149 | spell `applyAura`, gear `entityAuraId`, favour `aura`, drop `aura` |
| `item-ids.txt` | 675 | recipe `ingredients`/`crafted`, shop `items`, drops, rewards, starting inventory (now includes the `food:` / `resource:` namespaces) |
| `creature-ids.txt` | 137 | spell `summon.creature`, creature `base`, quest `kill` targets, event waves/bosses |
| `droptable-ids.txt` | 117 | a drop's `table`, a map's `lootTable` |
| `decoration-ids.txt` | 28 | basic placeable `spawnedId` (vanilla art) |
| `entity-ids.txt` | 188 | a placeable's `cloneEntity` (EntityIds names) |
| `music-track-ids.txt` | — | a map's `music` / `ambience` (bare track ids; raw `event:/...` paths also work — auto-registered) |
| `citizen-ids.txt` | 5 | `citizenOverrides` keys (vanilla citizen types + their baseline stats) |
| `raid-ids.txt` | 9 | a `scheduledRaid` event's `raidData` |
| `spac-ids.txt` | 93 | weapon `hitVfx` fallback spac, spell `spacVfx`, custom spac `base`, `trailSheet` clone targets, forge_creature_anim `spac` (creature rigs / prop rigs / effect spacs marked; code-referenced ids only — known-partial) |
| `vfx-ids.txt` | 32 | weapon `hitVfx`, vfx overrides, explosion `spacVfx`, shrine/portal vfx params (17 spac-vfx with their spac+animation, 15 sprite-vfx; complete for vanilla) |
| `favour-ids.txt` | 89 | a favour's `parent` (all vanilla favours by god, with aura/flag effect notes; complete for vanilla) |
| `biome-ids.txt` | 28 + 9 | maps take prefixed `biome:` ids; crops/shops take the bare category names — file explains the distinction and maps each biome to its category |
| `sound-event-ids.txt` | 520 | spell `castSound`, sfx params, map `music`/`ambience` raw paths (`event:/…`, grouped by FMOD folder; code-referenced — known-partial) |
| `anim-state-ints.txt` | 73 controllers | AI preset anim-int params, forge_creature_anim anim-map (per vanilla client controller: state-int / anim-byte → animation, plus each rig's valid animation names; known-partial where decompile unreadable) |

---

## Enums (use these exact spellings)

### `equipmentType`
`Helmet`, `Armor`, `Boots`, `Trinket`, `Back`, `Weapon` (melee), `Offhand` (ranged / scroll / shield), `Ammunition`, `LightSource`, `LumberAxe`, `Pickaxe`, `FishingRod`.

### `material`
`Flint`, `Copper`, `Bronze`, `Iron`, `Silver`, `Gold`, `Steel`, `Cheat`, `Rusted`.

### weapon `class` → animation
| class | slot | animation |
|---|---|---|
| `Sword` | Weapon | side swing |
| `Spear` | Weapon | forward thrust |
| `Dagger` | Weapon | quick stab |
| `Sledgehammer` | Weapon | heavy overhead |
| `Fists` | Weapon | punch |
| `Bow` | Offhand | draw + loose (needs arrows) |
| `Crossbow` | Offhand | aim + fire (needs arrows) |
| `Javelin` | Weapon | throw |
| `Shield` | Offhand | raise/block |
| `SpellTome` | Offhand | raise tome (the static "cast" pose) |

> For a **staff with a swing**, use a melee class (e.g. `Sword`) + `castSpell.castType: "OnUse"` — not `SpellTome`.

### held-look (`displayId` reuse, or `display` recolor)
The **held/equipped** model is separate from the inventory icon. Either reuse a vanilla `cdd:...` id
(`displayId`, 191 in `reference/display-ids.txt`), or compose one with `display` (existing shape + recolor):
- `skinName` (shape): main-hand (`skinTag` 8) — `Sword`, `Sword_leaf`, `Sword_long`, `Dagger`, `Spear`,
  `Hammer`, `Hammer_axe`, `Cestus`, `Bow`; offhand (`skinTag` 11) — `Crossbow`, `javelin`.
  *(There is no staff/wand shape — `Spear` or `Sword_long` is the closest "staff" look.)*
- `palette`: `weapon` (also `metal`/`leather`/`fabric`/`details` for armor) · `paletteRow`: 0–9 (color/material variant).
- **Custom art** (`display.sheet`): your own held weapon/staff — one PNG **1104×240**, 48×48 frames (23×5),
  rig-aligned to the vanilla `tools/sword/sword` sheet, transparent, no palette. (Only the held model; the
  inventory icon is the separate `icon` PNG.)
- **Swing-trail:** the trail follows the weapon **class** automatically; `weapon.trailColor` (`#RRGGBB`) tints
  it (e.g. a fiery slash); `weapon.trailSheet` swaps in your **own repainted trail art** (the mod clones the
  class's vanilla trail spac and keeps all timing/direction data — see PACKS.md "Custom trail art");
  `weapon.hideTrail` suppresses it entirely (staves/wands).

### `flags` (item)
`Food`, `Material`, `Ammunition`, `Money`, `Millable`, `Quest`, `Seed`, `Offering` (or omit). Comma-separate for multiple.

### usable `type`
`Healing` (eatable/drinkable consumable, auto-triggers) · `Special` (no built-in use trigger — use a weapon `spellTome`/`castSpell` for offensive "use").

### weapon `castSpell.castType` / `.event`
`castType`: `OnHit` (procs on a connecting hit) · `OnUse` (casts every attack/swing — staves).
`event`: `OnEntityHit` (only enemies) · `OnAnyHit` (incl. terrain).

### spell `attack.attackType`
`Magic`, `Melee`, `Ranged`.

### recipe `station`
omit/`null` (basic crafting) · `campfire` · `cauldron` · `clay-barrel` · `war-table` · any pack station id (a placeable's `station` field).

---

## Damage types (spell/weapon min/maxDamage keys)
`true`, `slashing` (slash), `piercing` (pierce), `bludgeoning` (bludgeon), `pyro` (fire), `chloro` (nature), `aqua` (water), `cosmo`, `necro`.

## Stat keys (`statBonuses`)
`Health`, `Energy`, `EnergyRegeneration`, `Armor`, `CritChance`, `AttackSpeed`, `MovementSpeed`,
`MeleeDamage`, `RangedDamage`, `MagicDamage`, `ThrowingDamage`, `PickaxePower`, `AxePower`, `HealingDoneModifier`,
`Knockback`, `KnockbackResistance`,
`MagicDamageModifier`, `RangedDamageModifier`, `ThrowingDamageModifier`, `BludgeoningDamageModifier`,
`PiercingDamageModifier`, `NecroDamageModifier`, `ChloroDamageModifier`, `PyroDamageModifier`, `CosmoDamageModifier`,
`PyroResistance`, `AquaResistance`, `ChloroResistance`, `CosmoResistance`, `NecroResistance`, `PiercingResistance`,
`BludgeoningResistance`, `MagicResistance`. Each: `additive` (flat) and/or `multiplier`.

---

## Spell visuals & sound (reuse these)

### `projectileDisplayId` (spell projectile look)
`arrow`, `arrow_spell`, `arrow_skeleton`, `ballista_arrow`, `dagger`, `dart_trap`, `blowdart`, `blowdart_poison`,
`fire_spell_new`, `fireball_new`, `aoe_fire`, `aoe_poison`, `poison_bolt`, `projectile_lightning_bolt`,
`projectile_scattershot`, `feather`, `boulder`, `construct`, `cursed_skull`, `hook`, `hook_hand`, `summon`, `no_vfx`.

### `particleTrail` (emitter)
`small_fireball_blue`, `small_fireball_flame`, `small_fireball_green`, `fireball_flame`, `fireball_flame2`,
`fireball_flame_3`, `fireball_green_flame`, `phoenix_fire_line`, `big_waterball`, `small_waterball`,
`blue_sparks8`, `red_sparks4`, `earthquake_tiny`.

### `castSound` (FMOD events)
`event:/spells/cast_fireball`, `event:/spells/cast_massive_fireball`, `event:/spells/cast_spell_cosmo`,
`event:/spells/cast_spell_healing`, `event:/spells/cast_spell_lightning`, `event:/spells/cast_spell_nature`,
`event:/spells/cast_spell_necro`, `event:/spells/cast_spell_projectile`, `event:/spells/cast_spell_projectile_volley`,
`event:/spells/cast_spell_stone`, `event:/spells/cast_spell_summon`, `event:/spells/cast_spell_water`.

### `spellTypeId`
You normally never set this — Content Forge picks it from the spell's blocks:
`spelltype:single_projectile_attack` (default), `instant_single_target` (`applyAura`), `summon_creature`
(`summon`), `area_of_effect` (`aoe`), `explosion`, `cone_attack` (`cone`), `line_attack` (`line`),
`multiple_projectile_attack` (`multiProjectile`), `heal`, `mass_heal` (`massHeal`). See PACKS.md
"Spell behaviors (SpellsPlus)". A few engine types remain unexposed (`linear_shockwave`, `hook`, `scripted`, …).

---

## Auras (for `applyAura` on a spell, or `entityAuraId` on gear)

### Status / debuff (great for weapon `onHit` procs)
`aura:general:burning`, `aura:general:fast_burning`, `aura:general:weak_poisoned`, `aura:general:faint_poisoned`,
`aura:general:poisoned`, `aura:general:strong_poisoned`, `aura:general:slowed`, `aura:general:bow_slow`,
`aura:general:stunned`, `aura:general:system_stunned`, `aura:general:petrified`.

### Buffs (for `entityAuraId` on gear, or consumables)
`aura:consumable:damage_buff:minor|major`, `aura:consumable:attack_speed_buff:minor|major`,
`aura:consumable:crit_buff:minor|major`, `aura:consumable:defence_buff:minor|major`,
`aura:consumable:energy_regen`/`energy_regen_major`, `aura:consumable:major_spellpower`, `aura:consumable:rage`,
`aura:consumable:fire_resistance`/`:major`, `aura:consumable:nature_resistance`/`:major`,
`aura:consumable:necrotic_resistance:minor|major`.

Full list (149): `reference/aura-ids.txt`.

---

## Reusing item/icon/visual ids

- **Icons** (`icon`): 520 in `reference/icon-ids.txt` — e.g. `sword:iron`, `axe:gold`, `bow:silver`, `ring:red`,
  `shield:iron`, `crossbow`, `apple:red`, `scroll:brown`, `arrow:iron`. (Or supply your own `.png` — see PACKS.md.)
- **Held visuals** (`displayId`): 191 in `reference/display-ids.txt` — e.g. `cdd:iron_sword`, `cdd:shield_iron`,
  `cdd:crossbow_leather`, `cdd:arrow:green`.
- **Item ids** (recipe `ingredients`, also any vanilla `crafted`): 675 in `reference/item-ids.txt` —
  e.g. `food:apple`, `material:iron_bar`, `resource:stone`. The file includes the `food:` and `resource:`
  namespaces; there are **no** `ore:`/`wood:` namespaces in the game (browse the file for exact ids; or read
  them off Item Spawner).
- **Creature ids** (`summon.creature`, creature `base`, quest kills, event waves): `reference/creature-ids.txt` (137).
- **Drop-table ids** (a drop's `table`): `reference/droptable-ids.txt` (117).
- **Decoration ids** (basic placeable `spawnedId`, vanilla art): `reference/decoration-ids.txt` (28, with variant numbers).
- **Citizen type ids** (`citizenOverrides` keys, with baseline stats): `reference/citizen-ids.txt`.
- **Raid ids** (`scheduledRaid.raidData`, with tiers): `reference/raid-ids.txt`.

> These lists are extracted from the current game build; new game versions may add/rename ids.

> Want previews instead of text lists? `python tools/asset_catalog.py` builds `asset-catalog.html` with thumbnails of every sprite sheet plus all of these id lists in one searchable page.
