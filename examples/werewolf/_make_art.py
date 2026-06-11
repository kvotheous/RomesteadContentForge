#!/usr/bin/env python3
"""
Art + audio generator for the "Versipellis" werewolf demo pack (ww).

Everything this pack ships visually/audibly is SYNTHESIZED here — no human-drawn
assets, no Pillow, stdlib only (struct + zlib for PNG, wave + math for audio).
Re-run to regenerate every PNG/WAV next to this script:

    python _make_art.py            # writes all assets
    python _make_art.py --preview  # also writes 4x-scaled previews to %TEMP%/ww_preview

Outputs (all next to this script):
  werewolf.png    480x192 — creature sheet, 4 direction rows (E,S,W,N) x 10 cols of 48x48
                  (cols 0-2 idle breathe, 3-6 walk, 7-9 attack lunge+claw)
  claw_slash.png  128x32  — kill vfx, 4 frames 32x32 of sweeping red claw streaks
  mist1/2/3.png   soft dithered gray-blue mist wisps (particle textures)
  moonmote.png    32x8    — 4-frame 8x8 twinkling pale glowmote
  icon_wolf.png / icon_villager.png / icon_seer.png / icon_laurel.png  18x18 icons
  howl.wav        ~2.6 s synthesized wolf howl (pitch-bent sine sweep + vibrato + octaves)

Pixel-art style: chunky shapes drawn from 24x24 ASCII grids scaled 2x to 48x48 —
no anti-aliasing, dark moonlit palette (fur grays, moon blues, blood red accents).
"""
import math
import os
import struct
import sys
import wave
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- PNG encoder
# (same minimal recipe as tools/make_held_sheet.py: RGBA8, filter-0 scanlines)


def _png_chunk(tag, payload):
    out = struct.pack(">I", len(payload)) + tag + payload
    return out + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)


def write_png(path, img):
    """img = list of rows, each row a list of (r, g, b, a) tuples."""
    h = len(img)
    w = len(img[0])
    raw = bytearray()
    for row in img:
        raw.append(0)  # filter byte 0
        for (r, g, b, a) in row:
            raw += bytes((r, g, b, a))
    data = (b"\x89PNG\r\n\x1a\n"
            + _png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + _png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + _png_chunk(b"IEND", b""))
    with open(path, "wb") as fh:
        fh.write(data)
    print("wrote %-18s %dx%d (%d bytes)" % (os.path.basename(path), w, h, len(data)))


# ---------------------------------------------------------------- grid helpers

T = (0, 0, 0, 0)  # transparent


def grid(w, h):
    return [[T] * w for _ in range(h)]


def from_ascii(text, pal, width):
    """ASCII art -> pixel grid. '.' = transparent; rows padded right to width."""
    lines = [ln for ln in text.strip("\n").split("\n")]
    out = []
    for ln in lines:
        if len(ln) > width:
            raise ValueError("row longer than %d: %r (len %d)" % (width, ln, len(ln)))
        ln = ln.ljust(width, ".")
        row = []
        for ch in ln:
            if ch == ".":
                row.append(T)
            else:
                row.append(pal[ch])
        out.append(row)
    return out


def paste(dst, src, dx=0, dy=0):
    """Blit non-transparent pixels of src onto dst at (dx, dy)."""
    for y, row in enumerate(src):
        ty = y + dy
        if ty < 0 or ty >= len(dst):
            continue
        for x, p in enumerate(row):
            tx = x + dx
            if p[3] == 0 or tx < 0 or tx >= len(dst[0]):
                continue
            dst[ty][tx] = p
    return dst


def stack(w, h, *layers):
    """Compose layers (each (grid, dx, dy) or grid) onto a fresh w x h canvas."""
    out = grid(w, h)
    for layer in layers:
        if isinstance(layer, tuple):
            g, dx, dy = layer
        else:
            g, dx, dy = layer, 0, 0
        paste(out, g, dx, dy)
    return out


def hmirror(g):
    return [list(reversed(row)) for row in g]


def scale2(g):
    out = []
    for row in g:
        big = []
        for p in row:
            big.append(p)
            big.append(p)
        out.append(big)
        out.append(list(big))
    return out


def shift(g, dx, dy):
    out = grid(len(g[0]), len(g))
    paste(out, g, dx, dy)
    return out


# ---------------------------------------------------------------- palette

