# Changelog — Romestead Content Forge

## 1.0.1
Compatibility + fixes release. **Required for the Romestead 2026-10-04 update** — 1.0.0 was built before it and is not compatible.

- **Romestead 2026-10-04 update** — interactive placeables follow the new interact signature; a *held*
  interaction (vanilla pickup/demolish) no longer triggers their tap action.
- **Survives future game updates better** — each patch is applied on its own; if an update breaks one, only
  that feature is disabled (logged as `Patch ... failed`) instead of the whole mod.
- **Summons no longer walk through walls** — they spawned at spell-cast height, above every wall's collision.
  Now grounded on spawn and when loaded from a save (older saves heal on load).
- **Companion teleport** — never lands inside walls/ruins and no longer copies the player's height mid-jump.
- **From-scratch creature sprites** stand upright and sit on the ground line; new spac field `feetRow` anchors
  the art's feet. New `forge_creature_anim` params `sprite-scale` and `sprite-offset-y` for fine tuning.
- **Portal sounds** now also play for the traveler, not just at the portal they left.
- **Dialogue placeables** — an unparseable `dialogue-distance` falls back to the default instead of closing the
  window immediately.
- **`cfvalidate`** (C# validator) compiles again, and no longer warns about a recipe station that one of the
  pack's own placeables declares.
- **Test pack / Authoring zip** — art derived from vanilla sprites removed (T37 rotter is now a color tint; the
  T42 hair sample is gone). The Authoring zip now includes the editor's `labels.json`, icon and `requirements.txt`.
- **License notices** — every download now includes `THIRD-PARTY-NOTICES.txt` (licenses of the bundled open-source
  libraries); the editor ships as `ForgeEditor-<version>.zip` (exe + notices).

## 1.0.0
Initial release. Adds **brand-new content** to Romestead from JSON "content packs" in
`BepInEx\config\RomesteadContentForge\packs\` — no game files touched, no compiling. See `docs/PACKS.md` for the
full field reference and `REFERENCE.md` for the id catalogs.

- **Announcement banners** — event/arena/handshake/two-handed messages now show as **big red text
  top-center** instead of the bottom-right toast. Configurable in `RomesteadContentForge.cfg`
  `[Notifications]`: `Style` (banner/toast), `BannerColor` (R,G,B), `BannerYPercent`, `BannerSeconds`.
- **Custom favour screen (`screen: true` favours)** — packs get their own fullscreen favour tree (the exact
  vanilla look: draggable node grid, connection lines, point counter, click-to-unlock for 1 point), opened
  by a **"Pack Favours"** button in the Skills & Favours window. Roots and parent-gated children on the
  pack's own grid — the vanilla tree is never touched.
- **Summon faction fix** — hostile-intent summons (`entityType: "Hostile"`) no longer join the player's
  faction (you can hit them, they aggro you properly); ALLY summons now sync their faction to the CLIENT
  too, so an ally's arrows/spells can never hit you while flying past.
- **Town job-buildings & work recipes (`townBuildings` / `workRecipes`)** — clone a vanilla workshop
  (bakery/blacksmith tier) into your own buildable building (name/icon/cost/effort/appeal/town-defence, and an
  optional **job swap** for who staffs it), and append **new work recipes** to any job's queue at any level
  (ingredients/resources, fuel/furnace, XP, repeatability).
- **Composite placeables (`parts`)** — a multi-part building as ONE buildable: several pack PNGs at per-part
  offsets with a controlled draw order, per-part **collision boxes** (walkable gaps included), placed,
  saved/reloaded and demolished as a single unit.
- **Retro entrances (`retrofit` on maps)** — dungeon entrances can now be stamped into **EXISTING saves**:
  once per save, on world load, in a clear spot of a listed biome (away from spawns, towns, buildings, players
  and other entrances); retries next load if no spot is found.
- **From-scratch spacs** — `spacs` entries **without `base`** build brand-new sprite animations from your own
  PNGs (`directions` 1/4/8, multi-sheet, named `animations` with frames + tick timing). The new id works
  anywhere a spac/vfx id is taken AND as a creature's **entire rig** via `forge_creature_anim` — a fully
  custom monster with zero vanilla art.
- **Five more AI presets** — `forge_patrol` (waypoint routes, resumes after combat), `forge_pack` (alerts
  same-base packmates, cascading), `forge_summoner` (kiting conjurer with living-minion caps), `forge_healer`
  (flock medic, always flees), `forge_turret` (stationary caster) — **8 built-in presets** total.
- **Three competitive event types** — `teamBattle` (auto-balanced teams on a pvp map, same-team damage
  swallowed, first to N kills), `koth` (sole-occupant king-of-the-hill scoring), `race` (ordered checkpoints,
  countdown + time limit) — **7 event types** total.
- **Creature drops (`drops[].creature`)** — append loot to a creature's own baked-in drop component:
  **wildlife drops** (bears/wolves/deer) are finally possible (the old "no shared wildlife table" limitation
  is closed from the other side).
- **Summon kill credit** — a summon's killing blow in PvP/arenas now credits its **owner** (a `cf-owner`
  stamp on summon spawns; used to read "not a player").
- **Companion facing fix** — a `companion` summon now faces its owner right after the teleport-leash blink
  (set once at the blink — no animation thrash).
- **Map auto-floor** — a `.tmx` that ships only the data layers (`ground`/`structure`) now auto-generates a
  plain cave floor (`cf_autofloor`) instead of rendering pitch black.
- **GUI editor overhaul** — **31 schema-driven sections** (every `ContentPack` section, incl. town buildings,
  work recipes, from-scratch spac `animations` and the new event types), the Forge **dark theme**, a
  click-through Validate (F5) panel, and a headless **test suite** with a `ContentPack.cs` ↔ `schema.json`
  parity gate.
- **Six new id catalogs** — `reference/spac-ids.txt`, `vfx-ids.txt`, `favour-ids.txt`, `biome-ids.txt`,
  `sound-event-ids.txt`, and `anim-state-ints.txt` (the animation ints of **73 vanilla client controllers**,
  mapped — no more trial-and-error anim ints).
- **Items** — any item with literal name/description, tier, flags, max-stack, and a sell/buy **price**.
- **Icons** — reuse any of 520 vanilla icon ids, **or** drop your own `.png` (18×18) and reference it.
- **Gear** — equippable slots (Helmet/Armor/Boots/Trinket/Back) with **stat bonuses** (Health, Crit, Move
  Speed, damage, resistances…) and/or an existing aura.
- **Weapons** — **melee** (Sword/Spear/Dagger/Sledgehammer/Fists) and **ranged** (Bow/Crossbow/Javelin), with
  damage by element, knockback, range, energy cost.
- **Shields** — block strength + arc.
- **Spells** — custom projectile **spells** (damage by type, reused projectile/trail/cast-sound) and
  **status spells** (`applyAura` → burn/poison DoT or buff).
- **Scrolls, wands & staves** — an Offhand **spell-tome** (raise-tome cast), or a melee weapon that casts a
  spell **on swing** (`castSpell` `castType: OnUse`) for a staff with a real swing animation; or **on hit**
  (`OnHit`) for ignite/poison-on-hit weapons.
- **Summons (necromancer)** — a spell's `summon` block spawns a creature that **fights for you** (skeletons,
  skulls, wolves…), with a `count` (e.g. 2 skeletons per cast) and an optional `maxActive` cap (oldest despawns
  past the limit). Bind it to a spell-tome or an on-swing staff.
- **World quests** — a `quests` block adds auto-activating quests (kill/collect/loot/craft/harvest objectives)
  that unlock crafting recipes when finished. No NPC or dialogue needed.
- **Placeable furniture** — a `furniture` block adds buildable decorations to the construction menu, reusing any
  vanilla decoration's art (`spawnedId`) with your own name, icon, build cost and footprint. The recipe
  auto-unlocks on login so it's always in the menu. See `reference/decoration-ids.txt` for the art to reuse.
- **Custom creatures** — a `creatures` block clones a vanilla creature (its AI/art) under a new id and overrides
  **name, max health, and color tint** — an elite/recolored variant (e.g. a 3× health purple "Bone Lord"). Summon
  it like any creature via a spell's `summon.creature`. (Brand-new sprites/AI aren't possible — controllers can't
  be injected — so it reuses the base creature's behavior.)
- **Custom dungeon rooms (`maps`)** — author a room in the **free Tiled editor** (a `.tmx` with `ground`/
  `structure` tile layers), ship it next to your pack, and the mod generates the game's binary world file,
  registers it as a handcrafted dungeon, and adds an **overworld entrance** that newly-generated worlds roll
  like any vanilla dungeon (`biomes` + `weight`). Exit box configurable; new worlds only (vanilla POI rule).
- **`dismissOnUnequip` summons** — set it on a spell's `summon` block and the summons despawn the moment the
  book/staff that casts them leaves the player's equipment (including weapon-set swaps).
- **Multi-room dungeons** — a map's `doors` link rooms (`{edge, target}`); `exit: false` marks inner rooms.
  Build whole multi-chamber dungeons from several Tiled files.
- **Shops** — a `shops` block adds new stock sections at the Market (the merchant offers them automatically by
  level/biome), and `shopAdditions` appends your items into vanilla sections. Prices drive buy/sell.
- **Weapon hitVfx** — a weapon can play an extra impact VFX at the hit position (your custom `vfx` ids or
  vanilla ones) — fire bursts on a flame blade, holy flashes on a paladin mace.
- **Crops & farming** — a `crops` block adds plantable crops: auto-generated seed item, growth stages (vanilla
  art or your PNG), watered/unwatered times, biome gating, harvest drop tables — and **farmsteads farm custom
  crops automatically** (drop the seed back and it's self-sustaining). Save-safe on uninstall.
- **Custom particles & emitters** — `particles` + `emitters` blocks build your own effects (sprite, fade,
  gravity, attached light, HSL drift; weighted mixes, spawn areas, velocity) usable anywhere the game takes an
  emitter id — e.g. a spell's `particleTrail`.
- **Interactive furniture** — give a `furniture` entry a `controller` (`forge_shrine` heals on interact with a
  cooldown; `forge_lever` toggles state) + `cloneEntity` and it places a real interactive entity —
  **server-authoritative** (heals/items go through the real game systems, co-op-safe) and save-persistent.
  All presets take `tooltip` (hover text) and `message` (floating text on interact).
- **Custom art everywhere** — placeables can ship **their own sprite** (`sprite`: a plain PNG, optional ambient
  animation), and custom creatures can ship **fully repainted animation rigs** (`sheet`/`sheets`: 1:1 repaints
  of the base creature's sheets — extract templates with `tools/xnb2png.ps1`). Plus `indestructible` placeables
  for quest shrines/temples, and clean demolish (no vanilla item drops) for interactive placeables.
- **Dialogue NPCs** — an `npcs` block puts a **talking character** in your custom maps: write the conversation
  as a plain-text **YarnSpinner `.yarn`** file (lines, options, portraits, conditions, the game's own quest/item
  commands) and the mod compiles it at load. Per-language translation files supported. **Forge dialogue
  commands** complete the API: `<<ForgeGiveItem>>`, `<<ForgeTakeItem>>`, `<<ForgeHeal>>` — NPCs can check your
  bag, take payment, and hand you items or healing, all in plain text.
- **Two-handed weapons (`twoHanded`)** — the offhand slot stays empty while the weapon is held (offhand
  auto-ejects to the bag; bows/crossbows count as offhand in this game).
- **Custom crafting stations (`station` on placeables)** — your placeable opens its own crafting window
  listing only its recipes; can double as a vanilla station via a comma list.
- **Pre-placed map doodads (`maps[].doodads`)** — put campfires, chests, any entity, or your own PNG props
  (with collision boxes) directly into your maps from JSON.
- **Tooling** — `tools/asset_catalog.py` (browse every game sprite with thumbnails + all id catalogs in one
  searchable HTML) and `tools/make_held_sheet.py` (generate a rig-aligned held-weapon sheet from ONE drawing).
- **World events (`events`)** — host-run event types: **wave defense** (waves spawn in a ring around the
  players in your map, survivors rewarded), **boss rush** (one elite at a time), **hunt contests** (timed
  most-kills competitions, scheduled to night/day/new-day or always-on), and **scheduled vanilla raids**
  (the real raid system, fired on your calendar).
- **Custom citizens (`citizens` / `citizenOverrides`)** — new town citizen types with arrival weighting,
  fixed localized names, starting job mastery and an optional **fixed appearance** (face/wardrobe/worn
  looks) — plus edits to vanilla citizen types.
- **Per-combo-step weapons (`comboSteps`)** — each hit of a melee chain gets its own damage multiplier,
  swing arc, and timing (build a slow-wide finisher).
- **Custom animation spacs (`spacs`)** — clone a vanilla effect animation with your own sheets and timing;
  use the new id anywhere (hitVfx, explosions, …). Plus **`playerAnimations`** — retime player animation
  states (visual).
- **Quest item rewards (`rewardItems`)** — quests grant items straight into the bag on completion (the
  vanilla item-reward strategy was an unimplemented stub — ours rides the official reward pipeline).
- **Replace vanilla dungeons (`replace` on maps)** — your Tiled room becomes the interior of a vanilla
  dungeon (same entrance/name/loot).
- **Built-in creature AI presets** — `forge_guard` (post guard with a leash), `forge_coward` (flees at low
  health), `forge_ranged` (kiting caster — ANY creature becomes a spell-slinger with one JSON line). Plus
  **`clientController: "forge_creature_anim"`** — data-driven client animations that let cross-family AI
  swaps animate correctly.
- **Forge tab** — the Skills & Favours window gains a third toggle listing installed packs, pack skills
  with levels, and learned pack favours with effects (localized; pure client UI).
- **Swappable creature AI (`creatures[].controller`)** — give a cloned creature ANY server AI by name
  (vanilla — an archer brain on a warrior body — or your own from an addon DLL). The old "controllers
  can't be injected" limitation was disproven and removed.
- **Custom favour-tree nodes (`favours`)** — real nodes in the gods' favour tree (stat bonuses, auras, or
  recipe unlocks; localized tooltips), attached under any vanilla node. **Uninstall-safe by construction**:
  the save file never contains a modded favour — remove the pack and the character logs in fine with the
  point refunded (automatic respec).
- **Drop-in custom sounds (`sounds`)** — put a `.wav`/`.ogg`/`.mp3` next to your pack and reference it as
  `cf:sound:<ns>:<name>` anywhere a sound id goes (spell casts, shrines, portals). No FMOD Studio needed;
  obeys the sound-effects volume slider; 3D-positional.
- **Per-weapon swing arc (`attackArcDegrees`)** — a melee weapon can sweep any cone width (vanilla is 144°;
  300° = near-circle swipe), plus `attackArcOffsetPx` to pull the hit zone closer or farther.
- **Player body rigs (`playerSkins.rig`)** — repaint or tint the player's animated body itself (not just
  worn parts) — every frame of every animation. Citizens and menu previews stay vanilla.
- **Map atmosphere fix** — `music`/`ambience` accept vanilla track ids (`dungeon`, `crypt`, ...) or raw
  `event:/...` paths (auto-registered); unknown ids no longer play the "music not available" placeholder.
- **PvP zones (`pvp: true` on maps)** — players can finally fight each other, but only inside maps you flag
  for it: friendly-fire gates lift there, dying spawns **no grave** (no item loss), and the server **rejects**
  player→player damage everywhere else (closes the hacked-client hole). Both fighters must run the mod + packs.
- **Duel arenas (`arenas`)** — bind an arena to a pvp map: kill announcements in chat ("X slew Y in the
  Colosseum!"), timed **matches** that start when enough fighters walk in, a winner (most kills / last standing)
  paid an item reward straight into their bag — or an endless free-for-all paying per kill. Kill credit is
  **exact** (the killing blow, as the server simulates it — poison/burn kills count for their caster, a mob
  stealing the kill doesn't credit a player, phoenix-feather saves don't count).
- **Co-op mod handshake** — when a client joins, their Content Forge reports its loaded packs to the host; the
  host chat-warns about **missing packs, version mismatches, or unmodded players** (who also can't take part in
  PvP). No more guessing whether everyone installed the same files.
- **Unique NPC bodies** — an NPC's `tint` (`[r,g,b]`) and/or `sheets` (1:1 body repaint PNG) give each talking
  character its own look instead of the shared satyr. The bundled **`examples/worldoftales/`** pack shows it
  all off: build the Storyteller's Arch → a dark realm with its own music, a violet Keeper of Tales (localized),
  a fetch quest and a craftable charm.
- **Talking placeables & portals** — more `controller` presets: `forge_dialogue` (a placeable that runs a Yarn
  conversation — talking statues, gravestones, magic mirrors), `forge_prop` (pure decor), and **`forge_portal`**
  (interact → teleport into one of your custom maps from anywhere in the overworld, exit returns you beside it).
- **Custom skills** — a `skills` block adds real entries to the **skills screen** (icon, localized name,
  levels 0–100, favour points on level-up). Wire them in via a weapon's `skillId` (hits train the skill and
  its level scales that weapon's damage) or a spell's `attack.skillId`. Existing characters pick new skills up
  automatically.
- **More spell behaviors** — beyond projectiles: `multiProjectile` (a fan), `aoe` (lingering burn/poison pools),
  `explosion` (instant blast — great in a `chain`), `heal` / `massHeal` (self or party, server-authoritative),
  plus experimental `cone` and `line` (breath weapons).
- **Spell requirements** — a spell's `requires` block gates casting on **skill level, favour, finished quest, or
  class** (any combination), with a custom red denial message. "Bolt Tome needs Runeology 3" just works.
- **Player skins (`playerSkins`)** — new **hair styles in character creation**, 1:1 **reskins** of vanilla
  character parts, and brand-new **worn looks** (`looks` + `parts`) that items wear via `equippable.displayId` —
  your armor can finally *look* like your armor. Dyeable through vanilla or pack palettes.
- **Map atmosphere** — maps take `music`, `ambience`, `musicVolume` and `ambientLight` `[r,g,b]`: give your
  realm its own soundtrack and a permanently dark (or golden) sky.
- **Summons survive reloads** — guardian/companion tracking (teleport-to-owner, dismiss-on-unequip, caps) is
  rebuilt when a save loads, so summons keep behaving after you quit and continue.
- **No phantom damage on your own summons** — hitting your own summon shows no damage numbers, plays no hit
  sounds, and doesn't drop its health bar (the predicted hit is skipped); in co-op the hit isn't relayed either.
- **Playable classes** — a `classes` block adds a profession to character creation (skill head-start + starting
  gear/favour). The **`examples/necromancer/`** pack ships a Necromancer class, a 2-skeleton book, staff, robe, recipe.
- **Chained spells (composed effects)** — a spell's `chain` casts other spells when it hits/ends: an arrow that
  spawns lightning on impact, a fireball that also ignites. Each chained spell is just another spell id.
- **Multi-language text** — `nameLocalized` / `descriptionLocalized` (and spell `displayNameLocalized`) give an
  item's name/description per language; the mod follows the game's current language and falls back to the plain
  name. The GUI editor edits translations per language, and the editor's own UI is translated too (EN/TR/DE/ES).
- **Loot drops** — `drops` adds your items to mob/chest loot tables with a % chance (e.g. a 5% drop from
  `enemy:fallen_sword`). Server-authoritative; works in co-op when the host runs the mod.
- **Custom VFX** — `vfx` lets a pack ship sprite-animation effects (impacts/bursts/auras): a PNG frame sheet
  + a frame list. Register a new id, or a vanilla id to **reskin** an existing effect.
- **Custom sounds** — drop FMOD `.bank` files (built in FMOD Studio) in the packs folder; they load at startup
  and their `event:/...` ids work anywhere a sound is taken (e.g. spell `castSound`).
- **Pack manifest + namespacing** — packs declare `id` / `namespace` / `author` / `version` / `dependencies`.
  Ids are checked against the namespace (collision warnings), dependencies are resolved (missing dep → the pack
  is skipped, not a crash) and load in order, and a load summary lists every pack + version for co-op checking.
- **Custom palettes** — `palettes` lets a pack ship its own dye ramp (PNG) and recolor held looks beyond the
  vanilla metal rows (e.g. a true fire red), via `display.palette` / `paletteRow`.
- **Custom held looks** — reuse a vanilla model (`displayId`), **compose** one from an existing shape +
  palette recolor (`display`), or inject **your own held sprite sheet** (`display.sheet`, a 1104×240 rig-aligned PNG).
- **Full weapon stats** — beyond damage/knockback/range: `stunPower` (stagger on hit), `movementFactor`
  (move-speed while swinging — slow heavy weapons), `damageVfx` (hit-impact size), `specialEnergyCost`, plus on
  `castSpell`: `probability` (proc chance, e.g. 25% ignite), `target` (`Self` for heal/buff-on-hit), and
  `chargedSpellId`/`chargeTime` (a stronger charged attack). **Stat bonuses also work on weapons** (+crit / +health
  while held).
- **Weapon swing-trail** — `trailColor` recolors the slash VFX (e.g. a fiery slash); `trailSheet` replaces
  its **shape** with your own repainted slash sheet (clones the vanilla trail spac and swaps the art, so every
  attack variant works); `hideTrail` suppresses it entirely (for a staff/wand that doesn't melee). Combine
  `trailColor` + `trailSheet` to tint a custom shape.
- **Staff fix** — an On-Use weapon (staff) that fired a projectile cast it twice in single-player; now casts
  once. (Co-op: a non-host caster's On-Use projectile is local-only.)
- **Recipe visibility fix** — pack recipes now show in the crafting menu automatically (the game only lists
  *known* recipes, not the recipe database). Quest-reward recipes stay hidden until their quest completes.
- **Summon skull fix** — when a summoned skeleton dies, the jumping skull the game spawns from it now stays on
  **your** side instead of attacking you.
- **Summon friendly-fire guard** — your own attacks no longer damage your summons (enemies still can).
- **XNB→PNG extractor** (`tools/xnb2png.ps1`) — dumps any uncompressed Color sprite sheet to PNG (recurses a
  whole folder) for tracing custom held-weapon or trail art. Local-only tracing templates; ship your own repaints.
- **Consumables** — food/potions that heal on use (instant + over-time).
- **Light sources** (torches) and **crafting recipes** (make items craftable at a station).
- **Co-op-safe** (host runs the mod; ids must match) and **save-safe** (removed packs become harmless
  placeholders). Names/descriptions register under the game's own key convention so tooltips show correctly.
- Ships a working **sample pack** + demo custom icons on first run, and a **validator** — cross-platform
  `python editor/validate.py` (also the editor's in-process Validate button) plus the .NET `cfvalidate` CLI.
