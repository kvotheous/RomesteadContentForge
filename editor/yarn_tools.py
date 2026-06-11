"""
Yarn dialogue helpers for the Content Forge editor — pure Python, no Qt, fully unit-testable.

Mirrors the conventions the mod itself uses (Npcs.cs + PACKS.md NPC section):
- translatable lines are SPEAKER lines (`actorid: text`) and `->` options; command-only / structural
  lines are never translated,
- translations key off explicit `#line:` tags; the csv ids are the full tag text (`line:hermit_01`),
- translation csv rows split at the FIRST tab (else first comma); a header row whose id is `id` is
  skipped; double-quoted values with `""` escaping are tolerated; files are UTF-8 (no BOM),
- translated speaker lines must KEEP the `actorid: ` prefix; options have no prefix.
"""
import os
import re

TAG_RE = re.compile(r"#(line:\S+)")
SPEAKER_RE = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*):\s+\S")
CMD_RE = re.compile(r"<<[^>]*>>")
ACTOR_CMD_RE = re.compile(r"<<\s*Actor\s+([^\s>]+)")


# ---- structure ----------------------------------------------------------------------------------
def parse_nodes(text):
    """Split a yarn file into nodes. Returns (nodes, problems).

    nodes: [{'title': str|None, 'start': line-index of the header's first line,
             'body_start': line-index after '---' or None, 'end': line-index of '===' or last line,
             'body': [line-index, ...]}]
    problems: ['error message', ...] for structural breakage (missing title / --- / ===).
    """
    lines = text.split("\n")
    nodes, problems = [], []
    state = "between"      # between | header | body
    cur = None
    for i, raw in enumerate(lines):
        s = raw.strip()
        if state == "between":
            if not s or s.startswith("//"):
                continue
            cur = {"title": None, "start": i, "body_start": None, "end": None, "body": []}
            nodes.append(cur)
            state = "header"
            # fall through into header handling for this same line
        if state == "header":
            if s == "---":
                cur["body_start"] = i + 1
                state = "body"
                if cur["title"] is None:
                    problems.append(f"node starting at line {cur['start'] + 1} has no 'title:' header")
            elif s.startswith("title:"):
                cur["title"] = s[len("title:"):].strip()
            elif s == "===":
                problems.append(f"node starting at line {cur['start'] + 1} has '===' before any '---' body")
                cur["end"] = i
                state = "between"
            continue
        if state == "body":
            if s == "===":
                cur["end"] = i
                state = "between"
            else:
                cur["body"].append(i)
    if state == "header":
        problems.append(f"node '{cur['title'] or '(untitled)'}' never reaches '---' (header not closed)")
    elif state == "body":
        cur["end"] = len(lines) - 1
        problems.append(f"node '{cur['title'] or '(untitled)'}' is missing its closing '==='")
    return nodes, problems


def _strip_comment(line):
    idx = line.find("//")
    return line if idx < 0 else line[:idx]


def classify_line(line):
    """'speaker' | 'option' | None for one BODY line ('is this translatable, and how')."""
    code = _strip_comment(line)
    if code.lstrip().startswith("->"):
        return "option"
    bare = TAG_RE.sub("", CMD_RE.sub("", code)).strip()
    if not bare:
        return None                     # command-only / blank / structural
    if SPEAKER_RE.match(code):
        return "speaker"
    return None                         # plain untagged prose (not a Forge convention) — leave alone


def iter_translatable(text):
    """Yield (line_index, kind) for every translatable body line, in file order."""
    nodes, _ = parse_nodes(text)
    for n in nodes:
        for i in n["body"]:
            kind = classify_line(text.split("\n")[i])
            if kind:
                yield i, kind


# ---- auto-tag ------------------------------------------------------------------------------------
def auto_tag(text, slug):
    """Add `#line:<slug>_NN` to every translatable line missing a tag. Idempotent.
    Returns (new_text, added_count)."""
    lines = text.split("\n")
    existing = {m.group(1) for m in TAG_RE.finditer(text)}
    n, added = 1, 0
    for i, kind in list(iter_translatable(text)):
        if TAG_RE.search(lines[i]):
            continue
        while f"line:{slug}_{n:02d}" in existing:
            n += 1
        tag = f"line:{slug}_{n:02d}"
        existing.add(tag)
        lines[i] = lines[i].rstrip() + f" #{tag}"
        added += 1
    return "\n".join(lines), added