PAL = {
    "k": (22, 19, 30, 255),     # outline near-black
    "n": (10, 8, 16, 255),      # nose / deepest shadow
    "d": (53, 49, 66, 255),     # dark fur
    "f": (92, 89, 111, 255),    # mid fur
    "g": (128, 130, 153, 255),  # light fur
    "m": (157, 166, 192, 255),  # moonlit highlight
    "b": (197, 208, 234, 255),  # bright moon rim
    "y": (255, 216, 74, 255),   # eye glow yellow
    "o": (255, 150, 40, 255),   # eye core ember
    "w": (232, 228, 211, 255),  # fang / claw bone
    "r": (150, 24, 40, 255),    # blood red dark
    "R": (220, 38, 60, 255),    # blood red bright
}

# ================================================================ WEREWOLF RIG
# Each facing is authored as a 24x24-space set of ASCII grids and scaled 2x to
# 48x48. Sheet rows are the game's 4-direction order: E, S, W, N (W = mirrored E).
# Columns: 0-2 idle (breathing hunch), 3-6 walk, 7-9 attack (windup/lunge/recover).

CW = 24  # authoring cell size (scaled 2x -> 48)

# ---- EAST (side view, facing right). Top part (head/hump/torso/arm), y0..15.
E_TOP = """
........................
........................
.............kk.........
......kkkkk..kdk........
.....kdfffdkkkdfk.......
....kdgggffddffffdk.....
...kdfggggffffffffdk....
...kdfgmmgffffggddkk....
..kdffgmgfffffggyyodk...
..kddffggffffffgddddnk..
.kdffdfffggffffdkkwkk...
.kdffdkffffgffffdkkk....
..kkk.kdffffffffdk......
......kdffffkffffdk.....
......kdffffkkdffdk.....
.......kdffdk.kdwwk.....
"""
# East legs, y16..23 (stand / stride A / stride B). Hind leg left, fore leg right.
E_LEGS_STAND = """
......kdffdk............
......kdfdfdk...........
.....kdfk.kdfk..........
.....kdfk..kdfk.........
.....kdfk..kdfk.........
.....kffk..kffk.........
.....kwwk..kwwk.........
........................
"""
E_LEGS_A = """
......kdffdk............
.....kdfdkfdk...........
....kdfk..kdfk..........
...kdfk....kdfk.........
...kdfk.....kdfk........
..kdfk......kffk........
..kwwk......kwwk........
........................
"""
E_LEGS_B = """
......kdffdk............
......kdfdfdk...........
......kdfkdfk...........
.....kdfk.kdfk..........
.....kffk..kdfk.........
.....kwwk..kffk.........
...........kwwk.........
........................
"""
# East attack tops: 1 windup (rears back, claw raised), 2 lunge (claw thrust
# forward with red streaks), 3 recover (claw low, streaks fading).
E_ATK1_TOP = """
...........kk..........
....kkkkk..kdk..........
...kdfffdkkkdfk..kwk....
..kdgggffddffffdkkwk....
..kdfggggffffffdkwwk....
..kdfgmmgffffggddkdk....
.kdffgmgfffffggyyodk....
.kddffggffffffgddddnk...
kdffdfffggffffdkkkkk....
kdffdkffffgffffdk.......
.kkk.kdffffffffdk.......
.....kdffffkfffdk.......
.....kdffffkkffdk.......
......kdffdk.kdfdk......
......kdffdk..kkk.......
.......kkkk.............
"""
E_ATK2_TOP = """
........................
........................
...........kk......R....
....kkkkk..kdk....R.R...
...kdfffdkkkdfk..R.R.R..
..kdgggffddffffdkkRkR...
..kdfggggffffffffdkwwk..
..kdfgmmgffffggddwwwwk..
.kdffgmgfffffggyyoddkk..
.kddffggffffffgddddnk...
kdffdfffggffffdkkwkk....
kdffdkffffgffffdkkk.....
.kkk.kdffffffffdk.......
.....kdffffkffffdk......
.....kdffffkkdffdk......
......kdffdk.kdwwk......
"""
E_ATK3_TOP = """
........................
........................
.............kk.........
......kkkkk..kdk........
.....kdfffdkkkdfk.......
....kdgggffddffffdk.....
...kdfggggffffffffdk....
...kdfgmmgffffggddkk....
..kdffgmgfffffggyyodk...
..kddffggffffffgddddnk..
.kdffdfffggffffdkkkk.r..
.kdffdkffffgffffdkww..r.
..kkk.kdffffffffdkwwk...
......kdffffkffffdkk....
......kdffffkkdffdk.....
.......kdffdk.kkkk......
"""

