#!/usr/bin/env python3
"""
asset_catalog.py - generate a self-contained, browsable HTML catalog of everything a
Content Forge pack can reference: game sprite sheets (with thumbnails decoded straight
from the .xnb files) plus all the id lists from reference/*.txt.

Usage:
    python tools/asset_catalog.py                       # default Steam content dir
    python tools/asset_catalog.py "D:\\Games\\Romestead\\Content" -o my-catalog.html

Output: asset-catalog.html (dark theme, tabs, instant filter, click-to-copy,
click a thumbnail for a zoomed view). Thumbnails are embedded as base64 data URIs;
if the embedded total would exceed the budget (~60 MB) the tool auto-switches to
writing PNGs into an `asset-catalog_files/` folder next to the html.

Stdlib only. XNB decode: uncompressed Texture2D, SurfaceFormat.Color (RGBA32) -
the same subset xnb2png.ps1 handles. Compressed / other-format files are skipped
and counted.
"""

import argparse
import base64
import html
import os
import struct
import sys
import time
import zlib
from datetime import datetime

DEFAULT_CONTENT = r"C:\Program Files (x86)\Steam\steamapps\common\Romestead\Content"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REFERENCE_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "reference"))

THUMB_MAX = 96                    # max thumbnail edge, px
EMBED_BUDGET = 60 * 1024 * 1024   # max total base64 chars embedded in the html
PROGRESS_EVERY = 200

# ---------------------------------------------------------------- XNB decode

class XnbSkip(Exception):
    """Raised with a short reason string when a file can't be decoded."""


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
    if flags & 0xC0:  # 0x80 = LZ4, 0x40 = LZX/xcompress
        raise XnbSkip("compressed XNB")
    try:
        pos = 10  # 'XNB' + platform + version + flags + total-size(int32)
        type_count, pos = _read_7bit(data, pos)
        is_texture = False
        for _ in range(type_count):
            length, pos = _read_7bit(data, pos)
            if b"Texture2DReader" in data[pos:pos + length]:
                is_texture = True
            pos += length + 4  # reader name + reader version int32
        _, pos = _read_7bit(data, pos)  # shared resource count
        _, pos = _read_7bit(data, pos)  # object type id
        fmt, width, height, _mips, dsize = struct.unpack_from("<5i", data, pos)
        pos += 20
    except (IndexError, struct.error):
        raise XnbSkip("truncated/garbled header")
    if not is_texture:
        raise XnbSkip("not a Texture2D")
    if fmt != 0:
        raise XnbSkip("SurfaceFormat %d (only Color)" % fmt)
    if width <= 0 or height <= 0 or dsize != width * height * 4:
        raise XnbSkip("size mismatch")
    if len(data) < pos + dsize:
        raise XnbSkip("truncated pixel data")
    return width, height, data[pos:pos + dsize]


# ------------------------------------------------------------- PNG encoding

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


def make_thumb(width, height, rgba, max_edge=THUMB_MAX):
    """Nearest-neighbour downscale to fit max_edge (keeps pixel art crisp)."""
    if width <= max_edge and height <= max_edge:
        return width, height, rgba
    if width >= height:
        tw = max_edge
        th = max(1, round(height * max_edge / width))
    else:
        th = max_edge
        tw = max(1, round(width * max_edge / height))
    xs = [min(width - 1, int(x * width / tw)) * 4 for x in range(tw)]
    out = bytearray(tw * th * 4)
    o = 0
    for y in range(th):
        row = min(height - 1, int(y * height / th)) * width * 4
        for sx in xs:
            out[o:o + 4] = rgba[row + sx:row + sx + 4]
            o += 4
    return tw, th, bytes(out)


# ----------------------------------------------------------- data gathering

