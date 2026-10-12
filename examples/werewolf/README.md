# Versipellis — the Werewolf Village demo pack (`ww`)

> **Status: unfinished, not maintained.** This branch holds a work-in-progress game mode (`Werewolf.cs` +
> this pack). It builds and its packs validate, but it hasn't been tested in a real 4+ player match.
> You're welcome to pick it up, fix it and open a pull request — no promises it'll get reviewed quickly.
> The `cftest:t91_werewolf` event in the test pack is a solo debug rig (`debugSolo`, `minPlayers: 1`); the
> validator's "can never produce a real match" warning about it is expected.

The flagship demo pack for Content Forge's **werewolf social-deduction event**
(`"type": "werewolf"` — a Mafia/Werewolf night-day loop: wolves pick victims at night,
the village votes at day, last side standing wins). This pack supplies everything the
mode needs: the moonlit village map with a walled ghost pen, a fully custom werewolf
creature, kill VFX, ambience particles, a synthesized howl, the reward laurel, and the
event definition itself.

**Every asset is machine-generated** — all PNGs and the WAV are synthesized by
[`_make_art.py`](_make_art.py) (pure Python stdlib, no Pillow, no human artist).
Regenerate everything with:

```
python _make_art.py            # rewrites every png + howl.wav next to the script
python _make_art.py --preview  # also dumps 4x-scaled previews to %TEMP%/ww_preview
```

## How to start a match

1. Install the pack: copy this folder's files into
   `BepInEx\config\RomesteadContentForge\packs\` (json + pngs + wav + tmx + yarn together).
2. In game, build the **Moongate to Versipellis** (`ww:village_portal`, construction
   menu — 2 stone) and interact to enter the village.
3. Gather **4+ players** inside (everyone must run the mod + this pack — the join
   handshake checks). The `ww:nightfall` event arms automatically: roles are dealt
   (wolf / villager / seer), night falls for 45 s, the village wakes, discusses for
   120 s, votes for 30 s — repeat until one side wins. Winners get the
   **Lupercal Laurel** (+25 HP, +5% move speed trinket).

> **Debug:** the event ships with `minPlayers: 4` (real matches). For a two-client
> smoke test, temporarily edit `ww:nightfall` to `"minPlayers": 2` — 2 players can
> never produce a fair match (wolf + villager only) but exercises the full
> role/kill/ghost/vote round-trip. Pack JSON is strict JSON, so this note lives here
> instead of a comment in the file.

## What's inside

| Id | What |
|---|---|
| `ww:werewolf` | The creature: hunched wolf-man, 4-direction from-scratch rig (`ww:werewolf_anims`, 48x48, idle breathe / 4-frame walk / 3-frame lunge+claw attack), `forge_guard` AI, 3x health. |
| `ww:claw_slash` | Kill VFX: 4 frames of three red claw streaks sweeping a diagonal, fading to blood flecks. Works anywhere a vfx/spac id is taken. |
| `ww:mist` / `ww:moonmotes` | Emitters: slow-drifting gray-blue fog wisps (3 dithered textures) and twinkling pale glowmotes with a faint blue light. |
| `cf:sound:ww:howl` | ~2.6 s synthesized wolf howl (pitch-bent sine 300→500→250 Hz, vibrato, layered octaves). The portal plays it on travel. |
| `ww:village` | 40x30 village map (`village.tmx`): central plaza with 4 campfires, 6 houses, walled ghost pen in the NE corner. `pvp: true` (no graves, kill-gate active), dusk ambient light `[0.45, 0.42, 0.55]`. |
| `ww:village_portal` | The way in — `forge_portal` placeable, buildable anywhere. |
| `ww:elder` | Elder Marcus, a plaza NPC who warns you about the wolf (`elder.yarn`). |
| `ww:laurel` | Lupercal Laurel — winners' trinket. |
| `ww:icon_wolf` / `ww:icon_villager` / `ww:icon_seer` / `ww:icon_laurel` | 18x18 role/reward icons (wolf head, wheat sheaf, seer eye, laurel wreath). |
| `ww:nightfall` | The event: `type: werewolf`, 45 s nights / 120 s days / 30 s votes, auto wolf count, seer on, ghost pen at px **(592, 48)**, laurel reward. |

## Map coordinates (px, 16 px = 1 tile)

- **Plaza center** (meeting spot): ~`(328, 232)` — campfires at `(280,184)`,
  `(392,184)`, `(280,288)`, `(392,288)`; Elder Marcus at `(344, 216)`.
- **Ghost pen**: NE corner, interior tiles x 35–38 / y 1–4 (4x4 walkable, fully
  walled — dead players respawn inside and spectate). Pixel center: **`(592, 48)`**
  = the event's `ghostPen`.
- **Exit**: gap in the south wall, tiles x 18–21 (the default bottom-center exit box —
  walk into it to leave).

## Art style notes

Chunky 2x-scaled pixel art on a moonlit palette: fur grays `#35313F`→`#80829A`,
moon-blue highlights `#9DA6C0`/`#C5D0EA`, glowing yellow eyes `#FFD84A` with ember
cores, blood-red accents `#DC263C`. Sprites are authored as 24x24 ASCII grids in
`_make_art.py` and doubled to 48x48 — no anti-aliasing anywhere; the mist uses
ordered (Bayer) dithering for its soft edges instead.