# ---- SOUTH (front view, facing the camera). Hunched: head low between shoulders.
S_TOP = """
........................
........................
.....kk.........kk......
....kdfk.......kfdk.....
....kdffk.....kffdk.....
...kkdffkkkkkkkffdkk....
..kdffdffffffffdffdk....
.kdffdfgggggggggdffdk...
.kdfdfggyyofggyyogdfdk..
.kdfdffgddddddddgffdfk..
.kdffdffgdnnnndgffdffk..
.kdffdfffdwddwdfffdffk..
.kdffdffffddddffffdffk..
..kdfdkdffffffffdkdfdk..
..kdfdkdffffffffdkdfdk..
..kwwwk.kdffffdk.kwwwk..
"""
S_LEGS_STAND = """
........kdffffdk........
........kdfkkdfk........
.......kdfdk.kdfdk......
.......kdfk...kdfk......
.......kdfk...kdfk......
.......kffk...kffk......
......kwwwk...kwwwk.....
........................
"""
S_LEGS_A = """
........kdffffdk........
........kdfkkdfk........
.......kdfdk.kdfdk......
.......kdfk...kdfk......
.......kffk...kdfk......
......kwwwk...kdfk......
..............kffk......
..............kwwwk.....
"""
S_LEGS_B = """
........kdffffdk........
........kdfkkdfk........
.......kdfdk.kdfdk......
.......kdfk...kdfk......
.......kdfk...kffk......
.......kdfk..kwwwk......
.......kffk.............
......kwwwk.............
"""
S_ATK1_TOP = """
........................
..kwwk..........kwwk....
..kffk..kk..kk..kffk....
..kdfk.kdfkkfdk.kdfk....
..kdfdkkdffffdkkdfdk....
..kdffdffffffffdffdk....
.kdffdfgggggggggdffdk...
.kdfdfggyyofggyyogdfdk..
.kdfdffgddddddddgffdfk..
.kdffdffgdnnnndgffdffk..
.kdffdfffdwddwdfffdffk..
.kdffdffffddddffffdffk..
..kdfdkdffffffffdkdfdk..
..kdfdkdffffffffdkdfdk..
...kkk.kdffffdk..kkk....
........................
"""
S_ATK2_TOP = """
........................
........................
.....kk.........kk......
....kdfk..R..R.kfdk.....
....kdffk.R..R.kffdk....
...kkdffkkRkkRkkffdkk...
..kdffdfffRffRffdffdk...
.kdffdfgggRggRgggdffdk..
.kdfdfggyyofggyyogdfdk..
.kdfdffgddddddddgffdfk..
.kdffdffgdnnnndgffdffk..
.kdwwdfffdwddwdfffdwwk..
.kwwfdffffddddffffdfwwk.
..kdfdkdffffffffdkdfdk..
..kdfdkdffffffffdkdfdk..
...kkk..kdffffdk..kkk...
"""
S_ATK3_TOP = """
........................
........................
.....kk.........kk......
....kdfk.......kfdk.....
....kdffk..r...kffdk....
...kkdffkkkrkkkffdkk....
..kdffdffffrfffdffdk....
.kdffdfggggrggggdffdk...
.kdfdfggyyofggyyogdfdk..
.kdfdffgddddddddgffdfk..
.kdffdffgdnnnndgffdffk..
.kdffdfffdwddwdfffdffk..
.kwwfdffffddddffffdfwwk.
..kdfdkdffffffffdkdfdk..
..kdfdkdffffffffdkdfdk..
...kkk.kdffffdk..kwwwk..
"""

