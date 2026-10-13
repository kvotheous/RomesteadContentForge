# ✅ Content Forge — Test Bench

**[« Docs index](../README.md)**

Step-by-step walkthroughs for every open test: [TESTPLAN.md](TESTPLAN.md)

_What this is: the T## checklist — what's tested, what's open, and how to test each feature. What this isn't:
authoring docs (see [PACKS.md](PACKS.md)). Docs index: [README.md](../README.md)._

`examples/test/test.json` (namespace **`cftest`**) is one pack exercising **every** framework feature; items
are named `T## …` so they match the rows. Get items via the **Item Spawner** (F1) → search `cftest:`.

**Status: T01–T39, T41–T45, T48–T51(core), T54, T62–T64, T69–T72, T74, T76–T79, T89 ✅. Open: T46/T47/T80/T86 (co-op), T52–T53, T55–T61, T65–T68, T73, T75 (confirm loop), T81–T85, T87–T88, T90 (retest) — walkthroughs in TESTPLAN.md.**

## Master checklist (T01–T90)

| # | Feature | How it was/is tested | Status |
|---|---------|----------------------|--------|
| T01 | Item + multi-language name/desc | spawn relic, switch language → name AND desc switch | ✅ |
| T02 | Custom PNG icon | sample pack's Forge Sigil shows custom 18×18 icon | ✅ |
| T03 | Stat-bonus gear | trinket: +100 HP, +25% speed (percentage stats are fractions) | ✅ |
| T04 | Melee weapon + stun | sword: damage, knockback, visible stagger | ✅ |
| T05 | Ranged weapon | crossbow fires piercing bolt | ✅ |
| T06 | Shield | blocks within its frontal arc | ✅ |
| T07 | Spell + scroll/tome | offhand scroll casts projectile | ✅ |
| T08 | Staff (cast on swing) | fireball per swing, no trail, no double-cast | ✅ |
| T09 | On-hit effect | flame sword ignites enemies | ✅ |
| T10 | Chained spell | arrow spawns fireball at impact (passes through small enemies = by design) | ✅ |
| T11 | Summon: guardian + count + cap | 2/cast, stays put, caps at 4 (oldest despawns); death-skull stays allied | ✅ |
| T12 | Summon: companion | teleports to your side when idle + far | ✅ |
| T13 | Custom creature | purple 3×-health Bone Lord, skeleton AI | ✅ |
| T14 | Consumable | ration heals 50 instantly | ✅ |
| T15 | Light source | torch equips in torch slot, emits light | ✅ |
| T16 | Recipe (auto-known) | T16 gem craftable in inventory panel (vanilla basics are tutorial/station-gated — not a bug) | ✅ |
| T17 | Loot drop | plains supply chests contain the relic | ✅ |
| T18 | World quest | kill 2 bears → relic recipe unlocks (quest-reward recipes stay hidden until earned) | ✅ |
| T19 | Playable class | Tester class: meat → hotbar, sword equipped | ✅ |
| T20 | Basic placeable (vanilla art) | 3×3 statue buildable from Decorations menu | ✅ |
| T21 | Custom dungeon room (`maps`) | entrance rolls in new worlds; room renders (needs a visible art layer); exit works | ✅ |
| T22 | dismissOnUnequip | buddy skeletons vanish when book unequipped | ✅ |
| T23 | No phantom damage on own summons | no numbers/sound/health-drop when hitting them | ✅ |
| T24 | Shop section exists | merchant offers the cftest wares section | ✅ (folded into T26) |
| T25 | Multi-room dungeon (doors) | T21 north doorway ↔ T21b inner room, both ways | ✅ |
| T26 | Shops (buy/sell) | market sells relic/ration/sword/seeds; sell pays ~20% | ✅ |
| T27 | Weapon hitVfx | big dust cloud at impact point on every T04 hit | ✅ |
| T28 | Custom crop | seeds → 4 visibly distinct stages (vanilla sheets = 4-frame blocks per stage) → harvest + seed back | ✅ |
| T29 | Custom particle emitter | zap projectile trails glowing orange embers | ✅ |
| T30 | Interactive placeable | shrine: tooltip on hover, message + 30 HP heal on interact, 10s cooldown, **survives save/reload** | ✅ |
| T31 | **Dialogue NPC** (Yarn) | satyr in the T21 room → Test Hermit dialogue, options, name plate (speaker-prefix lines) | ✅ |
| T32 | **Forge dialogue commands** | blessing heals 25; coal trade takes 2 coal → gives relic; refuses without coal | ✅ |
| T33 | **NPC translations** | game in Türkçe → hermit speaks Turkish, named "Test Münzevisi" (#line tags + tr_TR.csv + actorsLocalized) | ✅ |
| T34 | **Talking placeable** | "T34 Talking Stone" built from menu → speaks via its own .yarn; blessing heals 15 | ✅ |
| T35 | **Indestructible placeables** | demolish attempt on the Talking Stone | ✅ blocked; shrine demolishes cleanly |
| T36 | **Custom-sprite placeable** | "T36 Forge Crystal" | ✅ (after fixing the sprite-only controller defaulting that silently skipped it) |
| T37 | **Custom creature** | Rotter Book summon | ✅ green-tinted skeleton (the repainted-sheet art was removed from the repo: it was derived from vanilla art) |
| T39 | **Custom skill** | Runeology in the skills screen, trained by the T04 sword | ✅ |
| T40 | **World atmosphere** | RETEST after fix (atmosphere `music`/`ambience` take **track ids** or `event:/` paths now — the placeholder "music not available" loop is fixed): enter the realm → dungeon music + crypt ambience + ink-purple dark. Keeper loop: 3 wood sticks → ember → quest completes → craft the **Storyteller's Charm** | 🔁 |
| T45 | **Unique NPC bodies** | the Keeper of Tales is **violet-tinted** (npc `tint`), not a plain satyr; `sheets` (full repaint) follows the same path | 🔁 |
| T41 | **Portal (from anywhere)** | Crypt Portal built in the overworld → teleports into the T21 room; exit returns beside the portal | ✅ |
| T43 | **SpellsPlus (magic expansion)** | Fire Lob (burning pool), Heal (30 over time), Bolt fan | ✅ |
| T44 | **Spell requirements** | Bolt at Runeology 0 → red deny message; at Runeology 3 → casts | ✅ |
| T42 | **Player skins** | **T42 Emerald Plate** renders olive-green metal (metal palette row 6 — NOTE: metal palette is 5×7, rows 0–6 only) ✅ | ✅ |
| T46 | **PvP arena + duel match** (needs co-op, 2 players) | build the **T46 Arena Portal** (1 stone) → both players enter → hit each other: **damage lands** (it never does outside) → kill: chat announces "X slew Y in the Test Colosseum", **no grave** (inventory kept), match winner gets the **Champion's Belt**. Outside the arena player damage still blocked | 🔁 |
| T47 | **Mod handshake** (co-op) | modded client joins → host log shows its pack list; client with a missing/mismatched pack → chat warning from "Content Forge"; vanilla client → "no Content Forge" warning + can't fight in T46 | 🔁 |
| T48 | **Swing-arc tuning** | T48 Wide Cleaver: 300° cone hits beside/behind (vs T04's frontal 144°). Note: the visual trail stays the class art (cosmetic; hide or reskin via `hideTrail`/`trailSheet`) | ✅ |
| T49 | **Raw audio (sounds map)** | zap scroll plays the custom ding through the SFX slider | ✅ |
| T50 | **Player body rig** | paste into a pack: `"playerSkins": { "rig": { "tint": [80, 255, 80] } }` (no art needed) → relaunch: **your character AND co-op players render green-bodied**; **citizens unchanged**; clothes/hair/held items unchanged; char-creation preview stays vanilla (known v1 limit). For repaints: `"sheets": {"placeholder": "x.png"}` → log prints the real sheet names + sizes | 🔁 |
| T51 | **Favour-tree node + uninstall safety** | favour tree shows **T51 Runeheart** attached under **Woodcutter's Edge** (`minerva_favour:lumber_axe_power`, top row); unlock parent → node → **max Health +25**; SAVE+QUIT → relog works, node still learned (the save holds NO modded favour — only a `forge:favour:` flag + the refunded point); uninstall test: remove the pack → **login still works**, point refunded; re-add → learned again | 🔁 |
| T62 | **Custom palette (dye ramp)** | held blade renders in the pack's fire colors | ✅ |
| T52 | **Custom citizen** | recruit a jobless wild wanderer (or add-citizen cheat) → chat "**Anna the Forged** has arrived" (weight 10000 ≈ always); town overview shows her name; assign to a forge → blacksmith level 4 from day one; her look is pinned (female, fixed hair/tunic colors), same after save/reload | 🔁 |
| T53 | **Citizen override** | paste into test.json: `"citizenOverrides": { "citizen:generic": { "weight": 0 } }` → EVERY new arrival is Anna; remove after test | 🔁 |
| T54 | **Per-combo-step tuning** | verified vs Medusa: 16 / 35 / 96 damage = 0.5× / 1× / 3× of her ~32 resisted base; 3rd hit sweeps behind (300°) | ✅ |
| T55 | **Player animation remix** | `_temp_t55.json` is in the packs dir → first sword swing animation plays at HALF speed (visual only — damage timing unchanged); delete the file after | 🔁 |
| T56 | **Quest item reward** | finish the T18 bear quest (kill 2 bears) → **2× T01 relic appear in the bag** on auto turn-in + recipe unlock as before; relog → no duplicate grant | 🔁 |
| T57 | **Wave defense event** | enter the T46 arena (portal) → chat + a red TOP-CENTER banner "Event — T57 Waves begins" (configurable: [Notifications] in RomesteadContentForge.cfg) → 2 FallenSword spawn in a ring around you → clear → wave 2 (3 mixed) → clear → "conquered!" + **T57 Trophy**; leaving mid-wave despawns mobs and re-arms | 🔁 |
| T58 | **Boss rush event** | enter the T21 room → one FallenCenturion at a time → kill → Medusa → kill → trophy; never two at once | 🔁 |
| T59 | **Hunt contest** | with 1+ player online a 2-min hunt announces; kill any creatures; at time-up "X wins with N kills!" + trophy to the winner; re-announces after 60s cooldown | 🔁 |
| T60 | **Scheduled raid** | sleep to next day → chat + vanilla raid UI: rat raid marches on your nearest town (war-declaration style, `raid:rats` = trivial); next day NO raid (everyNDays 2); the day after → raid again | 🔁 |
| T61 | **Vanilla room replace** | NEW world: find a forest **Animal Den (bear)** entrance → inside is OUR test room (vanilla name on the map); if the log warns about exitDirection, report the value | 🔁 |
| T63 | **Custom spac (zero-art)** | every T48 hit flashes the slow-motion slash at the impact point | ✅ |
| T64 | **Two-handed weapon** | eject to bag + bounce-back + newest-equip-wins all verified; eject message is now a localized red top-center banner (configurable) | ✅ |
| T65 | **Custom crafting station** | craft+build the **T65 Runeforge** (2 stone) → press E → crafting window titled "T65 Runeforge" lists ONLY the Glowing Rune recipe; the rune does NOT appear in basic crafting/campfire/war-table; crafting consumes 1 wood stick; no "Invalid crafting station" warning in the log | 🔁 |
| T66 | **Map doodads (pre-placed props)** | enter the World of Tales realm → a **burning campfire**, an **openable chest**, and the **crystal sprite** are pre-placed; you can't walk through the crystal (collision) but it can't be attacked/demolished | 🔁 |
| T67 | **Held-sheet generator** (tool, offline) | `python tools/make_held_sheet.py --list-templates` lists shapes; generate a sheet from any PNG → `*_compare.png` rows align with vanilla; wire via `display.sheet` and the weapon renders in hand | 🔁 |
| T68 | **Tiled visual placement** | open a map's tmx in Tiled → object layer `doodads`: a Point named `CampFire` + a Rectangle with property `sprite=crystal.png` (~16×10 box) → both appear in-game at the clicked spots, the rectangle blocking exactly the drawn box; object layer `npcs`: a Point named after an npc id → that npc stands there (overrides json position); hiding an object in Tiled (eye icon) removes it next launch | 🔁 |
| T69 | **Custom creature AI (controller swap)** | **T69 Marksman Tome** → BLUE skeleton keeps distance and fires arrows ✅ (sword-swing pose during the shot is the warrior BODY's only attack anim — cosmetic; the swing itself deals no damage, the arrow is the attack) | ✅ |
| T70 | **AI preset: forge_guard** | GOLD skeleton guards its post, chases in range, walks back (after the summon-faction fix) | ✅ |
| T71 | **AI preset: forge_coward** | GREEN craven flees below half HP, regenerates, re-engages — player hits land directly now | ✅ |
| T72 | **AI preset: forge_ranged** | ORANGE wolf kites + fireballs; ally-projectile friendly fire gone (client faction sync) | ✅ |
| T73 | **Cross-family AI + generic client anims** | give a creature `controller: "wolf"` + `clientController: "forge_creature_anim"` + `controllerParams.spac: "skeleton_animations"` → animations work where they'd freeze; log lists the spac's animation names on first spawn | 🔁 |
| T74 | **Forge tab** | K → Forge tab: packs/skills/favours listed | ✅ |
| T82 | **Map auto-floor** | a pack tmx with ONLY ground/structure layers → room renders with the plain cave floor (not black), collision intact; log: "auto-generated 'cf_autofloor'"; a tmx WITH an art layer ships byte-identical | 🔁 |
| T75 | **AI: forge_patrol** | RETEST/confirm: walking EAST first then around a 192px square loop IS the route (patrol-points relative square) — pass if they loop and pause ~1.5s at corners | 🔁 |
| T76 | **AI: forge_pack** | pack groups up and fights together; one packmate that strays past pack-radius drops out (leash by design). OPEN: dropped loot item appeared ungatherable once — watch for repeat (which item? screenshot/log next time) | ✅ |
| T77 | **AI: forge_summoner** | **T77 Summoner Tome** → purple conjurer kites you, raises a rotter every ~8s (max 3 — kill one, it's replaced), fireballs between | 🔁 |
| T78 | **AI: forge_healer** | Bone Medic heals hurt allies (skeleton death-skull is vanilla, ignore) | ✅ |
| T79 | **AI: forge_turret** | turret holds position and fires | ✅ |
| T80 | **Summon kill credit** (co-op arena) | your summon lands the killing blow on the enemy player → arena chat names YOU (was "not a player") | 🔁 |
| T81 | **Creature drops** | kill a Bear (player killing blow) → it drops the pack item alongside vanilla loot (drops[].creature) | 🔁 |
| T83 | **Retro entrance (existing saves)** | load an EXISTING world (pre-pack) → log "retrofit ... entrance placed at tile X,Y" → walk there: den entrance, enter/exit works; reload → not placed again | 🔁 |
| T84 | **Composite placeable** | build **T84 Grove Shrine** → ONE entry, TWO crystals at offsets; both block walking, the gap between is walkable; save/reload keeps both; demolish removes everything (walk through the spot after) | 🔁 |
| T85 | **Town building + work recipe** | build hammer → Buildings → **T85 Grand Bakery** → place + hammer up → bakery stamps in, citizen slot works; assign a baker → work queue lists **T85 Test Bread** → ingredients drain, bread lands in output, job XP ticks | 🔁 |
| T89 | **Fully custom creature (zero vanilla art)** | wisps render + animate + die (the mound-with-glow look IS the bundled placeholder art — 3-frame 32px gradient blobs; lighting flattens them; supply real PNGs for real packs) | ✅ |
| T90 | **Custom favour screen (`screen: true`)** | RETEST after fix: pack-favour parser rejected parent-less screen roots → root node was missing (only 2 orphan children showed). Now: 3 nodes + lines; children greyed until root learned | 🔁 |
| T86 | **Team battle event** (co-op) | 2+ modded players in the T46 arena → "Team Red vs Team Blue — first to 3 kills" (also as a red top-center banner, max 1/sec); hitting your TEAMMATE does nothing (your client may flash a predicted hit — known cosmetic); 3 kills → team announced, every winner gets a trophy | 🔁 |
| T87 | **King of the hill** | T21 room, stand within ~80px of the hermit spot (160,120) → "takes the hill lead (n/20)" → 20s alone on it → trophy; two players BOTH on it = contested, nobody scores | 🔁 |
| T88 | **Checkpoint race** | T21 room → "race begins in 10s" → run the 4 corners IN ORDER (64,64 → 256,64 → 256,192 → 64,192) → winner line + trophy; idle past 120s = "time's up" | 🔁 |
| T38 | **"Go talk to NPC" quest (token pattern)** | hermit handed the Blessing token | ✅ (token received; confirm the quest ticked complete) |

## Dialogue conventions (learned the hard way)
- Name plates need **speaker-prefixed lines** (`actorid: text`) + `<<Actor actorid>>`; the display name is the
  string registered under the **actor id** (the `actors` map key = the yarn speaker id).
- Dialogue **lines** advance with space; only `->` options are clickable — vanilla behavior.
- Translations need explicit `#line:` tags, and translated texts must include the speaker prefix.
- Forge commands (`<<ForgeGiveItem>>`, `<<ForgeTakeItem>>`, `<<ForgeHeal>>`) act through the in-process server
  (single-player / co-op host).

## Known limitations (not bugs)
- Custom creatures/NPCs: ART fully yours (rig repaints T37, NPC bodies T45) AND the server AI is now
  swappable per creature (`controller` — any vanilla AI or an addon-DLL one, T69). Remaining true limit:
  the CLIENT animation controller stays the base's, so a custom AI must speak the base's animation
  vocabulary (vanilla controllers on a same-family base always do).
- Summon facing after companion teleport is left to the creature's AI; friendly fire on summons is blocked.
- Wildlife has no shared drop table — use enemy/boss/chest tables.
- Vanilla has no stationless recipes; vanilla starter recipes unlock through the tutorial quests.
- Crop sheets use 4-frame blocks per growth stage. (Maps without a visible art layer now get an auto-generated cave floor — T82.)
- Dungeon entrances roll in NEW worlds only (vanilla POI rule); `forge_portal` placeables bypass this (work in any world).
- Co-op: the host must run the mod + packs; map/NPC packs are needed by all players.
- PvP (T46): kill attribution is EXACT — the host's server simulates every killing blow (player hits AND
  player-applied poison/burn ticks carry the player as source; phoenix-feather saves never count). A summon's
  killing blow reads as "not a player" (vanilla stores no summon owner). The last-hit-within-10s heuristic
  remains only as fallback for deaths the server never simulated. Kills are keyed by character name
  (duplicate names merge tallies); a match doesn't survive save/reload (it aborts and re-arms once the
  arena empties). Both fighters must be fully modded (T47 handshake).

## When something fails
Note the `T##`, what you saw vs. expected, and the relevant `BepInEx\LogOutput.log` lines (search the id,
`[CFsummon]`, `creature`, `npc`, `map`, or any `Warning`/`Error`).