# ---- extraction (originals for the translation grid / csv) ---------------------------------------
def extract_lines(text):
    """Ordered [{'id', 'text', 'prefix'}] for every TAGGED translatable line.
    'text' is what a translation csv row should correspond to (speaker prefix KEPT for speaker
    lines; options without the '->', conditions or tag). 'prefix' is 'actorid: ' or ''."""
    lines = text.split("\n")
    out = []
    for i, kind in iter_translatable(text):
        m = TAG_RE.search(lines[i])
        if not m:
            continue
        code = TAG_RE.sub("", _strip_comment(lines[i]))
        if kind == "option":
            body = CMD_RE.sub("", code).strip()
            body = body[2:].strip() if body.startswith("->") else body
            out.append({"id": m.group(1), "text": body, "prefix": ""})
        else:
            sm = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$", code)
            actor, rest = sm.group(1), sm.group(2).strip()
            out.append({"id": m.group(1), "text": f"{actor}: {rest}", "prefix": f"{actor}: "})
    return out


# ---- lint ----------------------------------------------------------------------------------------
def _cmd_args(inner):
    """Split a <<...>> command's interior into args, double-quoted strings counting as one."""
    return re.findall(r'"(?:[^"\\]|\\.)*"|\S+', inner)


def lint(text, actors):
    """Structural + convention checks. Returns [('error'|'warn', message), ...]. `actors` is the
    pack entry's <<Actor key>> -> display-name map (may be empty)."""
    findings = []
    nodes, problems = parse_nodes(text)
    for p in problems:
        findings.append(("error", p))
    lines = text.split("\n")
    body_idx = {i for n in nodes for i in n["body"]}
    known = set(actors or {})
    for i in sorted(body_idx):
        code = _strip_comment(lines[i])
        for m in ACTOR_CMD_RE.finditer(code):
            if m.group(1) not in known:
                findings.append(("warn", f"line {i + 1}: <<Actor {m.group(1)}>> id is not in this entry's 'actors' map"))
        kind = classify_line(lines[i])
        if kind == "speaker":
            actor = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*):", code).group(1)
            if actor not in known:
                findings.append(("warn", f"line {i + 1}: speaker '{actor}' is not in this entry's 'actors' map (no name plate)"))
        if kind and not TAG_RE.search(lines[i]):
            findings.append(("warn", f"line {i + 1}: translatable line has no #line: tag (Auto-tag adds one)"))
        for m in CMD_RE.finditer(code):
            args = _cmd_args(m.group(0)[2:-2].strip())
            if not args:
                continue
            cmd, argc = args[0], len(args) - 1
            if cmd in ("ForgeGiveItem", "ForgeTakeItem") and argc != 2:
                findings.append(("error", f"line {i + 1}: <<{cmd}>> needs exactly 2 arguments (\"item:id\" amount), got {argc}"))
            elif cmd == "ForgeHeal" and argc != 1:
                findings.append(("error", f"line {i + 1}: <<ForgeHeal>> needs exactly 1 argument (amount), got {argc}"))
    return findings


# ---- translation csv (mirrors Npcs.cs ParseTranslationFile) ---------------------------------------
def parse_translation_csv(text):
    """csv text -> ordered {line-id: translated text}. First-tab-else-first-comma split, header 'id'
    row skipped, double-quoted values (with \"\" escaping) unquoted, ids/values trimmed."""
    out = {}
    for raw in text.splitlines():
        if not raw.strip():
            continue
        sep = raw.find("\t")
        if sep < 0:
            sep = raw.find(",")
        if sep <= 0:
            continue
        lid, val = raw[:sep].strip(), raw[sep + 1:].strip()
        if len(val) >= 2 and val[0] == '"' and val[-1] == '"':
            val = val[1:-1].replace('""', '"')
        if not lid or lid == "id":
            continue
        out[lid] = val
    return out


def read_translation_csv(path):
    if not os.path.isfile(path):
        return {}
    with open(path, encoding="utf-8-sig") as f:   # tolerate a BOM some spreadsheet tools add
        return parse_translation_csv(f.read())


def write_translation_csv(path, rows):
    """rows: ordered {line-id: text}. Written raw (`id,text`, first-comma split keeps inner commas
    safe), UTF-8 WITHOUT BOM, \\n newlines — the exact shape the mod reads."""
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for lid, val in rows.items():
            f.write(f"{lid},{val}\n")


def yarn_template(actors):
    """Minimal starter yarn: Start node + one speaker line with the entry's first actor."""
    actor = next(iter(actors or {}), "speaker")
    return f"title: Start\n---\n<<Actor {actor}>>\n{actor}: Hello, traveler.\n===\n"