# ---- NORTH (back view): same hunched silhouette, no face — moonlit back +
# bushy tail hanging center-low between the legs.
N_TOP = """
........................
........................
.....kk.........kk......
....kfdk.......kdfk.....
....kffdk.....kdffk.....
...kkffdkkkkkkkdffkk....
..kdffdmmmmmmmmdffdk....
.kdffdmmbbbbbbmmdffdk...
.kdfdfmbbmmmmbbmgdfdk...
.kdfdffmmggggmmfgffdfk..
.kdffdffggffggffffdffk..
.kdffdffffffffffffdffk..
.kdffdffffffffffffdffk..
..kdfdkdffffffffdkdfdk..
..kdfdkdffffffffdkdfdk..
..kwwwk.kdffffdk.kwwwk..
"""
N_LEGS_STAND = """
........kdffffdk........
........kdfdkdfk........
.......kdfdfkkdfdk......
.......kdfdfk.kdfk......
.......kdkdfk.kdfk......
.......kffkdk.kffk......
......kwwwk..kwwwk......
........................
"""
N_LEGS_A = """
........kdffffdk........
........kdfdkdfk........
.......kdfdfkkdfdk......
.......kdfdfk.kdfk......
.......kffdfk.kdfk......
......kwwwkdk.kdfk......
..............kffk......
..............kwwwk.....
"""
N_LEGS_B = """
........kdffffdk........
........kdfdkdfk........
.......kdfdfkkdfdk......
.......kdfdfk.kffk......
.......kdkdfkkwwwk......
.......kffkdk...........
......kwwwk.............
........................
"""


def build_werewolf_sheet():
    pal = PAL

    def G(text):
        return from_ascii(text, pal, CW)

    def frames_for(top, legs_stand, legs_a, legs_b, atk1, atk2, atk3):
        top_g, stand, la, lb = G(top), G(legs_stand), G(legs_a), G(legs_b)
        a1, a2, a3 = G(atk1), G(atk2), G(atk3)
        base = stack(CW, CW, (top_g, 0, 0), (stand, 0, 16))
        # idle: breathing hunch — torso sinks one px while the feet stay planted
        hunch = stack(CW, CW, (top_g, 0, 1), (stand, 0, 16))
        idle = [base, hunch, base]
        # walk: stride A / pass / stride B / pass, striding frames bob down 1
        walk = [stack(CW, CW, (top_g, 0, 1), (la, 0, 16)),
                base,
                stack(CW, CW, (top_g, 0, 1), (lb, 0, 16)),
                base]
        attack = [stack(CW, CW, (a1, 0, 0), (stand, 0, 16)),
                  stack(CW, CW, (a2, 0, 0), (la, 0, 16)),
                  stack(CW, CW, (a3, 0, 0), (stand, 0, 16))]
        return idle + walk + attack  # 10 frames

    east = frames_for(E_TOP, E_LEGS_STAND, E_LEGS_A, E_LEGS_B,
                      E_ATK1_TOP, E_ATK2_TOP, E_ATK3_TOP)
    south = frames_for(S_TOP, S_LEGS_STAND, S_LEGS_A, S_LEGS_B,
                       S_ATK1_TOP, S_ATK2_TOP, S_ATK3_TOP)
    west = [hmirror(f) for f in east]
    # north: attacks away from camera — raised-arm windup is the mirrored idle
    # silhouette with claws lifted; reuse the south attack timing shapes minus face
    n_top, n_stand, n_a, n_b = G(N_TOP), G(N_LEGS_STAND), G(N_LEGS_A), G(N_LEGS_B)
    n_base = stack(CW, CW, (n_top, 0, 0), (n_stand, 0, 16))
    n_hunch = stack(CW, CW, (n_top, 0, 1), (n_stand, 0, 16))
    n_idle = [n_base, n_hunch, n_base]
    n_walk = [stack(CW, CW, (n_top, 0, 1), (n_a, 0, 16)), n_base,
              stack(CW, CW, (n_top, 0, 1), (n_b, 0, 16)), n_base]
    n_atk = [shift(n_base, 0, 1), shift(n_base, 0, -2), n_base]
    north = n_idle + n_walk + n_atk

    rows = [east, south, west, north]  # the game's 4-direction row order
    sheet = grid(10 * 48, 4 * 48)
    for ry, frames in enumerate(rows):
        for cx, f in enumerate(frames):
            paste(sheet, scale2(f), cx * 48, ry * 48)
    return sheet


# ================================================================ CLAW SLASH VFX
# 4 frames 32x32: three parallel red claw streaks sweeping down the diagonal,
# brightest mid-swipe, fading to dark red trails.


