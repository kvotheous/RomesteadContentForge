# 🧾 Content Forge — Test Plan (step-by-step walkthroughs)

**[« Docs index](../README.md)**

_What this is: a per-test walkthrough for every OPEN row in [TESTING.md](TESTING.md) (status 🔁) — exact steps,
where each item comes from, what passes, what falsely looks like a fail, and how to re-run a test in a
lived-in save. What this isn't: the checklist itself (TESTING.md) or the field spec (PACKS.md)._

## Universal prerequisites (read once)

- **Packs installed:** copy **ALL files** of `examples/test/` AND `examples/worldoftales/` into
  `…\romestead\BepInEx\config\RomesteadContentForge\packs\` (json + png/tmx/yarn/csv/wav — not just the json).
- **Relaunch after ANY pack file change.** Packs load once at startup; an edited json/tmx/yarn does nothing
  until the game restarts. Stale dialogue = almost always a missed relaunch.
- **Items:** Item Spawner (**F1**) → search by **id** (`cftest:`, `wot:`) — id search works in every language.
  Vanilla ingredients spawn the same way (`wood_stick`, `material:linen`, `material:coal`, `material:flour`).
- **Logs:** `BepInEx\LogOutput.log` — search the `T##`, the content id, or `Warning`/`Error`.
- **Türkçe:** localized entries show TR names while playing Turkish — Keeper = "Masallar Bekcisi",
  ember = "Masal Koru", charm = "Masalcinin Tilsimi", arch = "Masalcinin Kemeri", T74 tab = "Demirhane",
  T39 skill = "Run Bilimi". Most `T##` test items are EN-only; search by id to be safe.
- **Lived-in save:** fine for almost everything. Things an EARLIER session may have left behind are called
  out per test under *Repeat/Reset*. Only T61 prefers unvisited dungeons and T83 fires once per save.
- **Hostile summons (T70–T79, T89, T73):** these tomes spawn creatures that are HOSTILE — summon them in
  open ground away from town/citizens/pets, or they will pick their own fights.

Two recurring places:
- **t21 room** = `cftest:t21_room`, the test chamber. Reached via the **T41 Crypt Portal** (buildable
  placeable, 2 stone, Decorations menu) — interact to enter; walk into the bottom-center exit box to leave.
  The Test Hermit stands at map px (160,120) — that exact spot is also the T87 hill.
- **t46 arena** = `cftest:t46_arena`, the PvP map. Reached via the **T46 Arena Portal** (1 stone).

**Events share maps and fire together** — entering the t21 room arms **T58 boss rush + T87 koth + T88 race**
at once (all `minPlayers: 1`); entering the t46 arena arms **T57 waves** solo and additionally
**T46 duel match + T86 team battle** with 2 players. Every event announces by its own name in chat and pays
the same **T57 Arena Trophy** (`cftest:t57_trophy`) — tell results apart by the chat lines and count trophies.
**T59 hunt** is global and re-announces all session long — its chatter during other tests is normal.

---

## Single-player, quick spawns (18 tests — one session, open ground)

**T42 Player skins (armor) — ✅ PASSED**
- Prereqs: none. (The hair half was removed: its sample sheet was derived from vanilla art.)
- Steps: F1 → `cftest:t42_armor` → spawn **T42 Emerald Plate** → equip in Armor slot.
- Pass: worn chest renders as a differently-tinted metal armour (greenish metal, not the vanilla color).
- Repeat/Reset: any time.

**T49 Raw audio (sounds map) — ✅ PASSED**
- Prereqs: sound effects slider up (not 0).
- Steps: 1. F1 → `cftest:t07` → spawn **T07 Scroll of Zap** → equip OFFHAND. 2. Attack a few times.
- Pass: each zap plays a custom **ding** (`t49_ding.wav` via `cf:sound:cftest:t49`) instead of the vanilla
  cosmo whoosh; volume follows the **sound effects** slider; log has no "Could not find event".
- False-fail: the sound is positional (full volume ~16 px, gone by ~640 px) — listen at your own cast point.
- Repeat/Reset: infinitely; nothing persists.

