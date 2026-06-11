#!/usr/bin/env python3
"""
make_held_sheet.py - generate a rig-aligned HELD-ITEM sprite sheet from ONE weapon PNG.

Draw your weapon once - pointing STRAIGHT UP, grip (pommel end) at bottom-center -
then let the tool place it into every animation frame of a vanilla main-hand sheet:

    python tools/make_held_sheet.py mysword.png --template Sword

Output: mysword_sheet.png (same dimensions/layout as the vanilla template, ready for
the pack's `display.sheet` field) plus mysword_compare.png (every non-empty cell,
template row over generated row, 2x zoom, orange corner tick = warned cell) for
fast eyeballing.

How it works: every 48x48 cell of the vanilla template sheet is analysed - opaque
pixels are split into connected components, collinear components re-merged (the
transparent hand cutout splits spear/hammer shafts in two), then each remaining
part gets a centroid + dominant axis via PCA; the axis endpoint FARTHER from the
cell center is taken as the tip, the nearer one as the grip. Your PNG is then
uniformly scaled (to the MEDIAN template weapon length), rotated about its grip to
each part's angle (nearest-neighbour upscale / coverage-supersampled downscale,
keeps pixel art crisp) and translated so your grip lands on the template grip.
Dual-wield frames (vanilla dagger) get one copy per part. Empty template cells
stay empty.

Limitations (v1) - all are WARNed per cell and ticked in the compare strip:
 * vanilla attack frames often contain a painted motion-smear arc, not a rigid
   weapon (sword columns 9/11/15/18/19); by default the tool draws your straight
   weapon at the recovered angle instead of a smear. See --smear: 'keep' copies
   the vanilla smear art verbatim, 'transfer' re-colours it to YOUR weapon's
   palette (luminance-band LUT). Smear cells handled by keep/transfer get a BLUE
   tick in the compare strip instead of orange.
 * foreshortened / blob frames don't PCA cleanly (angle unreliable).
 * uniform median scale: cells whose vanilla weapon is drawn shorter (perspective)
   will show your weapon slightly long, and smear frames slightly short.
 * tip-vs-grip is resolved by "tip is farther from cell center" - measured ~92%
   correct on the vanilla sword, ~94% on dagger (validated against handle colour);
   the residual flips are mostly in warned smear/extended-arm frames.

Stdlib only. XNB decode: uncompressed Texture2D SurfaceFormat.Color (RGBA32) -
same subset as tools/asset_catalog.py (100% of sprite xnbs decode this way).
"""

import argparse
import math
import os
import struct
import sys
import zlib

DEFAULT_CONTENT = r"C:\Program Files (x86)\Steam\steamapps\common\Romestead\Content"
TOOLS_SUBPATH = os.path.join("media", "sprites", "character", "tools")

# analysis thresholds
MIN_PIXELS = 12         # fewer opaque px than this -> low confidence (sliver frame)
MIN_ELONGATION = 1.7    # sqrt(major/minor eigenvalue); below -> blob/foreshortened
ALPHA_MIN = 8           # opaque means alpha >= this

# ---------------------------------------------------------------- XNB decode
# (same parser as tools/asset_catalog.py)

class XnbSkip(Exception):
    pass


def _read_7bit(data, pos):
    result = shift = 0
    while True:
        b = data[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if not b & 0x80:
            return result, pos
        shift += 7


def decode_xnb_rgba(data):
    """Return (width, height, rgba_bytes) for an uncompressed Color Texture2D XNB."""
    if len(data) < 16 or data[:3] != b"XNB":
        raise XnbSkip("not an XNB file")
    flags = data[5]
    if flags & 0xC0:
        raise XnbSkip("compressed XNB (not supported)")
    try:
        pos = 10
        type_count, pos = _read_7bit(data, pos)
        is_texture = False
        for _ in range(type_count):
            length, pos = _read_7bit(data, pos)
            if b"Texture2DReader" in data[pos:pos + length]:
                is_texture = True
            pos += length + 4
        _, pos = _read_7bit(data, pos)
        _, pos = _read_7bit(data, pos)
        fmt, width, height, _mips, dsize = struct.unpack_from("<5i", data, pos)
        pos += 20
    except (IndexError, struct.error):
        raise XnbSkip("truncated/garbled header")
    if not is_texture:
        raise XnbSkip("not a Texture2D")
    if fmt != 0:
        raise XnbSkip("SurfaceFormat %d (only Color supported)" % fmt)
    if width <= 0 or height <= 0 or dsize != width * height * 4:
        raise XnbSkip("size mismatch")
    if len(data) < pos + dsize:
        raise XnbSkip("truncated pixel data")
    return width, height, bytearray(data[pos:pos + dsize])


# ------------------------------------------------------------- PNG encode/decode

def _png_chunk(tag, payload):
    out = struct.pack(">I", len(payload)) + tag + payload
    return out + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)