def build_claw_slash():
    KB = (255, 245, 235, 255)   # white-hot streak core
    BR = PAL["R"]
    DR = PAL["r"]
    FD = (96, 16, 28, 160)      # fading trail

    def frame(prog):
        """prog 0..3 — streak sweep progress."""
        f = grid(32, 32)
        # three claw lines, offset perpendicular to the swipe diagonal
        for i, off in enumerate((-5, 0, 5)):
            # the streak runs from top-right to bottom-left as prog advances
            start = 2 + prog * 5
            length = (10, 16, 14, 9)[prog]
            for t in range(length):
                x = 26 - start - t + off
                y = 3 + start + t
                if 0 <= x < 32 and 0 <= y < 32:
                    col = BR if prog in (1, 2) else DR
                    if prog == 1 and 4 <= t <= length - 4:
                        col = KB if i == 1 else BR
                    if prog == 3:
                        col = FD
                    f[y][x] = col
                    # 2px-wide chunky streak
                    if x + 1 < 32:
                        f[y][x + 1] = col if prog == 1 else DR if prog == 2 else FD
        # blood flecks scatter on the later frames
        if prog >= 2:
            for (fx, fy) in ((6, 8), (10, 22), (20, 26), (24, 12), (14, 6)):
                f[(fy + prog * 2) % 32][fx] = DR if prog == 2 else FD
        return f

    sheet = grid(4 * 32, 32)
    for i in range(4):
        paste(sheet, frame(i), i * 32, 0)
    return sheet


# ================================================================ MIST + MOTES


BAYER4 = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def build_mist(size, lobes, seed):
    """Soft gray-blue wisp: union of offset radial lobes, ordered-dither alpha
    bands (chunky soft edges, zero AA mush)."""
    img = grid(size, size)
    base = (134, 148, 178)
    bright = (170, 184, 212)
    for y in range(size):
        for x in range(size):
            v = 0.0
            for (cx, cy, r) in lobes:
                dx = (x - cx) / r
                dy = (y - cy) / (r * 0.7)  # squashed: drifting fog bank
                v = max(v, 1.0 - math.sqrt(dx * dx + dy * dy))
            if v <= 0:
                continue
            th = BAYER4[(y + seed) % 4][(x + seed * 2) % 4] / 16.0
            if v > 0.62:
                img[y][x] = bright + (150,)
            elif v > 0.34 and v > th * 0.7:
                img[y][x] = base + (110,)
            elif v > 0.10 and v > th:
                img[y][x] = base + (60,)
    return img


