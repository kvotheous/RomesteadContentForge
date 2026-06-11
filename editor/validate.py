#!/usr/bin/env python3
"""
Content Forge pack validator — pure Python, cross-platform (no .NET needed). Mirrors the C# `cfvalidate` checks:
structure, enum values, intra-pack references, namespacing. Used in-process by the editor's Validate button and
as a standalone CLI.

  python validate.py <pack.json | folder-of-packs>

Exit code 1 if any errors. Warnings don't fail.
"""
import json
import os
import sys

EQUIP_TYPES = {"helmet", "armor", "boots", "trinket", "weapon", "offhand", "lightsource", "lumberaxe",
               "pickaxe", "fishingrod", "ammunition", "back"}
MATERIALS = {"flint", "copper", "bronze", "iron", "silver", "gold", "steel", "cheat", "rusted"}
WEAPON_CLASSES = {"sword", "spear", "dagger", "sledgehammer", "bow", "crossbow", "javelin", "fists", "shield", "spelltome"}
USABLE_TYPES = {"special", "healing"}
ATTACK_TYPES = {"magic", "melee", "ranged"}
FLAGS = {"noflags", "food", "material", "ammunition", "money", "millable", "quest", "seed", "offering"}
DAMAGE_KEYS = {"true", "slashing", "slash", "piercing", "pierce", "bludgeoning", "bludgeon", "pyro", "fire",
               "chloro", "nature", "aqua", "water", "cosmo", "necro"}
STATIONS = {"campfire", "cauldron", "clay-barrel", "war-table"}
STAT_KEYS = {k.lower() for k in [
    "Health", "Energy", "EnergyRegeneration", "Armor", "CritChance", "AttackSpeed", "MovementSpeed", "MeleeDamage",
    "RangedDamage", "MagicDamage", "ThrowingDamage", "PickaxePower", "AxePower", "LumberPower", "HealingDoneModifier",
    "Knockback", "KnockbackResistance", "MagicDamageModifier", "RangedDamageModifier", "ThrowingDamageModifier",
    "BludgeoningDamageModifier", "PiercingDamageModifier", "NecroDamageModifier", "ChloroDamageModifier",
    "PyroDamageModifier", "CosmoDamageModifier", "PyroResistance", "AquaResistance", "ChloroResistance",
    "CosmoResistance", "NecroResistance", "PiercingResistance", "BludgeoningResistance", "MagicResistance", "LightSource"]}
CHAIN_EVENTS = {"onentityhit", "onterrainhit", "onanyhit", "onspellended"}


def validate_pack(path):
    """Validate one pack FILE. Returns (errors, warnings)."""
    try:
        with open(path, encoding="utf-8") as fh:
            pack = json.load(fh)
    except Exception as e:
        return ([f"JSON parse error: {e}"], [])
    if not isinstance(pack, dict):
        return (["empty / invalid pack."], [])
    return validate_pack_data(pack, os.path.dirname(os.path.abspath(path)))