def encode_png(width, height, rgba):
    """Minimal RGBA8 PNG encoder (filter 0 scanlines)."""
    stride = width * 4
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]
    return (b"\x89PNG\r\n\x1a\n"
            + _png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + _png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + _png_chunk(b"IEND", b""))


def decode_png(data):
    """Minimal PNG decoder -> (width, height, rgba bytearray).
    Supports 8-bit depth, color types 0/2/3/4/6, no interlace, optional tRNS."""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG file")
    pos = 8
    width = height = None
    bitdepth = ctype = interlace = None
    idat = bytearray()
    palette = None
    trns = None
    while pos < len(data):
        (length,), tag = struct.unpack_from(">I", data, pos), data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if tag == b"IHDR":
            width, height, bitdepth, ctype, _comp, _filt, interlace = \
                struct.unpack(">IIBBBBB", payload)
        elif tag == b"PLTE":
            palette = payload
        elif tag == b"tRNS":
            trns = payload
        elif tag == b"IDAT":
            idat += payload
        elif tag == b"IEND":
            break
    if width is None:
        raise ValueError("PNG missing IHDR")
    if bitdepth != 8:
        raise ValueError("only 8-bit PNGs supported (got bit depth %d)" % bitdepth)
    if interlace:
        raise ValueError("interlaced (Adam7) PNGs not supported - re-save without interlace")
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(ctype)
    if channels is None:
        raise ValueError("unsupported PNG color type %d" % ctype)
    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    if len(raw) < (stride + 1) * height:
        raise ValueError("PNG pixel data truncated")
    # unfilter
    out = bytearray(stride * height)
    prev = bytearray(stride)
    bpp = channels
    p = 0
    for y in range(height):
        ftype = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if ftype == 1:    # Sub
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif ftype == 2:  # Up
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:  # Average
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:  # Paeth
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i - bpp] if i >= bpp else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF
        elif ftype != 0:
            raise ValueError("bad PNG filter type %d" % ftype)
        out[y * stride:(y + 1) * stride] = line
        prev = line
    # convert to RGBA
    rgba = bytearray(width * height * 4)
    if ctype == 6:
        rgba[:] = out
    elif ctype == 2:
        for i in range(width * height):
            rgba[i * 4:i * 4 + 3] = out[i * 3:i * 3 + 3]
            rgba[i * 4 + 3] = 255
    elif ctype == 0:
        for i in range(width * height):
            g = out[i]
            rgba[i * 4:i * 4 + 4] = bytes((g, g, g, 255))
    elif ctype == 4:
        for i in range(width * height):
            g, a = out[i * 2], out[i * 2 + 1]
            rgba[i * 4:i * 4 + 4] = bytes((g, g, g, a))
    elif ctype == 3:
        if palette is None:
            raise ValueError("paletted PNG missing PLTE")
        for i in range(width * height):
            idx = out[i]
            rgba[i * 4:i * 4 + 3] = palette[idx * 3:idx * 3 + 3]
            rgba[i * 4 + 3] = trns[idx] if (trns and idx < len(trns)) else 255
    return width, height, rgba


def load_image(path):
    """Load a .png or .xnb into (w, h, rgba)."""
    with open(path, "rb") as fh:
        data = fh.read()
    if data[:3] == b"XNB":
        return decode_xnb_rgba(data)
    return decode_png(data)