def build_moonmote():
    """4 twinkle frames, 8x8 each: pale blue-white glowmote."""
    CORE = (245, 248, 255, 255)
    HALO = (180, 198, 240, 170)
    DIM = (140, 158, 205, 110)
    sheet = grid(32, 8)

    def put(fi, x, y, c):
        sheet[y][fi * 8 + x] = c

    for fi, size in enumerate((1, 2, 3, 2)):
        cx = cy = 3
        put(fi, cx, cy, CORE)
        if size >= 2:
            for (dx, dy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                put(fi, cx + dx, cy + dy, HALO)
        if size >= 3:
            for (dx, dy) in ((2, 0), (-2, 0), (0, 2), (0, -2)):
                put(fi, cx + dx, cy + dy, DIM)
            for (dx, dy) in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                put(fi, cx + dx, cy + dy, DIM)
    return sheet


# ================================================================ ICONS (18x18)

ICON_PAL = dict(PAL)
ICON_PAL.update({
    "G": (216, 169, 55, 255),   # gold
    "L": (240, 207, 110, 255),  # light gold
    "e": (52, 102, 48, 255),    # laurel green dark
    "E": (96, 158, 74, 255),    # laurel green light
    "p": (122, 79, 208, 255),   # seer purple
    "P": (180, 150, 240, 255),  # seer light purple
    "W": (235, 238, 248, 255),  # eye white
    "s": (140, 96, 48, 255),    # stalk brown
})

ICON_WOLF = """
..................
.....kk...........
....kdfk....kk....
....kdffk..kdk....
...kkdffkkkfdk....
..kdffdffffffk....
.kdfdfggggggfdk...
.kdfdfgyogggffdk..
.kdffdgddggggfdkk.
..kdffggggggffddk.
...kdffffffffdnk..
....kdffffffdkk...
.....kdffkwwk.....
.....kdfk.kk......
....kdfk..........
....kffk..........
....kwwk..........
..................
"""
ICON_VILLAGER = """
..................
....L..L..L.......
...LGL.LGL.LGL....
...LGLLLGLLLGL....
....LGLLGLLGL.....
....LGLLGLLGL.....
.....LGLGLGL......
.....LGLGLGL......
......LGGGL.......
......sGGGs.......
.....ssGGGss......
....s.sGGGs.s.....
......sGGGs.......
.....ssGGGss......
....ss.GGG.ss.....
.......GGG........
......GG.GG.......
..................
"""
ICON_SEER = """
..................
..................
..................
......kkkkkk......
....kkWWWWWWkk....
..kkWWWWWWWWWWkk..
.kWWWWkppppkWWWWk.
.kWWWkpPPPPpkWWWk.
kWWWWkpPkkPpkWWWWk
.kWWWkpPPPPpkWWWk.
.kWWWWkppppkWWWWk.
..kkWWWWWWWWWWkk..
....kkWWWWWWkk....
......kkkkkk......
..................
.......PpP........
......P.p.P.......
..................
"""
ICON_LAUREL = """
..................
..EE..........EE..
.EeeE........EeeE.
.EeeE........EeeE.
..EeeE......EeeE..
.EEeeE......EeeEE.
.EeeeeE....EeeeeE.
..EeeE......EeeE..
..EEeeE....EeeEE..
..EeeeeE..EeeeeE..
...EeeE....EeeE...
...EEeeE..EeeEE...
...EeeeeEEeeeeE...
....EeeEEEEeeE....
.....EeeeeeeE.....
......EeeeeE......
.......GLG........
..................
"""


def build_icon(text):
    return from_ascii(text, ICON_PAL, 18)


# ================================================================ HOWL (wave)


def build_howl(path):
    rate = 22050
    dur = 2.6
    n = int(rate * dur)

    def freq(t):
        # the howl contour: rise 300->500, quavering hold, mournful fall to 250
        if t < 0.9:
            f = 300.0 + (500.0 - 300.0) * (t / 0.9) ** 1.4
        elif t < 1.7:
            f = 500.0
        else:
            f = 500.0 + (250.0 - 500.0) * ((t - 1.7) / 0.9) ** 1.2
        # vibrato grows once the note settles
        vib_amt = 0.0 if t < 0.5 else min(1.0, (t - 0.5) / 0.4) * 13.0
        return f + vib_amt * math.sin(2 * math.pi * 5.3 * t)

    def amp(t):
        a = 0.85
        if t < 0.18:
            a *= t / 0.18                      # breathe in
        if t > dur - 0.55:
            a *= max(0.0, (dur - t) / 0.55)    # trail off into the night
        a *= 1.0 + 0.06 * math.sin(2 * math.pi * 3.1 * t)  # faint throat tremor
        return a

    samples = bytearray()
    ph = 0.0
    for i in range(n):
        t = i / rate
        ph += 2 * math.pi * freq(t) / rate
        # layered voice: fundamental + octave body + airy 3rd harmonic + sub-growl
        s = (0.58 * math.sin(ph)
             + 0.26 * math.sin(2 * ph)
             + 0.09 * math.sin(3 * ph)
             + 0.14 * math.sin(0.5 * ph))
        v = int(max(-1.0, min(1.0, s * amp(t))) * 32000)
        samples += struct.pack("<h", v)

    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(rate)
        wf.writeframes(bytes(samples))
    print("wrote %-18s %.1fs %dHz mono (%d bytes)" %
          (os.path.basename(path), dur, rate, len(samples) + 44))


# ================================================================ main


def scale_n(g, n):
    out = []
    for row in g:
        big = []
        for p in row:
            big.extend([p] * n)
        for _ in range(n):
            out.append(list(big))
    return out


def main():
    preview = "--preview" in sys.argv

    assets = {
        "werewolf.png": build_werewolf_sheet(),
        "claw_slash.png": build_claw_slash(),
        "mist1.png": build_mist(32, [(13, 16, 11), (21, 14, 8)], 0),
        "mist2.png": build_mist(28, [(10, 15, 9), (18, 12, 8), (15, 17, 6)], 1),
        "mist3.png": build_mist(20, [(10, 10, 8)], 2),
        "moonmote.png": build_moonmote(),
        "icon_wolf.png": build_icon(ICON_WOLF),
        "icon_villager.png": build_icon(ICON_VILLAGER),
        "icon_seer.png": build_icon(ICON_SEER),
        "icon_laurel.png": build_icon(ICON_LAUREL),
    }
    for name, img in assets.items():
        write_png(os.path.join(HERE, name), img)
    build_howl(os.path.join(HERE, "howl.wav"))

    if preview:
        pdir = os.path.join(os.environ.get("TEMP", HERE), "ww_preview")
        os.makedirs(pdir, exist_ok=True)
        for name, img in assets.items():
            write_png(os.path.join(pdir, name), scale_n(img, 4))
        print("previews ->", pdir)


if __name__ == "__main__":
    main()