def scan_sprites(content_dir):
    """Walk media/sprites/**, decode each xnb, return (groups, stats).

    groups: {top_folder: [ {name, relpath, w, h, png_bytes} ]}
    """
    sprites_root = os.path.join(content_dir, "media", "sprites")
    groups = {}
    stats = {"total": 0, "decoded": 0, "skipped": 0, "reasons": {}}
    if not os.path.isdir(sprites_root):
        print("WARNING: sprites dir not found: %s" % sprites_root)
        return groups, stats
    t0 = time.time()
    for root, dirs, files in os.walk(sprites_root):
        dirs.sort()
        for fname in sorted(files):
            if not fname.lower().endswith(".xnb"):
                continue
            stats["total"] += 1
            path = os.path.join(root, fname)
            rel = os.path.relpath(path, content_dir).replace("\\", "/")
            try:
                with open(path, "rb") as fh:
                    data = fh.read()
                w, h, rgba = decode_xnb_rgba(data)
                tw, th, trgba = make_thumb(w, h, rgba)
                png = encode_png(tw, th, trgba)
            except XnbSkip as e:
                stats["skipped"] += 1
                stats["reasons"][str(e)] = stats["reasons"].get(str(e), 0) + 1
                continue
            except OSError as e:
                stats["skipped"] += 1
                reason = "read error (%s)" % e.__class__.__name__
                stats["reasons"][reason] = stats["reasons"].get(reason, 0) + 1
                continue
            under = os.path.relpath(path, sprites_root).replace("\\", "/")
            group = under.split("/", 1)[0] if "/" in under else "(root)"
            groups.setdefault(group, []).append({
                "name": fname[:-4],
                "relpath": rel[:-4],   # content path without .xnb - the referenceable form
                "w": w, "h": h,
                "png": png,
            })
            stats["decoded"] += 1
            if stats["total"] % PROGRESS_EVERY == 0:
                print("  ... %d xnb processed (%d decoded) in %.1fs"
                      % (stats["total"], stats["decoded"], time.time() - t0))
    return groups, stats