# ------------------------------------------------------------- cell analysis

def _pca_part(pts, cw, ch):
    """PCA over one connected component's (x, y) pixels. Returns a part dict:
    n, cx, cy (centroid), angle (directed grip->tip, deg, 0 = up, CW positive),
    grip, tip, length, elong, warns[]."""
    n = len(pts)
    mx = sum(p[0] for p in pts) / n
    my = sum(p[1] for p in pts) / n
    cxx = sum((x - mx) ** 2 for x, _ in pts) / n
    cyy = sum((y - my) ** 2 for _, y in pts) / n
    cxy = sum((x - mx) * (y - my) for x, y in pts) / n
    # principal axis
    phi = 0.5 * math.atan2(2.0 * cxy, cxx - cyy)
    ax, ay = math.cos(phi), math.sin(phi)
    tr, det = cxx + cyy, cxx * cyy - cxy * cxy
    disc = max(0.0, tr * tr / 4.0 - det)
    l1 = tr / 2.0 + math.sqrt(disc)              # major eigenvalue
    l2 = max(1e-6, tr / 2.0 - math.sqrt(disc))   # minor
    elong = math.sqrt(l1 / l2)
    # extent along axis
    tmin = tmax = 0.0
    for x, y in pts:
        t = (x - mx) * ax + (y - my) * ay
        if t < tmin:
            tmin = t
        if t > tmax:
            tmax = t
    e1 = (mx + tmin * ax, my + tmin * ay)
    e2 = (mx + tmax * ax, my + tmax * ay)
    # tip = axis endpoint farther from the cell center, grip = nearer
    ccx, ccy = cw / 2.0, ch / 2.0
    d1 = (e1[0] - ccx) ** 2 + (e1[1] - ccy) ** 2
    d2 = (e2[0] - ccx) ** 2 + (e2[1] - ccy) ** 2
    tip, grip = (e1, e2) if d1 >= d2 else (e2, e1)
    dirx, diry = tip[0] - grip[0], tip[1] - grip[1]
    length = math.hypot(dirx, diry)
    angle = math.degrees(math.atan2(dirx, -diry)) if length > 1e-6 else 0.0
    warns = []
    if n < MIN_PIXELS:
        warns.append("only %d opaque px" % n)
    if elong < MIN_ELONGATION:
        warns.append("low elongation %.2f (foreshortened/blob - angle unreliable)" % elong)
    return {"n": n, "cx": mx, "cy": my, "angle": angle, "grip": grip, "tip": tip,
            "length": length, "elong": elong, "warns": warns}


def _axis_diff(a_deg, b_deg):
    """Difference between two undirected axes (mod 180)."""
    d = (a_deg - b_deg) % 180.0
    return min(d, 180.0 - d)


def _should_merge(a, b, cw, ch):
    """Are two components segments of ONE weapon (e.g. the transparent hand
    cutout splits a spear/hammer shaft) rather than separate weapons
    (dual-wield daggers)? Merge when they are collinear: the line connecting
    the centroids runs along both parts' axes."""
    conn = math.degrees(math.atan2(b["cx"] - a["cx"], -(b["cy"] - a["cy"])))
    dist = math.hypot(b["cx"] - a["cx"], b["cy"] - a["cy"])
    good_a = a["elong"] >= 1.6 and a["n"] >= 8
    good_b = b["elong"] >= 1.6 and b["n"] >= 8
    if good_a and good_b:
        return (_axis_diff(a["angle"], b["angle"]) < 20.0
                and _axis_diff(a["angle"], conn) < 25.0
                and _axis_diff(b["angle"], conn) < 25.0)
    if good_a or good_b:
        g = a if good_a else b
        # blob (e.g. hammer head) sitting on the directional part's axis line
        return _axis_diff(g["angle"], conn) < 25.0 and dist < g["length"] * 0.9 + 8.0
    return True  # two blobs in one cell - treat as one object


