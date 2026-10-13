# Romestead Content Forge

**Add your own items, weapons, creatures, dungeons and more to Romestead with JSON content packs — ready-made AI and behaviors included, C# optional. No game files edited.**

> **Version 1.1.0 — works with the Romestead 2026-10-04 update, including dedicated servers. Provided as-is — fixes when I have time, no promises.**
> I made this for myself and may fix things now and then, but I can't promise updates or a timeline. It's experimental: much of it is tested, some
> features aren't, and future game updates can break things. The full source is on
> [GitHub](https://github.com/kvotheous/RomesteadContentForge) (MIT license) — anyone is welcome to fork it, fix it or build on it.

Content Forge is a modding framework: drop a `.json` "content pack" in a folder and it injects brand-new content straight into the game's databases at load. Your creations behave like real game content — they spawn, equip, deal damage, heal, craft, and show up in the **Item Spawner** automatically.

## Easiest way to start: the Forge Editor

Don't want to write JSON by hand? Download **`ForgeEditor-<version>.zip`**, unzip, and run **`ForgeEditor.exe`** (no Python, no install):

- **Every game value is built in** — items, creatures, icons, sounds, effects and more are picked from dropdowns, so you never guess an id.
- **Forms for every field**, with PNG previews and per-language text.
- **Built-in dialogue editor** — write NPC conversations (Yarn) with syntax highlighting, a node outline, error checks, and a side-by-side translation grid that tracks how much is translated.
- **One-click validation** catches mistakes before you launch the game.
- It saves the same `.json` packs the mod reads, so you can switch between the editor and hand-editing anytime.

---

## More than an injector: built-in building blocks

It's not just a content injector: Content Forge ships ready-made systems your packs can use by name.

- **Creature AI**: **8 ready-made AI brains**: guard, coward, ranged caster, patroller, pack hunter, summoner, healer, turret. Give any creature one by name.
- **Interactive objects**: **ready-made behaviors** for placeables: crafting stations, portals, healing shrines, levers, talking objects (dialogue), props and multi-part buildings, plus a data-driven animation controller for fully custom creatures.
- **World events**: **wave defense, boss rush, hunt contest, scheduled raids, team battles, king of the hill, races** and PvP duel arenas, all configured in JSON.
- **Dialogue commands**: NPC conversations can **give items, take payment and heal** (`ForgeGiveItem`, `ForgeTakeItem`, `ForgeHeal`).
- **C# when you want it**: addon DLLs can register their **own behaviors and AI** with one call, and any pack can then use them by name (see [Advanced](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/ADVANCED.md)).
- **Worked examples**: **3 example packs** (a story world, a necromancer class, and a test pack covering every feature) plus a **cookbook** of copy-paste recipes.

---

## What you can make

- **Items** — anything: name, description, icon, tier, stack size, sell/buy price.
- **Icons** — reuse any in-game icon, **or import your own `.png`**.
- **Gear** — helmets/armor/boots/trinkets/capes with **stat bonuses** (health, crit, move speed, damage, resistances…).
- **Weapons (full stats)** — **melee** (swords, spears, daggers, sledgehammers, fists) and **ranged** (bows, crossbows, javelins): damage by element, knockback, reach, **stagger/stun**, **move-speed-while-swinging**, **charged heavy attacks**, **proc chance** (e.g. 25% ignite), on-hit auras, and **+stat bonuses while held**.
- **Shields** — custom block strength and arc.
- **Spells, scrolls, wands & staves** — projectile/status spells cast by an Offhand **spell-tome** *or* a melee weapon that fires **on swing** (a staff with a real animation) or **on hit** (ignite/poison weapons).
- **Composed spells** — a spell can **chain** others on impact: an arrow that spawns lightning, a fireball that also ignites. Chains nest for multi-stage effects.
- **Custom held looks** — reuse a vanilla model, **recolor** one with a custom **palette** (dye), or paint your **own held sprite sheet**.
- **Swing trails** — recolor the slash, replace its **shape** with your own art, or **hide** it (for staves).
- **Custom VFX** — your own sprite-animation effects (hit flashes, bursts, auras), or reskin a vanilla effect.
- **Custom sounds** — reuse the game's audio, or just ship your own **wav/ogg/mp3/flac** files (FMOD banks also supported for advanced setups).
- **Loot** — add your items to **mob/chest drop tables** with a % chance — or straight onto a **creature** (wildlife included: bears and wolves can finally drop your items).
- **Summons (necromancer)** — a spell can spawn creatures that **fight for you** (skeletons, skulls, wolves…), with a per-cast **count** and a **max-active cap** (oldest despawns). Set each summon to **guardian** (stays put) or **companion** (blinks to your side when you wander off).
- **Playable classes** — add a **profession to character creation** (skill head-start + starting gear/clothes/favour). The bundled **Necromancer** example ships a class, a 2-skeleton book, a staff, a robe and a recipe.
- **World quests** — auto-activating **kill/collect/loot/craft/harvest** quests that **unlock crafting recipes** when finished — no NPC or dialogue needed.
- **Placeable furniture** — buildable **decorations in the construction menu**, reusing vanilla art with your own name, icon, build cost and footprint. The recipe auto-unlocks so it's always available.
- **Custom creatures** — clone a vanilla creature into an **elite/recolored variant** (new id, **more health**, **color tint**, custom name) reusing its AI, then summon it like any creature.
- **Brand-new monsters from your art** — **from-scratch sprite animations** (no vanilla art at all) plus **8 built-in AI brains** (guard, coward, ranged caster, patroller, pack hunter, summoner, healer, turret): a fully custom creature from one PNG.
- **Custom dungeons & worlds** — author maps in the **free Tiled editor**: **multi-room dungeons** (doors link rooms) with an **overworld entrance** new worlds roll like any vanilla one — or **retrofit into your EXISTING save** — plus per-map **music, ambience and ambient light**. No game tools needed.
- **Portals** — a placeable that **teleports players into your custom world** from anywhere (and back).
- **Dialogue NPCs** — talking characters in your maps, written as plain-text **YarnSpinner** files (lines, options, conditions, portraits) with per-language translations. Dialogue can **check your bag, take payment, give items and heal** — merchants, quest-givers, lore drops.
- **Interactive placeables** — healing **shrines**, **levers**, **talking statues/gravestones**, props and portals: server-authoritative, save-persistent, optional **indestructible** flag for quest temples.
- **Custom art everywhere** — placeables can ship **their own sprite** (with ambient animation), and custom creatures accept **fully repainted animation rigs**.
- **Player skins** — new **hair styles in character creation**, reskins of vanilla character parts, and brand-new **worn armor looks** your items can wear (dyeable via palettes).
- **Custom skills** — real **skills-screen entries** (icon, localized name, levels 0–100, favour on level-up) trained by your weapons and spells.
- **Favour-tree nodes** — new nodes in the gods' favour tree (stat bonuses / auras / recipe unlocks), **uninstall-safe** (removing a pack auto-refunds the point — never blocks login).
- **Forge tab** — a "Forge" toggle in the Skills & Favours window lists installed packs, pack skills and learned pack favours.
- **Custom favour screen** — packs can ship their **own fullscreen favour tree** (vanilla look: draggable grid, connection lines, click to unlock) behind a "Pack Favours" button in the Skills & Favours window.
- **Announcement banners** — event/arena/mod messages as **big red text top-center** (or the classic toast) — style, color, position, duration configurable in `RomesteadContentForge.cfg`.
- **Drop-in sounds** — put a `.wav`/`.ogg` next to your pack and use it for spell casts, shrines, portals. No audio tools needed.
- **Swing-arc control** — melee weapons can sweep any cone width (a 300° whirl-cleaver is one line of JSON).
- **Player body rigs** — tint or repaint the player's animated body itself, every frame.
- **More spell behaviors** — multi-shot fans, **lingering AoE pools**, **explosions**, self/party **heals** — and **casting requirements** (skill level, favour, quest, class) with a custom denial message.
- **PvP zones & duel arenas** — flag a map `pvp: true` and players can fight inside it (no item loss on death there; player damage stays blocked everywhere else). Bind an **arena** to it: kill announcements in chat, timed matches, an item prize for the winner.
- **Co-op mod handshake** — joining clients report their installed packs; the host gets chat warnings about missing packs, version mismatches, or unmodded players.
- **World events** — wave-defense arenas, boss rushes, timed hunt contests, scheduled vanilla raids — plus **team battles**, **king-of-the-hill** and **checkpoint races** — all from JSON.
- **Custom citizens** — new town workers with fixed names, looks and job mastery; edit vanilla citizen types too.
- **Town workshops & work recipes** — clone a vanilla job-building into your own buildable workshop and teach its workers **new recipes** (a Grand Bakery that bakes *your* bread).
- **Multi-part buildings** — compose several sprites, each with its own collision box, into **ONE buildable monument** with walkable gaps.
- **Deep weapon feel** — per-combo-step damage/arc/timing, **two-handed weapons**, custom effect animations, retimed player animations — and a tool that turns **one drawing into a full held-weapon sheet**.
- **Custom crafting stations** — your placeable opens its own crafting window with its own recipes.
- **Pre-placed map props** — campfires, chests, any entity or your own PNG (with collision) directly in your maps.
- **Visual asset catalog** — one command generates a browsable HTML of every game sprite + all referenceable ids.
- **Quest item rewards** and **vanilla-dungeon interior replacement**.
- **Crops & farming** — plantable seeds with growth stages; farmsteads auto-farm them.
- **Shops** — new stock sections at the Market.
- **Custom particles & emitters** — your own particle textures and emitter shapes for torches, shrines, auras.
- **Multi-language** — give names/descriptions per language; follows the player's game language. NPC dialogue translates too.
- **Consumables** (food/potions), **light sources** (torches), and **crafting recipes**.

Many packs coexist safely: each declares an **id / namespace / version / dependencies**, so different authors' content never collides.

---

## How it works

1. Install the mod. On first run it creates `BepInEx\config\RomesteadContentForge\packs\` with a **working sample pack** and a demo custom icon.
2. Edit or add `.json` files there (and drop `.png` icons alongside).
3. Launch — your content is in the game. Spawn it via **Item Spawner (F1)**, craft it, or find it where you placed it.

Full authoring guide with every field, valid values, stat keys, and damage types: see **`docs/PACKS.md`** in the Authoring zip (also on [GitHub](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/PACKS.md)).

A taste:
```json
{
  "id": "mymod", "namespace": "mymod", "author": "you", "version": "1.0.0",
  "items": [
    { "id": "mymod:flameblade", "name": "Flameblade",
      "nameLocalized": { "tr_TR": "Alev Kılıcı" },
      "icon": "sword:iron", "tier": 5,
      "equippable": { "equipmentType": "Weapon", "displayId": "cdd:iron_sword",
        "weapon": { "class": "Sword", "minDamage": { "pyro": 80 }, "maxDamage": { "pyro": 120 } } } }
  ]
}
```

**Prefer not to write JSON?** A **visual editor** (`ForgeEditor.exe`, or its Python source in the Authoring zip) — dropdowns for
every id, PNG previews, per-language text, and one-click validation. It writes these same packs.

---

## Documentation

All docs ship in the **Authoring zip** (`docs/` folder) and are also readable online on GitHub:

- [Getting started](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/GETTING_STARTED.md) — your first pack in a few minutes.
- [Pack reference (PACKS.md)](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/PACKS.md) — every field, valid values, stat keys, damage types.
- [Cookbook](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/COOKBOOK.md) — ready-to-copy recipes.
- [Id catalogs](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/REFERENCE.md) — every referenceable game id.
- [Advanced](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/ADVANCED.md) — C# controllers and AI, addon DLLs, art extraction, FMOD banks.
- Changelogs: [mod](https://github.com/kvotheous/RomesteadContentForge/blob/main/CHANGELOG.md) · [authoring kit](https://github.com/kvotheous/RomesteadContentForge/blob/main/docs/AUTHORING_CHANGELOG.md) · [editor](https://github.com/kvotheous/RomesteadContentForge/blob/main/editor/CHANGELOG.md).

---

## Requirements

- **Romestead**
- **Romestead BepInEx Mod Loader** (BepInEx 6 for .NET/CoreCLR) — required dependency. Install it first.
- **Romestead Item Spawner** (recommended) — to easily spawn and test your created items.

---

## Installation

**Which file do I download?**
- **`RomesteadContentForge-<version>.zip`** — the mod itself (`BepInEx\plugins\` DLLs). **Everyone needs this** — both players installing packs and pack creators.
- **`RomesteadContentForge-Authoring-<version>.zip`** — the **creator kit**: all docs (field reference, cookbook, getting-started), the editor's Python source (for the ready-to-run app, get `ForgeEditor-<version>.zip`), art/asset tools, worked example packs and 17 id catalogs. Only needed if you **create** packs; unzips anywhere (nothing installs into the game).
- **`ForgeEditor-<version>.zip`** — optional standalone GUI editor (`ForgeEditor.exe`, no Python needed) for creators who want just the editor.
- **`RomesteadContentForge-Server-<version>.zip`** — only for a **dedicated server** (see *Dedicated servers*); players don't need it.

Each download includes `THIRD-PARTY-NOTICES.txt` — the licenses of the open-source libraries it bundles.

1. Install the **Romestead BepInEx Mod Loader** and confirm it works.
2. Download the mod zip and extract it **into your Romestead game folder** (the archive has the correct `BepInEx\plugins\` layout).
3. Launch once — the sample pack appears in `BepInEx\config\RomesteadContentForge\packs\`.
4. Edit the sample or add your own packs, then relaunch.

---

## Dedicated servers

Content Forge has a separate **dedicated-server build**, `RomesteadContentForge.Server.dll` — the normal mod can't load on a headless server (there's no game client there).

1. **Set up BepInEx on the server** — extract the *Romestead BepInEx Mod Loader* next to `Server.exe` (leave out `d3d11.dll`, the client's hook).
2. **Extract `RomesteadContentForge-Server-<version>.zip`** into the server folder: the DLL goes to `BepInEx\plugins\`, and `start-modded-server.bat` lands next to `Server.exe`.
3. **Copy your packs** to the server's `BepInEx\config\RomesteadContentForge\packs\` — the **same packs every player has**.
4. **Start the server with `start-modded-server.bat`** (not Steam's Play button). The log shows *"Romestead Content Forge (Dedicated Server) … loaded"* and *"loaded N pack(s)"*.

Players keep the normal mod and the same packs. The co-op handshake warns in chat about missing or mismatched packs, as on a player host.

- **Works on a dedicated server:** items, spells, recipes, creatures + AI, NPCs, maps and portals, events and arenas, crafting stations, placeables.
- **Host-only:** the dialogue commands `ForgeGiveItem` / `ForgeTakeItem` / `ForgeHeal` (dialogue still opens and talks; these three need a player host).
- Using the other kvotheous mods on the same server? Add the **Kvotheous Dedicated Server Kit** — same loader, same `start-modded-server.bat`.

---

## Notes & Compatibility

- **Game updates:** built for the Romestead **2026-10-04** update. If a later update breaks part of the mod, that feature switches itself off (the BepInEx log shows `Patch ... failed`) and the rest keeps working.
- **Co-op:** the **host** must run the mod (content is server-authoritative); ideally all players run it with the **same packs** so everything matches. Against an unmodded host, modded content won't be present.
- **Dedicated servers:** supported with the separate server build — see *Dedicated servers* above.
- **Save-safe:** if you remove a pack, its items turn into harmless placeholders in old saves — no crashes.
- **Custom icons** should be authored at **18×18** (the game's item-icon size) with a transparent margin.
- **Custom VFX** (sprite-animation effects) are supported; **custom audio** is plain files — point a pack's `sounds` at `wav`/`ogg`/`mp3`/`flac`, no FMOD tooling needed (FMOD banks are also supported for advanced users).
- Pairs with the author's other Romestead mods (Item/Building/Citizen/Creature Spawners, Map Travel, Cheat Menu, QoL).

---

## For modders

Content packs are plain JSON — hand-edit them, use the **visual editor**, or run the **`cfvalidate`** CLI to check a pack before launch. The format is the single source of truth, mirrored by the editor's schema.

**Source code:** [github.com/kvotheous/RomesteadContentForge](https://github.com/kvotheous/RomesteadContentForge) — MIT licensed. Fork it, fix it, or send a pull request. There's also an unfinished **Werewolf Village** game mode (social deduction: wolves, villagers, a seer, its own village map and art) on the `werewolf` branch, if anyone wants to pick it up.

---

## Credits

- **kvotheous** — mod author.
- **IceBoxStudio** — Romestead BepInEx Mod Loader.
- **BepInEx Team** — framework and HarmonyX.
- The **Romestead** devs for a moddable game.

---

*Forge responsibly — and share what you make.*