def validate_pack_data(pack, d):
    """Validate an in-memory pack dict (d = folder for resolving .png paths). Returns (errors, warnings)."""
    errors, warnings = [], []

    def err(m): errors.append(m)
    def warn(m): warnings.append(m)

    items = pack.get("items") or []
    spells = pack.get("spells") or []
    recipes = pack.get("recipes") or []
    spell_ids = {s.get("id") for s in spells if isinstance(s, dict) and s.get("id")}
    item_ids = {i.get("id") for i in items if isinstance(i, dict) and i.get("id")}
    ns = (pack.get("namespace") or pack.get("id") or "").strip()
    seen = set()

    pack_stations = set()
    for sec in ("placeables", "furniture", "structures"):
        for f in (pack.get(sec) or []):
            if isinstance(f, dict) and f.get("station"):
                pack_stations.update(x.strip() for x in str(f["station"]).split(","))
                if not f.get("cloneEntity"):
                    err(f"placeable '{f.get('id')}': 'station' requires 'cloneEntity'.")



    def chk_dmg(dmg, where):
        if isinstance(dmg, dict):
            for k in dmg:
                if k.lower() not in DAMAGE_KEYS:
                    warn(f"{where}: unknown damage type '{k}'.")

    def chk_ns(cid, kind):
        if ns and cid and not str(cid).startswith(ns + ":"):
            warn(f"{kind} id '{cid}' is not namespaced '{ns}:...' — risks colliding with other packs.")

    # custom icon files exist?
    for k, v in (pack.get("icons") or {}).items():
        if not os.path.isfile(os.path.join(d, v)):
            warn(f"icon '{k}': file '{v}' not found next to the pack.")
    # custom sheet/palette/vfx files exist?
    for p in (pack.get("palettes") or []):
        if isinstance(p, dict) and p.get("sheet") and not os.path.isfile(os.path.join(d, p["sheet"])):
            warn(f"palette '{p.get('id')}': sheet '{p['sheet']}' not found.")
    for v in (pack.get("vfx") or []):
        if isinstance(v, dict) and v.get("sheet") and not os.path.isfile(os.path.join(d, v["sheet"])):
            warn(f"vfx '{v.get('id')}': sheet '{v['sheet']}' not found.")

    for s in spells:
        sid = (s or {}).get("id")
        if not sid:
            err("spell with empty id."); continue
        if ("spell:" + sid) in seen:
            err(f"duplicate spell id '{sid}'.")
        seen.add("spell:" + sid)
        chk_ns(sid, "spell")
        atk = s.get("attack")
        if isinstance(atk, dict):
            chk_dmg(atk.get("minDamage"), f"spell '{sid}' minDamage")
            chk_dmg(atk.get("maxDamage"), f"spell '{sid}' maxDamage")
            at = atk.get("attackType")
            if at and at.lower() not in ATTACK_TYPES:
                warn(f"spell '{sid}': unknown attackType '{at}'.")
        for c in (s.get("chain") or []):
            if isinstance(c, dict):
                if (c.get("event") or "OnAnyHit").lower() not in CHAIN_EVENTS:
                    warn(f"spell '{sid}': chain has unknown event '{c.get('event')}'.")
                if not c.get("spell"):
                    err(f"spell '{sid}': a chain entry has no 'spell'.")
        blocks = [b for b in ("aoe", "explosion", "cone", "line", "multiProjectile", "heal", "massHeal") if s.get(b)]
        if len(blocks) > 1:
            err(f"spell '{sid}': more than one behavior block ({', '.join(blocks)}) — pick one.")
        if s.get("line") and (s["line"].get("growSeconds") or 0) > 0 and not s.get("particleTrail"):
            warn(f"spell '{sid}': line growSeconds needs a particleTrail (vanilla NRE without one).")
        req = s.get("requires")
        if isinstance(req, dict):
            if not any(req.get(k) for k in ("skill", "skillLevel", "favour", "quest", "class")):
                warn(f"spell '{sid}': 'requires' block sets no condition.")
            rs = req.get("skill")
            if rs and ":" not in rs:
                warn(f"spell '{sid}': requires.skill '{rs}' looks malformed (expected 'skill:...' or a pack skill id).")

    for it in items:
        iid = (it or {}).get("id")
        if not iid:
            err("item with empty id."); continue
        if ("item:" + iid) in seen:
            err(f"duplicate item id '{iid}'.")
        seen.add("item:" + iid)
        if ":" not in iid:
            warn(f"item '{iid}': id has no namespace (use 'mymod:{iid}').")
        chk_ns(iid, "item")
        fl = it.get("flags")
        if isinstance(fl, list):
            fl = ",".join(str(x) for x in fl)
        if fl and fl.replace(" ", "").split(",")[0].lower() not in FLAGS:
            warn(f"item '{iid}': unknown flag '{fl}'.")
        eq = it.get("equippable")
        if isinstance(eq, dict):
            et = eq.get("equipmentType")
            if not et or et.lower() not in EQUIP_TYPES:
                err(f"item '{iid}': invalid equipmentType '{et}'.")
            mat = eq.get("material")
            if mat and mat.lower() not in MATERIALS:
                warn(f"item '{iid}': unknown material '{mat}'.")
            for key in (eq.get("statBonuses") or {}):
                if key.lower() not in STAT_KEYS:
                    warn(f"item '{iid}': unknown stat key '{key}'.")
            w = eq.get("weapon")
            if isinstance(w, dict):
                if (w.get("class") or "").lower() not in WEAPON_CLASSES:
                    warn(f"item '{iid}': unknown weapon class '{w.get('class')}'.")
                chk_dmg(w.get("minDamage"), f"item '{iid}' weapon minDamage")
                chk_dmg(w.get("maxDamage"), f"item '{iid}' weapon maxDamage")
            st = eq.get("spellTome")
            if isinstance(st, dict) and st.get("spellId") and st["spellId"] not in spell_ids:
                warn(f"item '{iid}': spellTome casts '{st['spellId']}' which isn't a spell in this pack (ok if vanilla/other-pack).")
        us = it.get("usable")
        if isinstance(us, dict):
            ut = us.get("type")
            if ut and ut.lower() not in USABLE_TYPES:
                err(f"item '{iid}': invalid usable type '{ut}'.")
            if ut and ut.lower() == "special":
                warn(f"item '{iid}': a 'Special' usable has no built-in use trigger — for a castable scroll use an Offhand spellTome.")

    for r in recipes:
        rid = (r or {}).get("id")
        if not rid:
            err("recipe with empty id."); continue
        if ("recipe:" + rid) in seen:
            err(f"duplicate recipe id '{rid}'.")
        seen.add("recipe:" + rid)
        if not r.get("crafted"):
            err(f"recipe '{rid}': missing 'crafted' item id.")
        elif r["crafted"] not in item_ids:
            warn(f"recipe '{rid}': crafts '{r['crafted']}' which isn't an item in this pack (ok if vanilla/other-pack).")
        if r.get("station") and r["station"].lower() not in STATIONS:
            if r["station"] not in pack_stations:
                warn(f"recipe '{rid}': unknown station '{r['station']}' (not vanilla and no placeable declares it — never craftable).")
        for k, v in (r.get("ingredients") or {}).items():
            if not isinstance(v, int) or v <= 0:
                warn(f"recipe '{rid}': ingredient '{k}' has count {v}.")

    for dr in (pack.get("drops") or []):
        if not isinstance(dr, dict):
            continue
        if not dr.get("table") and not dr.get("creature"):
            err("a drop entry needs 'table' (a drop-table id) or 'creature' (a creature whose death loot gains the item).")
        if dr.get("table") and dr.get("creature"):
            warn("a drop entry has BOTH 'table' and 'creature' — only the table path is used.")
        if not dr.get("item"):
            err("a drop entry has no 'item'.")
        ch = dr.get("chance")
        if ch is not None and (ch <= 0 or ch > 100):
            warn(f"drop into '{dr.get('table')}': chance {ch} is outside 0..100.")

    _placeables = (pack.get("placeables") or []) + (pack.get("furniture") or []) + (pack.get("structures") or [])
    for f in _placeables:
        if not isinstance(f, dict):
            continue
        fid = f.get("id")
        if not fid:
            err("a furniture entry has no 'id'.")
        else:
            chk_ns(fid, "furniture")
        if f.get("controller") or f.get("yarn") or f.get("sprite") or f.get("station") or f.get("parts"):
            # Interactive/talking furniture: an entity construction — needs an entity to clone, not decoration art
            if not f.get("cloneEntity") and not f.get("parts"):
                err(f"furniture '{fid}': 'controller'/'yarn'/'sprite' needs 'cloneEntity' (a vanilla entity name or Guid).")
            for pt in (f.get("parts") or []):
                if isinstance(pt, dict):
                    if not pt.get("sprite"):
                        err(f"furniture '{fid}': every composite part needs 'sprite'.")
                    elif not os.path.isfile(os.path.join(d, pt["sprite"])):
                        err(f"furniture '{fid}': part sprite '{pt['sprite']}' not found next to the pack.")
            if f.get("parts") and (f.get("yarn") or f.get("station")):
                err(f"furniture '{fid}': 'parts' can't be combined with yarn/station/controller.")
            spr = f.get("sprite")
            if spr and not os.path.isfile(os.path.join(d, spr)):
                err(f"furniture '{fid}': sprite '{spr}' not found next to the pack.")
            if f.get("spriteFrames") and not spr:
                err(f"furniture '{fid}': spriteFrames needs 'sprite'.")
            yarn = f.get("yarn")
            if yarn and not os.path.isfile(os.path.join(d, yarn)):
                err(f"furniture '{fid}': yarn file '{yarn}' not found next to the pack.")
        elif not f.get("spawnedId"):
            err(f"furniture '{fid}' has no 'spawnedId' (a vanilla decoration to reuse — see reference/decoration-ids.txt).")
        for dim in ("width", "height"):
            v = f.get(dim)
            if v is not None and (not isinstance(v, int) or v < 1):
                warn(f"furniture '{fid}': {dim} {v} should be an integer >= 1.")

    for c in (pack.get("creatures") or []):
        if not isinstance(c, dict):
            continue
        cid = c.get("id")
        if not cid:
            err("a creature entry has no 'id'.")
        else:
            chk_ns(cid, "creature")
        if not c.get("base"):
            err(f"creature '{cid}' has no 'base' (a vanilla creature to clone — see reference/creature-ids.txt).")
        col = c.get("color")
        if col is not None and (not isinstance(col, list) or len(col) < 3):
            warn(f"creature '{cid}': color should be [r, g, b].")

    for m in (pack.get("maps") or []):
        if not isinstance(m, dict):
            continue
        mid = m.get("id")
        if not mid:
            err("a map entry has no 'id'.")
        else:
            chk_ns(mid, "map")
        tmx = m.get("tmx")
        if not tmx:
            err(f"map '{mid}' has no 'tmx'.")
        elif not os.path.isfile(os.path.join(d, tmx)):
            err(f"map '{mid}': tmx file '{tmx}' not found next to the pack.")
        else:
            try:
                content = open(os.path.join(d, tmx), encoding="utf-8").read()
                if 'name="ground"' not in content:
                    warn(f"map '{mid}': tmx has no layer named 'ground' — the room will have no floor/collision.")
                if "world_tile_ground" not in content:
                    warn(f"map '{mid}': tmx doesn't reference the world_tile_ground tileset.")
                import re as _re
                layers = _re.findall(r'<layer[^>]*name="([^"]+)"', content)
                if not [l for l in layers if l not in ("ground", "structure")]:
                    warn(f"map '{mid}': tmx has only data layers (ground/structure) — missing visible layer; the mod will "
                         "auto-generate a plain cave floor (cf_autofloor). Add your own layer for real art.")
            except OSError:
                pass
        ed = m.get("exitDirection")
        if ed is not None and ed not in (0, 1, 2, 3):
            warn(f"map '{mid}': exitDirection {ed} should be 0..3 (0=E 1=S 2=W 3=N).")
        al = m.get("ambientLight")
        if al is not None and (not isinstance(al, list) or len(al) != 3 or any(not isinstance(v, (int, float)) or v < 0 or v > 1 for v in al)):
            err(f"map '{mid}': ambientLight must be [r, g, b] with each value 0..1.")
        map_ids = {mm.get("id") for mm in (pack.get("maps") or []) if isinstance(mm, dict)}
        for door in (m.get("doors") or []):
            if not isinstance(door, dict):
                continue
            if door.get("edge") == (ed if ed is not None else 1):
                err(f"map '{mid}': a door sits on the same edge as exitDirection — the game would treat it "
                    "as the overworld exit. Move the door or change exitDirection.")
            tgt = door.get("target")
            if not tgt:
                err(f"map '{mid}': a door has no 'target'.")
            elif tgt not in map_ids:
                warn(f"map '{mid}': door target '{tgt}' is not a map in this pack (cross-pack needs a dependency).")

    for s in (pack.get("shops") or []):
        if not isinstance(s, dict):
            continue
        sid = s.get("id")
        if not sid:
            err("a shop entry has no 'id'.")
        else:
            chk_ns(sid, "shop")
        if not s.get("items"):
            err(f"shop '{sid}' has no 'items'.")
        else:
            for iid in s["items"]:
                it = next((i for i in items if isinstance(i, dict) and i.get("id") == iid), None)
                if it is not None and it.get("price") is None:
                    warn(f"shop '{sid}': item '{iid}' has no 'price' — it can't be bought.")

    particle_ids = {p.get("id") for p in (pack.get("particles") or []) if isinstance(p, dict) and p.get("id")}
    for p in (pack.get("particles") or []):
        if not isinstance(p, dict):
            continue
        pid = p.get("id")
        if not pid:
            err("a particle entry has no 'id'.")
        else:
            chk_ns(pid, "particle")
        if not p.get("texture") and not p.get("vanillaSheet"):
            err(f"particle '{pid}' needs 'texture' (a PNG) or 'vanillaSheet'.")
        elif p.get("texture") and not os.path.isfile(os.path.join(d, p["texture"])):
            err(f"particle '{pid}': texture '{p['texture']}' not found next to the pack.")

    for e in (pack.get("emitters") or []):
        if not isinstance(e, dict):
            continue
        eid = e.get("id")
        if not eid:
            err("an emitter entry has no 'id'.")
        else:
            chk_ns(eid, "emitter")
        if not e.get("particles"):
            err(f"emitter '{eid}' has no 'particles'.")
        sf = e.get("spawnFrequency")
        if sf is not None and 0 < sf < 0.01:
            warn(f"emitter '{eid}': spawnFrequency {sf} spawns >100/sec — the particle budget is shared (50k).")

    for c in (pack.get("crops") or []):
        if not isinstance(c, dict):
            continue
        cid = c.get("id")
        if not cid:
            err("a crop entry has no 'id'.")
        else:
            chk_ns(cid, "crop")
        disp = c.get("display") or {}
        stages = disp.get("stages") or []
        sc = c.get("stageCount")
        if not sc or sc < 2:
            err(f"crop '{cid}': stageCount must be >= 2.")
        elif len(stages) != sc:
            err(f"crop '{cid}': display.stages has {len(stages)} entries but stageCount is {sc} — "
                "they MUST match (the game indexes stages by growth stage; a mismatch crashes the client).")
        if not disp.get("sheet") and not disp.get("file"):
            err(f"crop '{cid}': display needs 'sheet' (vanilla path) or 'file' (a PNG).")
        elif disp.get("file") and not os.path.isfile(os.path.join(d, disp["file"])):
            err(f"crop '{cid}': display file '{disp['file']}' not found next to the pack.")
        seed = c.get("seed") or {}
        if not seed.get("id"):
            err(f"crop '{cid}': needs a 'seed' block with an 'id' (the plantable item).")
        if not c.get("drops"):
            warn(f"crop '{cid}': no 'drops' — harvesting will yield nothing.")

    pack_map_ids = {mm.get("id") for mm in (pack.get("maps") or []) if isinstance(mm, dict)}
    for n in (pack.get("npcs") or []):
        if not isinstance(n, dict):
            continue
        nid = n.get("id")
        if not nid:
            err("an npc entry has no 'id'.")
        else:
            chk_ns(nid, "npc")
        if not n.get("map"):
            err(f"npc '{nid}' has no 'map'.")
        elif n["map"] not in pack_map_ids:
            err(f"npc '{nid}': map '{n['map']}' is not a map in this pack.")
        if not n.get("position") or len(n.get("position") or []) < 2:
            err(f"npc '{nid}' needs 'position' [x, y].")
        for shk, shv in (n.get("sheets") or {}).items():
            if shk.lower() != "no_weapons":
                warn(f"npc '{nid}': sheets key '{shk}' — the satyr body's only sheet is 'no_weapons' (48x48).")
            if shv and not os.path.isfile(os.path.join(d, shv)):
                err(f"npc '{nid}': sheet '{shv}' not found next to the pack.")
        tint = n.get("tint")
        if tint is not None and (not isinstance(tint, list) or len(tint) < 3 or any(not isinstance(v, (int, float)) or v < 0 or v > 255 for v in tint)):
            err(f"npc '{nid}': tint must be [r,g,b] 0-255.")
        yarn = n.get("yarn")
        if not yarn:
            err(f"npc '{nid}' has no 'yarn' file.")
        elif not os.path.isfile(os.path.join(d, yarn)):
            err(f"npc '{nid}': yarn file '{yarn}' not found next to the pack.")
        else:
            try:
                ytext = open(os.path.join(d, yarn), encoding="utf-8").read()
                node = n.get("startNode") or "Start"
                if f"title: {node}" not in ytext and f"title:{node}" not in ytext:
                    warn(f"npc '{nid}': yarn has no node titled '{node}'.")
            except OSError:
                pass

    for sk in (pack.get("skills") or []):
        if not isinstance(sk, dict):
            continue
        skid = sk.get("id")
        if not skid:
            err("a skill entry has no 'id'.")
        elif not skid.startswith("skill:"):
            warn(f"skill '{skid}': ids conventionally start with 'skill:' (e.g. skill:{ns}_runes).")

    ps = pack.get("playerSkins")
    if isinstance(ps, dict):
        part_names = set()
        for kind in ("reskins", "hair", "parts"):
            for e in (ps.get(kind) or []):
                if not isinstance(e, dict):
                    continue
                who = e.get("part") or e.get("name") or "(unnamed)"
                if kind != "reskins" and not e.get("name"):
                    err(f"playerSkins.{kind}: an entry has no 'name'.")
                if kind == "reskins" and (not e.get("tag") or not e.get("part")):
                    err("playerSkins.reskins: entries need 'tag' and 'part'.")
                for sk in ("sheet", "sheet2"):
                    f = e.get(sk)
                    if not f:
                        err(f"playerSkins.{kind} '{who}': missing '{sk}' (both facings are required).")
                    elif not os.path.isfile(os.path.join(d, f)):
                        err(f"playerSkins.{kind} '{who}': {sk} '{f}' not found next to the pack.")
                if kind == "parts" and e.get("name"):
                    part_names.add(e["name"])
        for lk in (ps.get("looks") or []):
            if not isinstance(lk, dict):
                continue
            lid = lk.get("id")
            if not lid:
                err("playerSkins.looks: an entry has no 'id'."); continue
            if not lid.startswith("cdd:"):
                warn(f"playerSkins look '{lid}': ids conventionally start with 'cdd:' (items reference it via equippable.displayId).")
            if not lk.get("fragments"):
                err(f"playerSkins look '{lid}' has no 'fragments'.")
            for fr in (lk.get("fragments") or []):
                if isinstance(fr, dict) and not (fr.get("tag") and fr.get("part")):
                    err(f"playerSkins look '{lid}': fragments need 'tag' and 'part'.")

    for k, f in (pack.get("sounds") or {}).items():
        if not f or not os.path.isfile(os.path.join(d, f)):
            err(f"sound '{k}': file '{f}' not found next to the pack.")
        elif not f.lower().rsplit('.', 1)[-1] in ("wav", "ogg", "mp3", "flac"):
            warn(f"sound '{k}': '{f}' — supported formats are wav/ogg/mp3/flac.")

    fav_ids = set()
    for fv in (pack.get("favours") or []):
        if not isinstance(fv, dict):
            continue
        fid = fv.get("id")
        if not fid:
            err("a favour entry has no 'id'."); continue
        chk_ns(fid, "favour")
        fav_ids.add(fid)
        if fv.get("screen"):
            # custom-screen node: parent optional (root), but must be another screen node when set
            par = fv.get("parent")
            if par:
                pdef = next((o for o in pack.get("favours") or [] if isinstance(o, dict) and o.get("id") == par), None)
                if pdef is None or not pdef.get("screen"):
                    warn(f"favour '{fid}': screen parent '{par}' is not a screen:true favour in this pack — the node becomes a root in game.")
        elif not fv.get("parent"):
            err(f"favour '{fid}' has no 'parent' (the node to attach under).")
        pos = fv.get("position")
        if not isinstance(pos, list) or len(pos) < 2:
            err(f"favour '{fid}' needs 'position' [column, row].")
        effects = [k for k in ("statBonuses", "aura", "recipe", "flagId") if fv.get(k)]
        if len(effects) > 1:
            warn(f"favour '{fid}': more than one effect ({', '.join(effects)}) — pick ONE.")
        if not effects:
            warn(f"favour '{fid}': no effect set (statBonuses/aura/recipe/flagId) — a cosmetic node.")
        for key in (fv.get("statBonuses") or {}):
            if key.lower() not in STAT_KEYS:
                warn(f"favour '{fid}': unknown stat key '{key}'.")

    for it2 in items:
        w2 = ((it2 or {}).get("equippable") or {}).get("weapon") or {}
        arc = w2.get("attackArcDegrees")
        if arc is not None and not (0 <= arc <= 360):
            warn(f"item '{it2.get('id')}': attackArcDegrees {arc} outside 0..360 (vanilla=144; >360 untested).")
        if arc and (w2.get("class") or "").lower() == "spear":
            warn(f"item '{it2.get('id')}': attackArcDegrees has no effect on spears (they stab in a line).")

    for mm in (pack.get("maps") or []):
        if not isinstance(mm, dict):
            continue
        for dd in (mm.get("doodads") or []):
            if not isinstance(dd, dict):
                continue
            if not dd.get("entity") and not dd.get("sprite"):
                err(f"map '{mm.get('id')}': a doodad needs 'entity' or 'sprite'.")
            if not isinstance(dd.get("position"), list) or len(dd.get("position") or []) < 2:
                err(f"map '{mm.get('id')}': a doodad needs 'position' [x, y].")
            sp = dd.get("sprite")
            if sp and not os.path.isfile(os.path.join(d, sp)):
                err(f"map '{mm.get('id')}': doodad sprite '{sp}' not found next to the pack.")

    arena_seen = set()
    for a in (pack.get("arenas") or []):
        if not isinstance(a, dict):
            continue
        aid = a.get("id")
        if not aid:
            err("an arena entry has no 'id'."); continue
        chk_ns(aid, "arena")
        amap = a.get("map")
        if not amap:
            err(f"arena '{aid}' has no 'map'.")
        else:
            m = next((mm for mm in (pack.get("maps") or []) if isinstance(mm, dict) and mm.get("id") == amap), None)
            if m is None:
                err(f"arena '{aid}': map '{amap}' is not a map in this pack.")
            elif not m.get("pvp"):
                err(f"arena '{aid}': map '{amap}' is not flagged \"pvp\": true — the arena would be dead.")
            if amap in arena_seen:
                err(f"arena '{aid}': map '{amap}' already has an arena (one arena per map).")
            arena_seen.add(amap)
        rw = a.get("winnerReward")
        if rw and rw not in item_ids:
            warn(f"arena '{aid}': winnerReward '{rw}' is not an item in this pack (ok if vanilla/other-pack).")

    EVENT_TYPES = {"wavedefense", "bossrush", "huntcontest", "scheduledraid", "teambattle", "koth", "race", "werewolf"}
    for ev in (pack.get("events") or []):
        if not isinstance(ev, dict):
            continue
        evid = ev.get("id")
        if not evid:
            err("an event entry has no 'id'."); continue
        chk_ns(evid, "event")
        et = (ev.get("type") or "").lower()
        if not et:
            err(f"event '{evid}' has no 'type' (waveDefense/bossRush/huntContest/scheduledRaid/teamBattle/koth/race).")
        elif et not in EVENT_TYPES:
            err(f"event '{evid}': unknown type '{ev.get('type')}'.")
        if et in ("wavedefense", "bossrush", "teambattle", "koth", "race", "werewolf"):
            if not ev.get("map"):
                err(f"event '{evid}': type '{ev.get('type')}' needs a 'map' (a map in this pack).")
            elif ev["map"] not in pack_map_ids:
                err(f"event '{evid}': map '{ev['map']}' is not a map in this pack.")
        if et == "wavedefense" and not ev.get("waves"):
            err(f"event '{evid}': waveDefense needs 'waves'.")
        if et == "bossrush" and not ev.get("bosses"):
            err(f"event '{evid}': bossRush needs 'bosses'.")
        if et == "scheduledraid" and not ev.get("raidData"):
            err(f"event '{evid}': scheduledRaid needs 'raidData' (e.g. raid:rats).")
        if et == "koth" and (not isinstance(ev.get("point"), list) or len(ev.get("point") or []) < 2):
            err(f"event '{evid}': koth needs 'point' [x, y] (the hill center in map px).")
        if et == "werewolf":
            gp = ev.get("ghostPen")
            if not isinstance(gp, list) or len(gp) < 2:
                err(f"event '{evid}': werewolf needs 'ghostPen' [x, y] (the walled spectator pen in map px).")
            if (ev.get("minPlayers") or 1) < 2:
                warn(f"event '{evid}': werewolf with minPlayers < 2 can never produce a real match (4+ recommended; 2 = debug).")
        if et == "race":
            cps = ev.get("checkpoints")
            if not isinstance(cps, list) or len(cps) < 2:
                err(f"event '{evid}': race needs 'checkpoints' (at least two [x, y] points).")
            elif any(not isinstance(p, list) or len(p) < 2 for p in cps):
                err(f"event '{evid}': every checkpoint must be an [x, y] pair.")

    for sa in (pack.get("shopAdditions") or []):
        if not isinstance(sa, dict):
            continue
        if not sa.get("shop"):
            err("a shopAdditions entry has no 'shop' (an existing shop id like 'all:1').")
        if not sa.get("items"):
            err(f"shopAdditions for '{sa.get('shop')}': no 'items'.")

    for tb in (pack.get("townBuildings") or []):
        if not isinstance(tb, dict):
            continue
        tbid = tb.get("id")
        if not tbid:
            err("a townBuilding entry has no 'id'."); continue
        chk_ns(tbid, "townBuilding")
        if not tb.get("base"):
            err(f"townBuilding '{tbid}' has no 'base' (a vanilla BuildingData id like 'building:bakery:0').")
        elif not str(tb["base"]).startswith("building:"):
            warn(f"townBuilding '{tbid}': base '{tb['base']}' doesn't look like a 'building:...' id.")

    for wr in (pack.get("workRecipes") or []):
        if not isinstance(wr, dict):
            continue
        wrid = wr.get("id")
        if not wrid:
            err("a workRecipe entry has no 'id'."); continue
        if not wr.get("job"):
            err(f"workRecipe '{wrid}' has no 'job' (e.g. 'job:baker').")
        if not wr.get("output"):
            err(f"workRecipe '{wrid}' has no 'output' (the crafted item id).")
        lv = wr.get("level")
        if lv is not None and not (isinstance(lv, int) and 0 <= lv <= 4):
            warn(f"workRecipe '{wrid}': level {lv} is outside the job-level range 0..4.")

    for sp2 in (pack.get("spacs") or []):
        if not isinstance(sp2, dict):
            continue
        spid = sp2.get("id")
        if not spid:
            err("a spac entry has no 'id'."); continue
        chk_ns(spid, "spac")
        if not sp2.get("base"):
            # from-scratch spac: needs own sheets + frame grid + animations
            if not sp2.get("sheets"):
                err(f"spac '{spid}': no 'base' and no 'sheets' — a from-scratch spac needs its own PNGs.")
            if not sp2.get("animations"):
                err(f"spac '{spid}': a from-scratch spac needs 'animations' (name -> frames/timing).")
            if not sp2.get("frameWidth") or not sp2.get("frameHeight"):
                warn(f"spac '{spid}': from-scratch without frameWidth/frameHeight — the whole PNG becomes ONE frame.")
            if sp2.get("directions") not in (None, 1, 4, 8):
                err(f"spac '{spid}': directions must be 1, 4 or 8.")
        for shk, shv in (sp2.get("sheets") or {}).items():
            if shv and not os.path.isfile(os.path.join(d, shv)):
                err(f"spac '{spid}': sheet '{shk}' file '{shv}' not found next to the pack.")

    # declared dependencies present? (only checkable across a folder — see validate_folder)
    return (errors, warnings)