def analyze_cell(rgba, sheet_w, x0, y0, cw, ch):
    """Analyse one cell. Opaque pixels are split into 8-connected components,
    collinear components are re-merged (hand cutouts split one weapon in two);
    what remains >= 8 px becomes a 'part' with its own PCA (vanilla dagger
    frames contain TWO daggers - dual wield). Returns None if empty, else the
    largest part's fields plus 'parts' (all placeable parts) and cell warns."""
    pix = set()
    border = False
    for dy in range(ch):
        row = ((y0 + dy) * sheet_w + x0) * 4 + 3
        for dx in range(cw):
            if rgba[row + dx * 4] >= ALPHA_MIN:
                pix.add((dx, dy))
                if dx == 0 or dy == 0 or dx == cw - 1 or dy == ch - 1:
                    border = True
    if not pix:
        return None
    # connected components (8-connectivity)
    comps = []
    todo = set(pix)
    while todo:
        seed = todo.pop()
        comp = [seed]
        stack = [seed]
        while stack:
            x, y = stack.pop()
            for nx in (x - 1, x, x + 1):
                for ny in (y - 1, y, y + 1):
                    if (nx, ny) in todo:
                        todo.discard((nx, ny))
                        comp.append((nx, ny))
                        stack.append((nx, ny))
        comps.append(comp)
    comps.sort(key=len, reverse=True)
    big = [c for c in comps if len(c) >= 8] or [comps[0]]
    stray = sum(len(c) for c in comps) - sum(len(c) for c in big)
    # merge collinear components (one weapon split by the hand cutout)
    pcas = [_pca_part(c, cw, ch) for c in big]
    merged = True
    while merged and len(big) > 1:
        merged = False
        for i in range(len(big)):
            for j in range(i + 1, len(big)):
                if _should_merge(pcas[i], pcas[j], cw, ch):
                    big[i] = big[i] + big[j]
                    del big[j], pcas[j]
                    pcas[i] = _pca_part(big[i], cw, ch)
                    merged = True
                    break
            if merged:
                break
    parts = sorted(pcas, key=lambda p: -p["n"])
    main = parts[0]
    warns = list(main["warns"])
    for p in parts[1:]:
        warns += ["part2+: " + w for w in p["warns"]]
    if len(big) > 1:
        warns.append("%d disjoint parts (dual-wield frame? each placed separately)"
                     % len(big))
    if stray:
        warns.append("%d stray px in tiny blobs ignored" % stray)
    if border:
        warns.append("art touches cell border")
    info = dict(main)
    info["parts"] = parts
    info["warns"] = warns
    return info