**T54 Per-combo-step tuning — ✅ PASSED**
- Prereqs: a tanky target (a Bear, or your own T13 Bone Lord won't take your hits — use wildlife).
- Steps: 1. F1 → `cftest:t54` → **T54 Trinity Blade** → equip. 2. Land a full 3-hit chain; watch numbers.
  3. For hit 3, stand with extra enemies BESIDE/BEHIND you.
- Pass: hit 1 ≈ HALF a normal hit, hit 2 normal, hit 3 ≈ 3× spike that also hits beside/behind (300°);
  hits 1–2 stay frontal.
- False-fail: crits and damage ranges (30–40) blur single readings — average a few chains.
- Repeat/Reset: infinitely.

**T62 Custom palette (dye ramp) — ✅ PASSED**
- Steps: 1. F1 → `cftest:t62` → **T62 Ember Sword** → hold it. 2. F1 → `cftest:t04` → hold for comparison.
- Pass: the held T62 blade renders in FIRE colors (pack palette `cftest:fire`, row 10, over the vanilla
  Sword shape) — clearly different from T04's plain iron look. Icon is unchanged (only the held model).
- Repeat/Reset: infinitely.

**T63 Custom spac (zero-art slow-motion slash) — ✅ PASSED**
- Steps: 1. F1 → `cftest:t48` → **T48 Wide Cleaver** → equip. 2. Hit any enemy.
- Pass: every hit flashes a **slow-motion** sword-slash at the impact point
  (`cftest:t63_slowslash` = vanilla `vfx_swordslash` at quarter speed, `frameDurationScale: 4`).
- False-fail: don't confuse it with the weapon's own swing trail — the slow slash plays AT THE TARGET.
- Repeat/Reset: infinitely.

**T64 Two-handed weapon — ✅ PASSED**
- Prereqs: spawn `cftest:t06` (T06 Test Shield) too; have bag space.
- Steps: 1. F1 → `cftest:t64` → **T64 Pyrostaff**. 2. Equip the shield, then equip the staff.
  3. While holding the staff, equip the shield again. 4. Fill the bag and retry. 5. Put 1H sword + shield in
  the OTHER weapon set and swap sets back and forth.
- Pass: (2) shield jumps to the bag; (3) the **STAFF** stows to the bag instead (newest equip wins);
  (4) bag full → item drops at your feet, never destroyed; (5) set swaps cause **no false ejects**.
  Each eject shows a **red "Two-Handed" BANNER top-center** (localized en/tr; configurable in
  `BepInEx\config\RomesteadContentForge.cfg` [Notifications] — Style toast reverts to the old bottom-right toast).
- Repeat/Reset: infinitely.

**T69 Custom creature AI (controller swap) — ✅ PASSED** (sword-swing pose during the shot is cosmetic — warrior body, archer brain; the swing deals no damage, the arrow is the attack)
- Steps: 1. F1 → `cftest:t69_book` → **T69 Marksman Tome** → equip OFFHAND. 2. Cast in open ground.
  3. Lure an enemy to it (it's YOUR ally, `PlayerSummon`).
- Pass: BLUE-tinted skeleton that keeps its distance and **fires arrows** (warrior body, archer brain — there
  is no bow visual; the arrow projectile is the proof). No animation freeze.
- Repeat/Reset: cap 2 active; recast to replace. Earlier sessions' summons are re-tracked on load.

**T70 AI preset: forge_guard — ✅ PASSED**
- Steps: 1. F1 → `cftest:t70_book` → cast once in open ground, REMEMBER the spawn spot. 2. Walk near
  (within ~200 px ≈ 12 tiles) → it chases+melees. 3. Run >160 px from its post.
- Pass: GOLD skeleton wanders near its spawn, chases within ~200 px, then gives up and **WALKS back** to its
  post (no teleport). HOSTILE — it will attack you on sight.
- Repeat/Reset: kill it and recast anywhere; nothing persists across relaunch (summons are save-tracked, so
  in-save it stays until killed).

**T71 AI preset: forge_coward — ✅ PASSED**
- Prereqs: none — hostile summons are now hittable by YOU directly (the friendly-fire gate protects only
  ALLY summons since the summon-faction fix).
- Steps: 1. F1 → `cftest:t71_book` → cast → GREEN hostile skeleton (2× HP). 2. Hit it yourself below half
  health. 3. Back off and watch it from afar. 4. Re-approach when its HP bar refills.
- Pass: below 50% it RUNS away, regenerates when safe, re-engages once healthy.
- Repeat/Reset: kill + recast.

**T72 AI preset: forge_ranged — ✅ PASSED** (faction fix verified — ally summons' projectiles no longer
hit YOU mid-fight; ally factions sync to the client too).
- Steps: 1. F1 → `cftest:t72_book` → cast → ORANGE wolf. 2. Charge it. 3. Flee from it. 4. Put a wall/rock
  between you.
- Pass: it **never bites**: backs off when you charge, follows when you flee, lobs a test fireball about every
  **2 s** from mid-range (~140 px preferred); line of sight broken → it repositions to shoot again.
- Repeat/Reset: kill + recast.

**T90 Custom favour screen (`screen: true` favours) — RETEST after fix** (parser rejected parent-less screen roots: root node was missing, only 2 orphan children showed)
- Prereqs: at least 1 favour point (level up once, or unlock nothing yet this character).
- Steps: 1. Press **K** (Skills & Favours). 2. Click the new **"Pack Favours"** toggle (fourth button).
  3. The fullscreen tree opens: **T90 Forge Attunement** (anvil icon, row 0) with two children below,
  gray connection lines between them. 4. Hover a node → tooltip with effect + "1 favour point" cost.
  5. Click the root → confirm → check appears, +10 max Health. 6. Children un-grey; unlock one.
  7. ESC/close → reopen → state kept. 8. Save+quit → relog → still learned (strip-on-save round-trip).
- Pass: children are GREYED (0.5 opacity, unclickable) until the root is learned; unlock costs exactly
  1 point (counter top-left updates); these 3 nodes never appear in the VANILLA favour tree.
- False-fail: with 0 favour points clicking does nothing (by design — earn a point first).
- Repeat/Reset: canteen respec refunds them like vanilla favours.

**T74 Forge tab — ✅ PASSED**
- Steps: 1. Press **K** (Skills & Favours). 2. Click the third toggle (anvil icon), "Forge".
- Pass: lists installed packs (cf_testbench + worldoftales), pack skills with levels (T39 Runeology),
  and learned pack favours with their effects (T51 Runeheart if learned). In Türkçe the tab reads "Demirhane".
- Repeat/Reset: any time; reflects whatever earlier sessions learned/leveled — that's correct behavior.

**T75 AI: forge_patrol — CONFIRM** (walking EAST first then around the square IS the route — the points are a relative 192px square; pass = full loop + ~1.5s corner pauses)
- Steps: 1. F1 → `cftest:t75_book` → cast in OPEN ground (route needs ~12 tiles of clearance). 2. Watch a
  full lap. 3. Fight it mid-route, then disengage.
- Pass: blue skeleton marches a **square ~12 tiles/side** (192 px), pausing ~1.5 s at corners; after the fight
  it resumes at the **NEAREST corner**, not where it left off.
- Repeat/Reset: kill + recast (route re-anchors to the new spawn point).

**T76 AI: forge_pack — ✅ PASSED** (one packmate straying past pack-radius 240px = leash, by design; OPEN: a dropped loot item looked ungatherable once — note the item if it repeats)
- Steps: 1. F1 → `cftest:t76_book` → cast **THREE** times, 6–10 tiles (96–160 px) apart. 2. Walk up to ONE.
- Pass: aggroing one red skeleton makes the **other two charge in** (pack alert radius 240 px).
- False-fail: spawned too far apart (>15 tiles) they can't hear each other — keep within ~240 px.
- Repeat/Reset: kill all + recast.

**T77 AI: forge_summoner**
- Steps: 1. F1 → `cftest:t77_book` → cast → purple conjurer. 2. Chase it; count its minions. 3. Kill one
  minion and wait ~8 s.
- Pass: it **kites** you, raises a green rotting skeleton about every **8 s** up to **3 alive** (kill one →
  replaced), and throws fireballs between summons.
- Repeat/Reset: kill conjurer + minions, recast.

**T78 AI: forge_healer — ✅ PASSED** (skeleton death spawning a skull is vanilla — ignore)
- Prereqs: two T71 cravens summoned and damaged (use the T69 marksman to hurt them).
- Steps: 1. F1 → `cftest:t78_book` → cast near the hurt cravens. 2. Watch their HP bars. 3. Walk at the medic.
- Pass: white medic shadows the skeletons, heals the **most injured** every ~2 s (visible +15 HP jumps,
  within 200 px), and always **flees YOU**.
- Repeat/Reset: kill + recast; needs freshly-damaged allies each run.

**T79 AI: forge_turret — ✅ PASSED**
- Steps: 1. F1 → `cftest:t79_book` → cast → dark skeleton. 2. Circle it at range; hide behind terrain.
- Pass: it **NEVER walks**; snipes you with fireballs about every **1.5 s** within ~16 tiles (260 px) — but
  only with line of sight.
- Repeat/Reset: kill + recast.

**T89 Fully custom creature (zero vanilla art) — ✅ PASSED** (the mound-with-glow look IS the bundled placeholder art: 3-frame 32px gradient blobs, flattened by lighting)
- Steps: 1. F1 → `cftest:t89_book` → **T89 Wisp Tome** → cast. 2. Watch idle; attack it; kill it.
  3. Check the log.
- Pass: a green pulsing blob (from-scratch `wisp.png` animations) guards its spawn, flashes white on attack,
  freeze+poof death; log lists **"registered from-scratch spac"** with the grid + animation names.
- Repeat/Reset: kill + recast.

---

## Single-player, build & world (11 tests)

**T40 World atmosphere + Keeper loop — RETEST after fixes**
- Prereqs: 4 stone + 2 wood sticks to build; carry **3× `wood_stick`** + **1× `material:linen`**; NO
  `wot:ember` in inventory (see Repeat).
- Steps: 1. Build **Storyteller's Arch** (Decorations menu) → interact → enter the World of Tales.
  2. Listen + look. 3. Talk to the Keeper → "I want an ember" → trade. 4. Walk back out through the exit;
  the quest "A Tale Worth Telling" completes. 5. Basic crafting → **Storyteller's Charm** (1 ember + 1 linen).
- Pass: dungeon music + crypt ambience play (no "music not available" placeholder loop) + ink-purple dim light
  (`[0.4,0.3,0.55]`); Keeper takes 3 sticks → gives 1 **ember** (= `wot:ember`, a quest token the Keeper
  trades for kindling); charm crafts and gives +50 HP / +10% move speed.
- False-fails: already carrying an ember → "the ember you carry still glows" is the BY-DESIGN branch, not a
  missing option (the ember option is now always visible). Don't F1-spawn `wot:ember` — that completes the
  quest without testing the trade.
- Repeat/Reset: the trade repeats whenever you hold ZERO embers (craft the charm or stash the ember, bring 3
  more sticks). The quest + recipe unlock are once per world — if an earlier session finished them, only the
  trade + atmosphere remain testable here.

**T45 Unique NPC bodies — RETEST after saved-NPC repoint fix**
- Prereqs: same realm visit as T40.
- Steps: 1. Stand next to the Keeper of Tales. 2. Compare with the Test Hermit in the t21 room (untinted).
- Pass: the Keeper is **violet-tinted** (npc `tint` [150,60,255]), visibly not a plain satyr. This must hold
  even though this save spawned the NPC in a PRE-fix session (the repoint fix targets exactly that).
- Repeat/Reset: any visit; if still plain, grab the log (`npc`, `NpcBody`) — that's the open suspect.

**T66 Map doodads (pre-placed props)**
- Prereqs: same realm visit as T40.
- Steps: 1. In the World of Tales find: a campfire at px (120,80), a chest at (200,90), the crystal sprite at
  (64,200). 2. Open the chest; walk into the crystal; try to attack/demolish the crystal.
- Pass: campfire **burns**, chest **opens** (empty storage), crystal **blocks walking** (16×10 box) but can't
  be attacked or demolished.
- Repeat/Reset: maps regenerate every launch — chest contents RESET each session (decoration, not storage);
  anything stored there in an earlier session is gone, by design.

**T51 Favour refund check (core ✅ — only the uninstall leg is open)**
- Prereqs: **T51 Runeheart** learned (favour tree under **Woodcutter's Edge**, Minerva, top row; unlock the
  parent first, 1 point each). ⚠ Do this leg while NO T85 bakery stands (demolish it first — a saved world
  containing a pack town building can't load without the pack).
- Steps: 1. Confirm node learned + max HP includes +25. 2. SAVE+QUIT. 3. Move the test pack's files OUT of the
  packs dir. 4. Relaunch → log into the same character. 5. Check favour points. 6. Restore the pack files →
  relaunch → log in.
- Pass: (4) login **works** (the save holds no modded favour — only a `forge:favour:` flag); (5) the point is
  **refunded**; (6) node is **learned again** and the point re-charged.
- Repeat/Reset: repeatable any time; an earlier-learned node is exactly the right starting state.

**T52 Custom citizen**
- Prereqs: a town that can take a citizen; a **jobless** wild wanderer nearby (or the add-citizen cheat).
- Steps: 1. Recruit the jobless wanderer. 2. Read chat + the town overview. 3. Assign her to a forge.
  4. Save/reload and re-check her look.
- Pass: chat: "**Anna the Forged** has arrived" (weight 10000 ≈ always wins the roll); overview shows her
  name; blacksmith **level 4** from day one; look pinned (female, fixed hair/tunic colors) and identical
  after reload.
- False-fail: **skilled (Good/Legendary) recruits bypass the roll** and keep their craft — recruit a JOBLESS
  one. An Anna from an earlier session doesn't block a second Anna (each arrival rolls independently).
- Repeat/Reset: recruit another jobless wanderer any time.

**T56 Quest item reward**
- Prereqs: the T18 bear quest ("T18 Test Hunt") NOT yet completed in this save — it's once per world. If an
  earlier session finished it, this needs a fresh world.
- Steps: 1. Note your `cftest:t01_relic` count. 2. Kill **2 Bears** (forest wildlife). 3. Watch the bag on
  auto turn-in. 4. Relog.
- Pass: **2× T01 Forge Relic** appear in the bag on completion + the relic recipe unlocks; after relog **no
  duplicate grant** (the reward must not re-pay on world load).
- Repeat/Reset: not repeatable in-save (non-repeatable world quest) — that's the point of step 4.

**T61 Vanilla room replace**
- Prereqs: a forest **Animal Den (bear)** entrance you have **never entered** (replacement is consistent only
  for unvisited dungeons). A lived-in save's unvisited den works; otherwise roll a NEW world.
- Steps: 1. Find the den (vanilla name on the map). 2. Enter. 3. Check the log for an `exitDirection` warning.
- Pass: inside is OUR test room (the t21-room layout) instead of the bear den interior; the entrance/POI/map
  name stay vanilla. If the log warns about exitDirection, note the value in the report.
- Repeat/Reset: every unvisited bear den shows it; previously-visited dens may keep their old interior.

**T65 Custom crafting station**
- Prereqs: 2 stone (build) + 1 stone (craft).
- Steps: 1. Build **T65 Runeforge** from the construction menu. 2. Press **E** on it. 3. Craft the rune.
  4. Open basic crafting, a campfire, and the war-table and search the rune.
- Pass: a crafting window titled **"T65 Runeforge"** lists ONLY the Glowing Rune recipe; crafting consumes
  1 stone → 1 **T65 Glowing Rune**; the rune appears in NO other station; log has no "Invalid crafting
  station".
- Repeat/Reset: craft repeatedly; the runeforge persists in the save (it's an interactive placeable — fine as
  long as the MOD stays installed; pack removal alone is also fine for placeables).

**T83 Retro entrance (existing saves)**
- Prereqs: an EXISTING world created before the pack (your lived-in save). Fires **once per save**.
- Steps: 1. Load the world. 2. Search the log for `retrofit`. 3. Walk to the logged tile X,Y. 4. Enter + exit.
  5. Reload and re-check the log.
- Pass: log "retrofit ... entrance placed at tile X,Y"; a den entrance stands there; enter/exit works; on
  reload it is **not placed again**.
- Repeat/Reset: if an earlier session already placed it, the log line is in THAT session's log — the entrance
  is simply there now; verify enter/exit + no re-place. "No clear spot found" = logged + retried next load.
  A world whose generation already rolled the entrance is just marked done. Fresh trigger needs a different
  pre-pack save.

**T84 Composite placeable**
- Prereqs: 2 stone.
- Steps: 1. Build **T84 Grove Shrine** (ONE entry in the menu). 2. Walk into both crystals and through the gap
  between them. 3. Save + reload. 4. Demolish it, then walk over the whole spot.
- Pass: TWO crystals at offsets from one build; both **block** walking, the gap is **walkable**; reload keeps
  both; demolish removes **everything** (no invisible blocker left — that's why you walk the spot after).
- Repeat/Reset: build/demolish freely.

**T85 Town building + work recipe**
- Prereqs: build hammer; 4 lumber + 2 brick; a citizen who can take the baker job; `material:flour` in the
  building's storage; the furnace **lit** (the bread recipe is fuel-gated).
- Steps: 1. Build menu → Buildings → **T85 Grand Bakery** → place + hammer up. 2. Assign a baker. 3. Queue
  **T85 Test Bread** in the work queue. 4. Watch storage/output. 5. ⚠ When done testing, **demolish it**
  before any pack-removal test (see T51).
- Pass: bakery stamps in with a working citizen slot; work queue lists T85 Test Bread; 1 flour drains → 2
  bread land in the output; job XP ticks.
- Repeat/Reset: the queue repeats as long as flour + fuel last. The building persists in the save.

---

## Events (solo) (6 tests)

**T57 Wave defense event**
- Prereqs: t46 arena portal built; go in ALONE (with 2 players the duel + team battle also arm — chaos).
- Steps: 1. Enter the arena. 2. Survive: chat "T57 Waves begins" → **5 s** → **2× FallenSword** spawn in a
  ring around you (3–6 tiles out) → clear → **8 s** breather → **2× FallenSword + 1× FallenSpear** → clear.
- Pass: wave announcements, ring spawns, then "conquered!" + **T57 Arena Trophy** in the bag.
- False-fail: kills by leaving the map don't count — stepping out mid-wave **despawns the mobs and re-arms**
  the event (that itself is a sub-check: leave mid-wave once and re-enter).
- Repeat/Reset: leave until the arena is empty → re-enter → it runs again. Trophies stack (max 10).

**T58 Boss rush event**
- Prereqs: t21 room via the T41 Crypt Portal. ⚠ Entering ALSO starts T87 koth + T88 race — expect their chat
  lines; they don't interfere with the bosses.
- Steps: 1. Enter the room. 2. Kill the **FallenCenturion** that spawns. 3. Kill the **Medusa** that follows.
- Pass: bosses come **one at a time** (next spawns only on kill, never two at once) → trophy at the end.
- Repeat/Reset: leave the room (world empties) → event aborts/re-arms → re-enter to run again. A relog
  mid-rush restarts it (event progress is session-keyed).

**T59 Hunt contest**
- Prereqs: 1+ player online — it announces on its own, all the time.
- Steps: 1. Wait for the 2-min hunt announcement (re-announces ~60 s after each contest ends). 2. Kill any
  creatures (any kind counts — no target set). 3. Wait for time-up.
- Pass: "X wins with N kills!" + trophy to the winner; a new hunt announces after the 60 s cooldown.
- False-fail: kills finished by your **summons or DoTs may not credit** (vanilla kill-credit funnel) — land
  killing blows yourself. Player deaths never score.
- Repeat/Reset: fully automatic, endlessly repeating — its chatter during other tests is normal.

**T60 Scheduled raid**
- Prereqs: a town to be raided; ability to sleep.
- Steps: 1. Sleep to next day (~05:00). 2. Watch chat + the vanilla raid UI. 3. Repeat the next day.
  4. And the day after.
- Pass: day 1 — a **rat raid** (trivial, `raid:rats`) marches on your nearest town immediately
  (war-declaration style, `immediate: true`); day 2 — **NO raid** (`everyNDays: 2`); day 3 — raid again.
- Repeat/Reset: the every-2-days clock is save-persistent — if an earlier session raided "yesterday", today is
  the off-day; just extend the observation one more day.

**T87 King of the hill**
- Prereqs: t21 room (arms together with T58 + T88 — fight the boss away from the center first if you want a
  calm hill stand).
- Steps: 1. Stand within ~80 px of the hermit spot (160,120) — the hermit marks the hill center.
  2. Stay ALONE on it ~20 s.
- Pass: chat "takes the hill lead (n/20)" (lead lines throttle to ~1 per 10 s) → at 20 hill-seconds → trophy.
  With a second player both on the hill: **contested**, nobody scores.
- Repeat/Reset: leave the room → re-arms. A relog resets your accrued seconds.

**T88 Checkpoint race**
- Prereqs: t21 room (arms with T58 + T87 — you can run the corners during the boss fight; dodge it).
- Steps: 1. Enter → chat "race begins in 10s". 2. Run the 4 corners **IN ORDER**: (64,64) → (256,64) →
  (256,192) → (64,192) — within ~48 px (3 tiles) of each. 3. Or idle 120 s instead.
- Pass: winner line + trophy on crossing the last corner in order; idling past 120 s → "time's up" abort.
- False-fail: corners out of order silently don't count (positions are polled every tick — sprinting can't
  skip one); restart the loop from corner 1.
- Repeat/Reset: leave → re-enter → new countdown.

---

## Co-op (2 players) (4 tests)

**T47 Mod handshake** *(do this first — it gates T46/T80/T86)*
- Prereqs: a second machine/instance. Host runs the mod + both packs.
- Steps: 1. Second player with mod + identical packs joins → check the HOST's log. 2. Rejoin with one pack
  removed or version-edited on the client. 3. Rejoin with no mod at all (vanilla client) and try to hit the
  host inside the t46 arena.
- Pass: (1) host log lists the client's packs; (2) chat warning from "Content Forge" naming the
  missing/mismatched pack; (3) "no Content Forge" warning + that client **can't deal player damage in T46**.
- Repeat/Reset: every join re-runs the handshake; nothing persists.

**T46 PvP arena + duel match**
- Prereqs: both players fully modded (T47 green). ⚠ Both entering also arms **T86 teams** and **T57 waves**:
  expect wave mobs + team-roster chat. Read each event's named chat lines; player-kill credit feeds the duel
  AND the team battle from the same kills.
- Steps: 1. Hit each other OUTSIDE the arena → nothing. 2. Both enter via the **T46 Arena Portal**.
  3. Hit each other. 4. Kill once. 5. Let the match end (180 s, most kills wins).
- Pass: inside, **damage lands**; kill → chat "X slew Y in the Test Colosseum" + **no grave** (inventory
  kept); winner receives the **T46 Champion's Belt**; outside, player damage is still blocked.
- Repeat/Reset: a match doesn't survive save/reload (aborts); the arena **re-arms once empty** — both leave,
  re-enter for a fresh match. A belt from an earlier session doesn't block a new one.

**T80 Summon kill credit (co-op arena)**
- Prereqs: an active arena fight (per T46); summoner carries the T11 Guard Book or T13 Bone Lord Book.
- Steps: 1. Get the enemy player to low HP. 2. Let YOUR summon land the killing blow.
- Pass: arena chat names **YOU** as the killer (previously read "not a player").
- Repeat/Reset: any arena kill; re-arm the arena as in T46.

**T86 Team battle event**
- Prereqs: 2+ fully-modded players in the t46 arena (it needs the pvp map).
- Steps: 1. Both enter → chat announces "Team Red vs Team Blue — first to 3 kills" + rosters (split
  alternates by join order). 2. Hit your TEAMMATE (with >2 players). 3. Fight to 3 kills.
- Pass: teammate hits do **nothing** (your own client may flash a predicted hit — known cosmetic, the server
  swallows it); at 3 kills the winning team is announced and **every** winning-team member gets a trophy.
- False-fail: with exactly 2 players you're always on opposite teams — the teammate-damage check needs a 3rd,
  or accept the duel kills double-counting into the team score (same kill feed as T46 — by design).
- Repeat/Reset: empty the arena → re-arms. Mid-battle joiners go to the smaller team.

---

## Offline tools (2 tests)

**T67 Held-sheet generator**
- Prereqs: Python; the game's Content dir; any source PNG of a weapon.
- Steps: 1. `python tools/make_held_sheet.py --list-templates` → shapes print (Sword 1104×240 = 23×5 cells of
  48×48, Dagger, Spear, Hammer…). 2. `python tools/make_held_sheet.py myblade.png --template Sword` →
  writes `myblade_sheet.png` + `myblade_compare.png`. 3. Open the compare PNG. 4. Optionally wire the sheet
  via an item's `equippable.display.sheet` + relaunch and hold it.
- Pass: compare-strip rows align with the vanilla template (orange/blue ticks on smear cells are informative,
  not errors); in-game the weapon renders in hand with your art.
- Repeat/Reset: pure offline; rerun freely (`--smear straight|keep|transfer` variants).

**T68 Tiled visual placement**
- Prereqs: free Tiled editor; edit the INSTALLED copy of a map tmx in the packs dir (e.g. worldoftales
  `realm.tmx`); relaunch after saving. Keep a backup to revert.
- Steps: 1. Open the tmx in Tiled → new **Object Layer** named exactly `doodads`. 2. Insert a **Point** named
  `CampFire`; insert a **Rectangle** (~16×10 px) with custom property `sprite=crystal.png`. 3. New object
  layer `npcs` → Point named `wot:sage`. 4. Save → relaunch → enter the realm. 5. Back in Tiled, hide one
  object (eye icon) → save → relaunch.
- Pass: campfire at the point; crystal at the rectangle's center blocking **exactly the drawn box**; the sage
  stands at YOUR point (overrides the json position); the hidden object is gone next launch.
- Repeat/Reset: edit + relaunch each round; revert the tmx when done. Property values must stay single-line.

---

## Temp-file tests (6 tests — cluster them into ONE relaunch pair, see the run order)

All temp packs go in the packs dir next to the others. Validate before launching:
`python editor/validate.py <packs folder>`. **Delete the temp files + relaunch when done.**

**T50 Player body rig**
- Prereqs: create `_temp_t50.json`:
  `{ "id": "_temp_t50", "namespace": "t50", "playerSkins": { "rig": { "tint": [80, 255, 80] } } }`
- Steps: 1. Relaunch → load your character. 2. Look at yourself; look at citizens; check worn clothes/hair/
  held items. 3. (Optional co-op) a partner's body too. 4. Open character creation.
- Pass: your character (and co-op players this client renders) is **green-bodied**; citizens unchanged;
  clothes/hair/held items unchanged; char-creation preview stays **vanilla** (known v1 limit — NOT a fail).
- Repeat/Reset: delete the file + relaunch → vanilla body. For sheet discovery: use
  `"rig": { "sheets": { "placeholder": "x.png" } }` → the log prints the real sheet names + sizes.

**T53 Citizen override**
- Prereqs: paste into the INSTALLED `test.json` (top level):
  `"citizenOverrides": { "citizen:generic": { "weight": 0 } }` → relaunch.
- Steps: 1. Recruit 2–3 jobless wild wanderers in a row.
- Pass: **EVERY** new arrival is Anna (generic weight 0 stops vanilla arrivals; skilled Good/Legendary
  recruits still bypass the roll — use jobless ones).
- Repeat/Reset: remove the paste + relaunch. Annas recruited during the test stay in town (harmless).

**T55 Player animation remix**
- Prereqs: create `_temp_t55.json`:
  `{ "id": "_temp_t55", "namespace": "t55", "playerAnimations": { "attack1": { "frameDurationScale": 2 } } }`
- Steps: 1. Relaunch. 2. Equip any sword (T04) and watch the FIRST swing of the combo vs the 2nd/3rd.
- Pass: the first swing animation plays at **HALF speed** — visual only; hit timing/damage cadence unchanged
  (the swing connects on schedule even though the arm lags — that mismatch IS the pass, not a bug).
- Repeat/Reset: delete the file + relaunch.

**T73 Cross-family AI + generic client anims**
- Prereqs: create `_temp_t73.json`:
  `{ "id": "_temp_t73", "namespace": "t73", "creatures": [ { "id": "t73:wolfbrain", "base": "SkeletonWarrior",
  "name": "T73 Wolf-Brain", "controller": "wolf", "clientController": "forge_creature_anim",
  "controllerParams": { "spac": "skeleton_animations" } } ], "spells": [ { "id": "t73:summon",
  "displayName": "Summon Wolf-Brain", "summon": { "creature": "t73:wolfbrain", "entityType": "Hostile",
  "count": 1 } } ], "items": [ { "id": "t73:book", "name": "T73 Wolf-Brain Tome", "icon": "scroll:brown",
  "equippable": { "equipmentType": "Offhand", "spellTome": { "spellId": "t73:summon" } } } ] }`
- Steps: 1. Relaunch → F1 → `t73:book` → cast in open ground. 2. Watch it move/attack. 3. Check the log.
- Pass: the skeleton **animates** while driven by the wolf AI (walks, attacks — where a bare cross-family swap
  would freeze the rig); log lists the spac's animation names on first spawn.
- Repeat/Reset: delete the file + relaunch.

**T81 Creature drops**
- Prereqs: none — test.json now ships `{ "creature": "Bear", "item": "cftest:t01_relic", "chance": 100 }`.
- Steps: 1. Relaunch. 2. Find a Bear and land the **killing blow yourself**. 3. Loot.
- Pass: the relic drops alongside the Bear's vanilla loot (the `drops[].creature` path — the only way to add
  wildlife drops).
- False-fail: kills WITHOUT a player killing blow only roll loot **5%** of the time (vanilla rule) — don't let
  summons/DoTs finish it.
- Repeat/Reset: every Bear; delete the file + relaunch when done.

**T82 Map auto-floor**
- Prereqs: in Tiled, copy `testroom.tmx` → `t82.tmx` and DELETE every layer except `ground`/`structure`
  (data-only map). Create `_temp_t82.json`:
  `{ "id": "_temp_t82", "namespace": "t82", "maps": [ { "id": "t82:bare", "tmx": "t82.tmx",
  "exitDirection": 1 } ], "placeables": [ { "id": "t82:portal", "name": "T82 Portal", "icon": "sam",
  "materials": { "resource:stone": 1 }, "width": 2, "height": 2, "cloneEntity": "WorkbenchGuid",
  "controller": "forge_portal", "controllerParams": { "map": "t82:bare" } } ] }`  (both files in the packs dir)
- Steps: 1. Relaunch → check the log. 2. Build the T82 Portal → enter. 3. Walk the room edges.
- Pass: log: **"auto-generated 'cf_autofloor'"**; the room renders with the plain cave floor (NOT pitch
  black); walls still block (collision intact). (The "tmx WITH art ships byte-identical" half: the regular
  testroom logs NO autofloor line — that absence is the check.)
- Repeat/Reset: delete both files + relaunch; demolish the portal first.

---

## Fastest full pass (minimal world reloads)

⚠ Two ordering rules: **(a)** demolish the T85 bakery before the T51 pack-removal leg (a save holding a pack
town building can't load without the pack). **(b)** Do temp-file tests in their own relaunch pair so the main
session stays clean.

**0 — No game (any time):** T67 tool runs. Prepare in advance: the four `_temp_*.json` files, the T53 paste
text, and the T82 `t82.tmx` (Tiled). Don't install them yet.

**1 — Main session (1 launch, lived-in save):**
1. On load: grep the log for `retrofit` → **T83** (walk there whenever convenient).
2. Open ground away from town — all F1 spawns in one sweep: **T42**(armor) → **T49 → T62 → T63 → T54 →
   T64**(with T06) → allied **T69**, then hostiles **T70 → T72 → T75 → T76**(×3) **→ T77 → T79 → T89 →
   T71 + T78** (cravens hurt by the T69 marksman, then the medic). **T74** (K → Forge tab) and **T90** (K → Pack Favours tree) between fights.
3. Build cluster: **T65** runeforge (craft the rune, check other stations) → **T84** grove → T41 Crypt
   Portal → T46 Arena Portal → Storyteller's Arch → **T85** bakery (assign baker, bread ticks — demolish
   when verified).
4. Arena visit SOLO → **T57** waves (leave mid-wave once to see the re-arm, re-enter, clear both waves).
5. t21-room visit → **T58 + T87 + T88 fire together**: run the race corners during the countdown chaos,
   kill Centurion → Medusa, then stand on the hermit-spot hill 20 s. Expect three trophies; sort the chat
   lines by event name.
6. Realm visit → **T40** (music/ambience/dark + 3-sticks-for-ember trade + charm craft) + **T45** (violet
   Keeper) + **T66** (campfire/chest/crystal). Bring 3 wood sticks + 1 linen, carry no ember in.
7. Overworld: **T56** (kill 2 Bears — only if the bear quest is still open in this save) · **T52** (recruit a
   jobless wanderer) · **T59** announces itself throughout — just note one "X wins" line · save/reload once
   (T84 persistence) · **T60**: sleep to next day → rat raid (off-day + day-3 raid can ride later sessions).
8. **T61** if the save has an unvisited forest bear den.
9. Quit to menu, demolish-check done → ready for T51.

**2 — T51 refund block (2 launches):** remove test pack → launch → login OK + point refunded → restore pack
→ launch → node re-learned. (Can fold launch #2 into block 3's launch.)

**3 — Temp-file block (2 launches):** install `_temp_t50/_t55/_t73/_t81/_t82` + the T53 paste → ONE launch
checks all six: green body (**T50**), half-speed first swing (**T55**), animated wolf-brain (**T73**), Bear
relic (**T81**), autofloor room (**T82**), Anna-only arrivals (**T53**). Then delete temps + revert the
paste → relaunch clean (this clean relaunch can double as the T68 verify launch after a Tiled edit).

**4 — Co-op session (1+ launches):** **T47** first (clean join → log; mismatched join → warning; vanilla
join → warning + no arena damage), then both modded players: arena → **T46** duel (+ expect T57/T86 noise)
→ **T80** summon killing blow → **T86** to 3 kills (3rd player or accept the shared kill feed).

Net: ~6 launches for all 41 open rows (T42/T49/T54/T62/T64 already ✅).