def validate_folder(target):
    """Validate a file or every *.json in a folder. Returns a human-readable report string + total error count."""
    files = []
    if os.path.isfile(target):
        files = [target]
    elif os.path.isdir(target):
        files = [os.path.join(target, f) for f in sorted(os.listdir(target)) if f.endswith(".json")]
    else:
        return (f"Not found: {target}", 1)
    if not files:
        return ("No .json packs found.", 0)

    # cross-pack: ids present (for dependency checks)
    present = set()
    for f in files:
        try:
            p = json.load(open(f, encoding="utf-8"))
            present.add((p.get("id") or os.path.splitext(os.path.basename(f))[0]))
        except Exception:
            pass

    lines, total_err, total_warn = [], 0, 0
    for f in files:
        name = os.path.basename(f)
        errs, warns = validate_pack(f)
        # dependency check
        try:
            p = json.load(open(f, encoding="utf-8"))
            for dep in (p.get("dependencies") or []):
                if dep not in present:
                    warns.append(f"declares dependency '{dep}' which isn't installed here.")
        except Exception:
            pass
        lines.append(f"== {name} ==")
        for e in errs:
            lines.append(f"  ERROR: {e}")
        for w in warns:
            lines.append(f"  warn:  {w}")
        total_err += len(errs); total_warn += len(warns)
    lines.append("")
    lines.append(f"Done: {len(files)} pack(s), {total_err} error(s), {total_warn} warning(s).")
    return ("\n".join(lines), total_err)


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
    report, errors = validate_folder(target)
    print(report)
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