def load_reference_files():
    """Return ordered list of (tab_label, filename, lines) for reference/*.txt."""
    order = ["icon-ids.txt", "item-ids.txt", "creature-ids.txt", "entity-ids.txt",
             "decoration-ids.txt", "droptable-ids.txt", "aura-ids.txt",
             "display-ids.txt", "citizen-ids.txt", "raid-ids.txt",
             "music-track-ids.txt"]
    result = []
    if not os.path.isdir(REFERENCE_DIR):
        print("WARNING: reference dir not found: %s" % REFERENCE_DIR)
        return result
    present = sorted(f for f in os.listdir(REFERENCE_DIR) if f.endswith(".txt"))
    ordered = [f for f in order if f in present] + [f for f in present if f not in order]
    for fname in ordered:
        with open(os.path.join(REFERENCE_DIR, fname), "r", encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        label = fname[:-4]
        if label.endswith("-ids"):
            label = label[:-4]
        label = label.replace("-", " ")
        result.append((label, fname, lines))
    return result


# -------------------------------------------------------------- HTML output

CSS = """
:root { --bg:#14161a; --panel:#1c1f26; --line:#2c313c; --fg:#d6dae3; --dim:#7d8694;
        --acc:#e8a33d; --acc2:#5fb4d9; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--fg);
       font:14px/1.45 "Segoe UI",system-ui,sans-serif; }
header { position:sticky; top:0; z-index:5; background:var(--panel);
         border-bottom:1px solid var(--line); padding:10px 16px; }
h1 { font-size:16px; margin:0 0 8px; color:var(--acc); }
h1 small { color:var(--dim); font-weight:normal; }
#filter { width:340px; max-width:90%; padding:6px 10px; background:var(--bg);
          color:var(--fg); border:1px solid var(--line); border-radius:6px; }
#filter:focus { outline:1px solid var(--acc2); }
#shown { color:var(--dim); margin-left:10px; }
nav { display:flex; flex-wrap:wrap; gap:4px; margin-top:8px; }
nav .lbl { color:var(--dim); font-size:11px; text-transform:uppercase;
           letter-spacing:.05em; align-self:center; margin:0 4px 0 8px; }
nav button { background:var(--bg); color:var(--fg); border:1px solid var(--line);
             border-radius:6px; padding:4px 10px; cursor:pointer; font-size:13px; }
nav button:hover { border-color:var(--acc2); }
nav button.on { background:var(--acc); color:#14161a; border-color:var(--acc);
                font-weight:600; }
nav button .n { opacity:.65; font-size:11px; }
main { padding:14px 16px 60px; }
.tab { display:none; }
.tab.on { display:block; }
.note { background:#23272f; border:1px solid var(--line); border-left:3px solid var(--acc2);
        padding:8px 12px; border-radius:6px; margin-bottom:12px; color:var(--dim); }
.grid { display:flex; flex-wrap:wrap; gap:8px; }
.card { width:140px; background:var(--panel); border:1px solid var(--line);
        border-radius:8px; padding:8px; text-align:center; }
.card .im { height:100px; display:flex; align-items:center; justify-content:center;
            cursor:zoom-in; }
.card img { max-width:96px; max-height:96px; image-rendering:pixelated;
            background:repeating-conic-gradient(#23262d 0 25%,#1a1d22 0 50%) 0 0/16px 16px; }
.card .nm { font-size:12px; word-break:break-all; margin-top:6px; cursor:copy; }
.card .nm:hover { color:var(--acc2); }
.card .dim { color:var(--dim); font-size:11px; }
.rows { max-width:1100px; }
.row { padding:2px 8px; border-radius:4px; font:13px/1.5 Consolas,monospace;
       white-space:pre-wrap; cursor:copy; }
.row:hover { background:#242a35; }
.row .cmt { color:var(--dim); }
.row.cmtline { color:var(--dim); cursor:default; }
.row.cmtline:hover { background:none; }
#big { display:none; position:fixed; inset:0; z-index:9; background:rgba(10,11,14,.92);
       flex-direction:column; align-items:center; justify-content:center; cursor:zoom-out; }
#big.on { display:flex; }
#big img { image-rendering:pixelated; max-width:92vw; max-height:80vh;
           background:repeating-conic-gradient(#23262d 0 25%,#1a1d22 0 50%) 0 0/24px 24px; }
#big .cap { margin-top:12px; font:13px Consolas,monospace; color:var(--acc);
            cursor:copy; }
#big .cap:hover { color:var(--acc2); }
#toast { position:fixed; bottom:18px; left:50%; transform:translateX(-50%);
         background:var(--acc); color:#14161a; padding:6px 16px; border-radius:6px;
         font-weight:600; display:none; z-index:10; }
footer { position:fixed; bottom:0; left:0; right:0; background:var(--panel);
         border-top:1px solid var(--line); color:var(--dim); font-size:11px;
         padding:4px 16px; }
"""

JS = """
var tabs=document.querySelectorAll('nav button[data-tab]');
var panels=document.querySelectorAll('.tab');
var filter=document.getElementById('filter');
var shown=document.getElementById('shown');
function activate(id){
  tabs.forEach(function(b){b.classList.toggle('on',b.dataset.tab===id);});
  panels.forEach(function(p){p.classList.toggle('on',p.id==='tab-'+id);});
  applyFilter();
}
tabs.forEach(function(b){b.addEventListener('click',function(){activate(b.dataset.tab);});});
function applyFilter(){
  var q=filter.value.toLowerCase();
  var panel=document.querySelector('.tab.on');
  if(!panel)return;
  var items=panel.querySelectorAll('[data-k]');
  var n=0;
  items.forEach(function(el){
    var hit=!q||el.dataset.k.indexOf(q)>=0;
    el.style.display=hit?'':'none';
    if(hit)n++;
  });
  shown.textContent=n+' / '+items.length+' shown';
}
filter.addEventListener('input',applyFilter);
var toast=document.getElementById('toast');
var toastT=null;
function copy(text){
  function done(){toast.textContent='copied: '+text;toast.style.display='block';
    clearTimeout(toastT);toastT=setTimeout(function(){toast.style.display='none';},1200);}
  if(navigator.clipboard&&navigator.clipboard.writeText){
    navigator.clipboard.writeText(text).then(done,function(){fallback();});
  }else{fallback();}
  function fallback(){var t=document.createElement('textarea');t.value=text;
    document.body.appendChild(t);t.select();
    try{document.execCommand('copy');done();}catch(e){}
    document.body.removeChild(t);}
}
var big=document.getElementById('big');
var bigImg=big.querySelector('img');
var bigCap=big.querySelector('.cap');
document.addEventListener('click',function(e){
  var t=e.target;
  if(t.closest&&t.closest('.im')){
    var card=t.closest('.card');
    bigImg.src=card.querySelector('img').src;
    bigCap.textContent=card.dataset.path+'  ('+card.dataset.dims+')';
    bigCap.dataset.copy=card.dataset.path;
    big.classList.add('on');
    return;
  }
  if(t===bigCap){copy(bigCap.dataset.copy);e.stopPropagation();return;}
  if(t.closest&&t.closest('#big')){big.classList.remove('on');return;}
  var c=t.closest&&t.closest('[data-copy]');
  if(c){copy(c.dataset.copy);}
});
document.addEventListener('keydown',function(e){
  if(e.key==='Escape')big.classList.remove('on');
  if(e.key==='/'&&document.activeElement!==filter){e.preventDefault();filter.focus();}
});
activate(tabs[0].dataset.tab);
"""


def esc(s):
    return html.escape(s, quote=True)


def render_text_tab(tab_id, lines, note=None):
    """Render a reference/*.txt file: comments dimmed, ids click-to-copy."""
    out = ['<div class="tab" id="tab-%s">' % tab_id]
    if note:
        out.append('<div class="note">%s</div>' % note)
    out.append('<div class="rows">')
    for line in lines:
        stripped = line.strip()
        if not stripped:
            continue
        key = esc(line.lower())
        if stripped.startswith("#"):
            out.append('<div class="row cmtline" data-k="%s">%s</div>' % (key, esc(line)))
            continue
        ident = stripped.split()[0]
        body = esc(line)
        if "#" in line:  # dim a trailing comment
            idx = line.index("#")
            body = esc(line[:idx]) + '<span class="cmt">' + esc(line[idx:]) + "</span>"
        out.append('<div class="row" data-k="%s" data-copy="%s" title="click to copy %s">%s</div>'
                   % (key, esc(ident), esc(ident), body))
    out.append("</div></div>")
    return "\n".join(out)


def render_sprite_tab(tab_id, entries, srcs):
    out = ['<div class="tab" id="tab-%s">' % tab_id,
           '<div class="note">Click the name to copy the content path '
           '(usable as a sheet/texture reference). Click the image to zoom.</div>',
           '<div class="grid">']
    for e in entries:
        dims = "%dx%d" % (e["w"], e["h"])
        out.append(
            '<div class="card" data-k="%s" data-path="%s" data-dims="%s">'
            '<div class="im"><img loading="lazy" src="%s" alt=""></div>'
            '<div class="nm" data-copy="%s" title="click to copy">%s</div>'
            '<div class="dim">%s</div></div>'
            % (esc(e["relpath"].lower()), esc(e["relpath"]), dims,
               srcs[id(e)], esc(e["relpath"]), esc(e["name"]), dims))
    out.append("</div></div>")
    return "\n".join(out)


def build_html(content_dir, sprite_groups, ref_tabs, srcs, mode):
    nav, body = [], []

    # --- Icons tab (text only - honest fallback, see note below)
    icon_note = ("Icon ids for packs (item icon / spellIcon / etc). No previews: icons are "
                 "cells inside runtime sprite sheets (media/interface/icons/**) and the "
                 "id&rarr;cell mapping lives in game data, not in static files. To eyeball "
                 "them, browse those sheets with tools/xnb2png.ps1.")
    icon_lines = []
    other_refs = []
    for label, fname, lines in ref_tabs:
        if fname == "icon-ids.txt":
            icon_lines = lines
        else:
            other_refs.append((label, fname, lines))
    n_ids = sum(1 for l in icon_lines if l.strip() and not l.strip().startswith("#"))
    nav.append('<span class="lbl">ids</span>')
    nav.append('<button data-tab="icons">icons <span class="n">%d</span></button>' % n_ids)
    body.append(render_text_tab("icons", icon_lines, icon_note))

    # --- catalog id tabs
    for label, fname, lines in other_refs:
        tab_id = "ref-" + fname[:-4]
        n = sum(1 for l in lines if l.strip() and not l.strip().startswith("#"))
        nav.append('<button data-tab="%s">%s <span class="n">%d</span></button>'
                   % (tab_id, esc(label), n))
        body.append(render_text_tab(
            tab_id, lines,
            "From reference/%s &mdash; click a line to copy its id." % esc(fname)))

    # --- sprite tabs
    nav.append('<span class="lbl">sprites</span>')
    for group in sorted(sprite_groups):
        entries = sprite_groups[group]
        tab_id = "sp-" + "".join(c if c.isalnum() else "_" for c in group)
        nav.append('<button data-tab="%s">%s <span class="n">%d</span></button>'
                   % (tab_id, esc(group), len(entries)))
        body.append(render_sprite_tab(tab_id, entries, srcs))

    generated = datetime.now().strftime("%Y-%m-%d %H:%M")
    return ("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">"
            "<title>Romestead asset catalog</title>"
            "<style>%s</style></head>\n<body>\n"
            "<header><h1>Romestead asset catalog "
            "<small>&mdash; Content Forge reference browser</small></h1>"
            "<input id=\"filter\" type=\"search\" placeholder=\"filter (substring, press /)\" "
            "autocomplete=\"off\"><span id=\"shown\"></span>"
            "<nav>%s</nav></header>\n"
            "<main>\n%s\n</main>\n"
            "<div id=\"big\"><img alt=\"\"><div class=\"cap\" title=\"click to copy\"></div></div>"
            "<div id=\"toast\"></div>\n"
            "<footer>generated %s from %s &mdash; thumbnails %s &mdash; "
            "tools/asset_catalog.py</footer>\n"
            "<script>%s</script>\n</body></html>"
            % (CSS, "".join(nav), "\n".join(body), esc(generated), esc(content_dir),
               mode, JS))


# ----------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description="Generate asset-catalog.html for Romestead packs.")
    ap.add_argument("content_dir", nargs="?", default=DEFAULT_CONTENT,
                    help="game Content directory (default: %(default)s)")
    ap.add_argument("-o", "--out", default="asset-catalog.html",
                    help="output html path (default: %(default)s)")
    args = ap.parse_args()

    content_dir = os.path.normpath(args.content_dir)
    if not os.path.isdir(content_dir):
        print("ERROR: content dir not found: %s" % content_dir)
        return 2

    t0 = time.time()
    print("Scanning sprites under %s ..." % os.path.join(content_dir, "media", "sprites"))
    sprite_groups, stats = scan_sprites(content_dir)
    ref_tabs = load_reference_files()

    # decide embed mode against the budget
    all_entries = [e for grp in sprite_groups.values() for e in grp]
    b64_total = sum((len(e["png"]) + 2) // 3 * 4 for e in all_entries)
    inline = b64_total <= EMBED_BUDGET
    srcs = {}
    out_html = os.path.abspath(args.out)
    if inline:
        mode = "inline (%.1f MB base64)" % (b64_total / 1048576.0)
        for e in all_entries:
            srcs[id(e)] = "data:image/png;base64," + base64.b64encode(e["png"]).decode("ascii")
    else:
        files_dir = os.path.splitext(out_html)[0] + "_files"
        files_rel = os.path.basename(files_dir)
        os.makedirs(files_dir, exist_ok=True)
        mode = "external folder %s (budget exceeded: %.1f MB base64)" % (
            files_rel, b64_total / 1048576.0)
        for e in all_entries:
            fname = e["relpath"].replace("/", "__") + ".png"
            with open(os.path.join(files_dir, fname), "wb") as fh:
                fh.write(e["png"])
            srcs[id(e)] = files_rel + "/" + fname

    doc = build_html(content_dir, sprite_groups, ref_tabs, srcs, mode)
    with open(out_html, "w", encoding="utf-8") as fh:
        fh.write(doc)

    elapsed = time.time() - t0
    print()
    print("=== asset_catalog summary ===")
    print("  sprites scanned : %d xnb" % stats["total"])
    print("  decoded         : %d" % stats["decoded"])
    print("  skipped         : %d" % stats["skipped"])
    for reason, n in sorted(stats["reasons"].items(), key=lambda kv: -kv[1]):
        print("      %-28s %d" % (reason, n))
    print("  reference tabs  : %d (%s)" % (len(ref_tabs),
          ", ".join(t[1] for t in ref_tabs) if ref_tabs else "none"))
    print("  thumbnail mode  : %s" % mode)
    print("  output          : %s (%.1f MB)" % (out_html, os.path.getsize(out_html) / 1048576.0))
    print("  runtime         : %.1fs" % elapsed)
    return 0


if __name__ == "__main__":
    sys.exit(main())