def analyze_sheet(w, h, rgba, fw, fh):
    """Return list of cells: dicts with col,row,x0,y0 and analyze_cell info (or None).
    Adds a 'motion smear' warning to cells much longer than the confident median:
    vanilla attack frames often contain a painted swoosh arc, not a rigid weapon."""
    cols, rows = w // fw, h // fh
    cells = []
    for r in range(rows):
        for c in range(cols):
            x0, y0 = c * fw, r * fh
            info = analyze_cell(rgba, w, x0, y0, fw, fh)
            cells.append({"col": c, "row": r, "x0": x0, "y0": y0, "info": info})
    lens = sorted(c["info"]["length"] for c in cells
                  if c["info"] and not c["info"]["warns"])
    if lens:
        median = lens[len(lens) // 2]
        for c in cells:
            if c["info"] and c["info"]["length"] > 1.7 * median:
                c["info"]["smear"] = True
                c["info"]["warns"].append(
                    "length %.0f >> median %.0f (likely motion-smear frame - "
                    "drawn as a rigid weapon)" % (c["info"]["length"], median))
    return cells


# --------------------------------------------------------------- compositing

def paste_rotated(dst, dst_w, dx0, dy0, cw, ch, src, src_w, src_h,
                  src_grip, dst_grip, theta_deg, scale):
    """Inverse-map paste: rotate src by theta about src_grip, scale, and paste so
    src_grip lands on dst_grip (cell-local) of the cell at dx0,dy0. The caller
    passes the cell angle (deg, 0 = up) and this maps the up-pointing src into
    the cell. Upscale/1:1 = nearest-neighbour (crisp pixel art). Downscale =
    coverage supersampling (plain NN drops most pixels of thin blades)."""
    th = math.radians(theta_deg)
    c, s = math.cos(th), math.sin(th)
    gx, gy = dst_grip
    ux, uy = src_grip
    inv = 1.0 / scale
    if scale >= 0.999:  # nearest neighbour
        for py in range(ch):
            vy = py + 0.5 - gy
            for px in range(cw):
                vx = px + 0.5 - gx
                # R(-theta) * v / scale + src_grip
                sx = (c * vx + s * vy) * inv + ux
                sy = (-s * vx + c * vy) * inv + uy
                ix, iy = int(math.floor(sx)), int(math.floor(sy))
                if 0 <= ix < src_w and 0 <= iy < src_h:
                    so = (iy * src_w + ix) * 4
                    if src[so + 3] >= ALPHA_MIN:
                        do = ((dy0 + py) * dst_w + dx0 + px) * 4
                        dst[do:do + 4] = src[so:so + 4]
        return
    # downscale: k x k subsamples per dest pixel, majority colour, coverage gate
    k = min(6, int(math.ceil(inv)) + 1)
    offs = [(i + 0.5) / k for i in range(k)]
    need = k * k * 0.28
    for py in range(ch):
        for px in range(cw):
            tally = {}
            hits = 0
            for oy in offs:
                vy = py + oy - gy
                for ox in offs:
                    vx = px + ox - gx
                    sx = (c * vx + s * vy) * inv + ux
                    sy = (-s * vx + c * vy) * inv + uy
                    ix, iy = int(math.floor(sx)), int(math.floor(sy))
                    if 0 <= ix < src_w and 0 <= iy < src_h:
                        so = (iy * src_w + ix) * 4
                        if src[so + 3] >= ALPHA_MIN:
                            hits += 1
                            key = bytes(src[so:so + 4])
                            tally[key] = tally.get(key, 0) + 1
            if hits >= need:
                do = ((dy0 + py) * dst_w + dx0 + px) * 4
                dst[do:do + 4] = max(tally.items(), key=lambda kv: kv[1])[0]


# --------------------------------------------------------- smear keep/transfer

def build_smear_lut(t_rgba, sheet_w, cells, fw, fh, user_rgba, bands=8):
    """Luminance-band palette LUT for --smear transfer.

    Collect the unique colours of the template's CLEAN (non-smear, non-empty)
    weapon cells and of the user's PNG; sort each by luminance and split into up
    to `bands` bands (fewer if either palette is tiny, e.g. a <=3-colour or
    grayscale weapon - bands collapse gracefully); band k of the template maps
    to the median colour of band k of the user palette (dark->dark,
    highlight->highlight). The returned callable maps ANY (r, g, b) by its
    luminance - smear-only colours such as white motion streaks land in the top
    band and become the user's brightest colours. Returns None if either side
    has no opaque pixels."""
    def lum(c):
        return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
    t_cols = set()
    for cell in cells:
        info = cell["info"]
        if not info or info.get("smear"):
            continue
        for dy in range(fh):
            base = ((cell["y0"] + dy) * sheet_w + cell["x0"]) * 4
            for dx in range(fw):
                o = base + dx * 4
                if t_rgba[o + 3] >= ALPHA_MIN:
                    t_cols.add((t_rgba[o], t_rgba[o + 1], t_rgba[o + 2]))
    u_cols = set()
    for i in range(0, len(user_rgba), 4):
        if user_rgba[i + 3] >= ALPHA_MIN:
            u_cols.add((user_rgba[i], user_rgba[i + 1], user_rgba[i + 2]))
    if not t_cols or not u_cols:
        return None
    t_lums = sorted({lum(c) for c in t_cols})
    u_sorted = sorted(u_cols, key=lum)
    n = max(1, min(bands, len(u_sorted), len(t_lums)))
    bounds = [t_lums[k * len(t_lums) // n] for k in range(1, n)]  # band k starts here
    reps = []
    for b in range(n):
        lo = b * len(u_sorted) // n
        hi = max(lo + 1, (b + 1) * len(u_sorted) // n)
        seg = u_sorted[lo:hi]
        reps.append(seg[len(seg) // 2])
    def map_rgb(r, g, b):
        light = 0.299 * r + 0.587 * g + 0.114 * b
        band = 0
        for t in bounds:
            if light >= t:
                band += 1
            else:
                break
        return reps[band]
    return map_rgb


def copy_template_cell(dst, t_rgba, sheet_w, x0, y0, cw, ch, lut=None):
    """--smear keep/transfer: copy one template cell into the output sheet
    verbatim (lut=None) or with each non-transparent pixel's RGB remapped
    through the palette LUT while its alpha is preserved exactly."""
    for dy in range(ch):
        base = ((y0 + dy) * sheet_w + x0) * 4
        for dx in range(cw):
            o = base + dx * 4
            a = t_rgba[o + 3]
            if a == 0:
                continue
            if lut is None:
                dst[o:o + 4] = t_rgba[o:o + 4]
            else:
                r, g, b = lut(t_rgba[o], t_rgba[o + 1], t_rgba[o + 2])
                dst[o] = r
                dst[o + 1] = g
                dst[o + 2] = b
                dst[o + 3] = a


# ----------------------------------------------------------------- templates

def discover_templates(content_dir):
    """Return {lowercase_name: path} for every xnb under character/tools."""
    root = os.path.join(content_dir, TOOLS_SUBPATH)
    found = {}
    if not os.path.isdir(root):
        return found
    for r, _dirs, files in os.walk(root):
        for f in sorted(files):
            if f.lower().endswith(".xnb"):
                found[f[:-4].lower()] = os.path.join(r, f)
    return found


def resolve_template(name, content_dir):
    if os.path.isfile(name):
        return name
    templates = discover_templates(content_dir)
    key = name.lower()
    if key in templates:
        return templates[key]
    raise SystemExit("ERROR: template '%s' not found. Discovered: %s\n"
                     "(or pass a path to an .xnb/.png sheet directly)"
                     % (name, ", ".join(sorted(templates)) or "none - check --content"))


# -------------------------------------------------------------- compare strip

def build_compare(tw, th, t_rgba, g_rgba, cells, fw, fh, per_band=12, zoom=2):
    """All non-empty cells, wrapped: each band = template row over generated row.
    Warned cells get an orange tick in the top-left corner; smear cells handled
    by --smear keep/transfer get a BLUE tick instead (eyeball the recolour)."""
    nonempty = [c for c in cells if c["info"]]
    n = len(nonempty)
    if n == 0:
        return None
    gap = 1
    cols = min(per_band, n)
    bands = (n + cols - 1) // cols
    out_w = (cols * (fw + gap) + gap) * zoom
    band_h = 2 * (fh + gap)
    out_h = (bands * (band_h + gap) + gap) * zoom
    out = bytearray(out_w * out_h * 4)
    for i in range(out_w * out_h):
        out[i * 4:i * 4 + 4] = b"\x20\x22\x28\xff"
    for idx, cell in enumerate(nonempty):
        band, col = idx // cols, idx % cols
        for rowi, src in enumerate((t_rgba, g_rgba)):
            ox = (gap + col * (fw + gap)) * zoom
            oy = (gap + band * (band_h + gap) + rowi * (fh + gap)) * zoom
            for py in range(fh * zoom):
                sy = cell["y0"] + py // zoom
                for px in range(fw * zoom):
                    sx = cell["x0"] + px // zoom
                    so = (sy * tw + sx) * 4
                    if src[so + 3] >= ALPHA_MIN:
                        do = ((oy + py) * out_w + ox + px) * 4
                        out[do:do + 4] = src[so:so + 4]
            if rowi == 0 and cell["info"]["warns"]:
                tick = (b"\x3d\x7c\xe8\xff" if cell["info"].get("smear_handled")
                        else b"\xe8\xa3\x3d\xff")
                for py in range(3 * zoom):
                    for px in range(3 * zoom):
                        do = ((oy + py) * out_w + ox + px) * 4
                        out[do:do + 4] = tick
    return out_w, out_h, out


# ----------------------------------------------------------------------- main

def angle_diff(a, b):
    d = (a - b) % 360.0
    return d if d <= 180.0 else 360.0 - d


def generate(template_path, fw, fh, user_w, user_h, user_rgba, user_grip, verbose=True,
             smear_mode="straight"):
    """Core pipeline. Returns (tw, th, t_rgba, out_rgba, cells, stats).
    smear_mode: 'straight' pastes the rigid weapon into motion-smear cells,
    'keep' copies the template's smear art verbatim, 'transfer' copies it with
    the palette remapped to the user weapon's colours. Only length-flagged
    smear cells are affected - foreshortened/blob cells always get the straight
    paste (their art IS a weapon pose, just compressed)."""
    t_w, t_h, t_rgba = load_image(template_path)
    if t_w % fw or t_h % fh:
        print("WARNING: template %dx%d is not a multiple of frame size %dx%d - "
              "trailing partial cells ignored" % (t_w, t_h, fw, fh))
    cells = analyze_sheet(t_w, t_h, t_rgba, fw, fh)
    confident = [c["info"]["length"] for c in cells
                 if c["info"] and not c["info"]["warns"]]
    pool = confident or [c["info"]["length"] for c in cells if c["info"]]
    if not pool:
        raise SystemExit("ERROR: template sheet has no opaque cells - wrong file?")
    pool.sort()
    median_len = pool[len(pool) // 2]
    # user weapon length: grip to farthest opaque pixel
    gx, gy = user_grip
    best = 0.0
    for y in range(user_h):
        for x in range(user_w):
            if user_rgba[(y * user_w + x) * 4 + 3] >= ALPHA_MIN:
                d = math.hypot(x + 0.5 - gx, y + 0.5 - gy)
                if d > best:
                    best = d
    if best <= 0.0:
        raise SystemExit("ERROR: input image has no opaque pixels")
    scale = median_len / best
    lut = None
    if smear_mode == "transfer":
        lut = build_smear_lut(t_rgba, t_w, cells, fw, fh, user_rgba)
        if lut is None:
            print("WARNING: no colours to build the transfer LUT from - "
                  "falling back to --smear keep")
    out = bytearray(t_w * t_h * 4)
    n_filled = n_warn = n_smear = 0
    for cell in cells:
        info = cell["info"]
        if not info:
            continue
        n_filled += 1
        if info["warns"]:
            n_warn += 1
            if verbose:
                print("  WARN cell c%02d r%d: %s" % (cell["col"], cell["row"],
                                                     "; ".join(info["warns"])))
        if info.get("smear") and smear_mode != "straight":
            n_smear += 1
            info["smear_handled"] = True
            copy_template_cell(out, t_rgba, t_w, cell["x0"], cell["y0"], fw, fh, lut)
            continue
        for part in info["parts"]:
            paste_rotated(out, t_w, cell["x0"], cell["y0"], fw, fh,
                          user_rgba, user_w, user_h, user_grip,
                          part["grip"], part["angle"], scale)
    stats = {"w": t_w, "h": t_h, "cols": t_w // fw, "rows": t_h // fh,
             "filled": n_filled, "warned": n_warn, "smear_handled": n_smear,
             "median_len": median_len, "user_len": best, "scale": scale}
    return t_w, t_h, t_rgba, out, cells, stats


def main():
    ap = argparse.ArgumentParser(
        description="Generate a rig-aligned held-item sheet from one weapon PNG "
                    "(drawn pointing UP, grip at bottom-center).")
    ap.add_argument("input", nargs="?", help="your weapon .png")
    ap.add_argument("--template", default="Sword",
                    help="vanilla shape name (Sword, Sword_leaf, Sword_long, Dagger, "
                         "Cestus, Spear, Spear_ribbon, Hammer, Hammer_axe, ...) or a "
                         "path to an .xnb/.png sheet (default: %(default)s)")
    ap.add_argument("--grip", default=None, metavar="X,Y",
                    help="grip point in YOUR image, pixels (default: bottom-center)")
    ap.add_argument("--content", default=DEFAULT_CONTENT,
                    help="game Content dir (default: %(default)s)")
    ap.add_argument("--frame", default="48x48", metavar="WxH",
                    help="frame/cell size (default: %(default)s)")
    ap.add_argument("--smear", default="straight",
                    choices=("straight", "keep", "transfer"),
                    help="how to fill motion-smear attack cells (cells whose weapon "
                         "length >> median, i.e. painted swoosh arcs). 'straight' "
                         "(default): draw your rigid weapon at the recovered angle, "
                         "orange tick. 'keep': copy the template's smear art verbatim, "
                         "vanilla colours - fine when your weapon matches vanilla metal "
                         "tones. 'transfer': keep the template smear's shape/alpha but "
                         "remap its palette to YOUR weapon's colours via a luminance-band "
                         "LUT (dark->your darks, highlights->your highlights). keep/"
                         "transfer cells get a BLUE compare-strip tick. Foreshortened/"
                         "blob cells are NOT affected by this option: their art IS a "
                         "real weapon pose, just compressed, so the straight-weapon "
                         "paste stays correct for them under every mode.")
    ap.add_argument("-o", "--out", default=None, help="output sheet path")
    ap.add_argument("--list-templates", action="store_true",
                    help="print discovered template sheets and exit")
    ap.add_argument("--quiet", action="store_true", help="suppress per-cell warnings")
    args = ap.parse_args()

    fw, fh = (int(v) for v in args.frame.lower().split("x"))

    if args.list_templates:
        templates = discover_templates(args.content)
        if not templates:
            print("No templates found under %s" %
                  os.path.join(args.content, TOOLS_SUBPATH))
            return 1
        print("Discovered held-item template sheets (use the name with --template):")
        for name in sorted(templates):
            try:
                w, h, _ = load_image(templates[name])
                dims = "%dx%d (%dx%d cells of %dx%d)" % (w, h, w // fw, h // fh, fw, fh)
            except (XnbSkip, ValueError, OSError) as e:
                dims = "UNDECODABLE: %s" % e
            print("  %-16s %s" % (name, dims))
        return 0

    if not args.input:
        ap.error("input PNG required (or use --list-templates)")

    template_path = resolve_template(args.template, args.content)
    print("Template: %s" % template_path)

    user_w, user_h, user_rgba = load_image(args.input)
    if args.grip:
        gx, gy = (float(v) for v in args.grip.split(","))
        user_grip = (gx + 0.5, gy + 0.5)
    else:
        user_grip = (user_w / 2.0, user_h - 0.5)

    t_w, t_h, t_rgba, out, cells, st = generate(
        template_path, fw, fh, user_w, user_h, user_rgba, user_grip,
        verbose=not args.quiet, smear_mode=args.smear)

    base = os.path.splitext(args.input)[0]
    out_path = args.out or base + "_sheet.png"
    with open(out_path, "wb") as fh_:
        fh_.write(encode_png(t_w, t_h, out))
    cmp_path = os.path.splitext(out_path)[0].replace("_sheet", "") + "_compare.png"
    cmp_img = build_compare(t_w, t_h, t_rgba, out, cells, fw, fh)
    if cmp_img:
        with open(cmp_path, "wb") as fh_:
            fh_.write(encode_png(*cmp_img))

    print()
    print("=== make_held_sheet summary ===")
    print("  template      : %dx%d, %dx%d cells of %dx%d"
          % (st["w"], st["h"], st["cols"], st["rows"], fw, fh))
    print("  cells filled  : %d (empty stay empty)" % st["filled"])
    print("  low-confidence: %d cells warned (check them in the compare strip)"
          % st["warned"])
    if st["smear_handled"]:
        print("  smear cells   : %d filled with template art (--smear %s, blue "
              "tick in compare strip)" % (st["smear_handled"], args.smear))
    print("  scale         : your %.1fpx weapon -> %.1fpx median template length "
          "(x%.3f)" % (st["user_len"], st["median_len"], st["scale"]))
    print("  sheet         : %s" % os.path.abspath(out_path))
    if cmp_img:
        print("  compare strip : %s (template row above, yours below)"
              % os.path.abspath(cmp_path))
    print("Point your pack's display.sheet at the generated PNG "
          "(frameWidth/frameHeight %dx%d)." % (fw, fh))
    return 0


if __name__ == "__main__":
    sys.exit(main())
