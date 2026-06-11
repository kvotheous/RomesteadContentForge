"""schema.json: internal consistency + FULL parity with ContentPack.cs (the ground truth)."""
import json
import os
import re

import pytest

from conftest import EDITOR_DIR, ROOT_DIR

SCHEMA_PATH = os.path.join(EDITOR_DIR, "schema.json")
CONTENTPACK_CS = os.path.join(ROOT_DIR, "ContentPack.cs")

# Top-level ContentPack fields that intentionally have NO schema section of their own.
# (Everything is currently covered: placeables/structures are declared as ALIASES of the
# `furniture` section, which the parity test below verifies explicitly.)
JSON_ONLY_SECTIONS = set()

MANIFEST_FIELDS = {"id", "namespace", "author", "version", "description", "dependencies", "apiVersion"}


@pytest.fixture(scope="module")
def schema():
    with open(SCHEMA_PATH, encoding="utf-8") as fh:
        return json.load(fh)


def walk_fields(fields):
    """Yield every field dict, recursing into object fields, array items and dict value-objects."""
    for f in fields or []:
        yield f
        if f.get("fields"):
            yield from walk_fields(f["fields"])
        item = f.get("item")
        if isinstance(item, dict):
            if item.get("fields"):
                yield from walk_fields(item["fields"])
            if item.get("source"):
                yield item


def test_schema_parses(schema):
    assert isinstance(schema["sections"], dict) and schema["sections"]
    assert isinstance(schema["manifest"], list) and schema["manifest"]


def test_manifest_covers_contentpack_scalars(schema):
    keys = {f["key"] for f in schema["manifest"]}
    assert MANIFEST_FIELDS <= keys, f"manifest missing: {MANIFEST_FIELDS - keys}"


def test_every_list_section_has_valid_idfield(schema):
    for name, sec in schema["sections"].items():
        if sec.get("singleton") or sec.get("dict"):
            continue
        idf = sec.get("idField")
        assert isinstance(idf, str) and idf, f"section '{name}' has no idField"
        keys = {f["key"] for f in sec["fields"]}
        assert idf in keys, f"section '{name}': idField '{idf}' is not one of its fields"


def test_no_duplicate_field_keys(schema):
    def check(fields, where):
        seen = set()
        for f in fields or []:
            assert f["key"] not in seen, f"duplicate field key '{f['key']}' in {where}"
            seen.add(f["key"])
            if f.get("fields"):
                check(f["fields"], f"{where}.{f['key']}")
            item = f.get("item")
            if isinstance(item, dict) and item.get("fields"):
                check(item["fields"], f"{where}.{f['key']}[]")

    check(schema["manifest"], "manifest")
    for name, sec in schema["sections"].items():
        check(sec.get("fields"), name)


def test_every_source_reference_resolves(schema):
    sources = schema.get("sources", {})
    used = set()
    all_fields = list(schema["manifest"])
    for sec in schema["sections"].values():
        all_fields += sec.get("fields") or []
        if sec.get("keySource"):
            used.add(sec["keySource"])
    for f in walk_fields(all_fields):
        for key in ("source", "keySource"):
            if f.get(key):
                used.add(f[key])
    missing = used - set(sources)
    assert not missing, f"fields reference undefined sources: {missing}"


def test_source_files_and_pack_refs_exist(schema):
    for name, spec in schema.get("sources", {}).items():
        if "file" in spec:
            # the editor resolves relative to editor/ then the repo root (data_path)
            cands = [os.path.join(EDITOR_DIR, spec["file"]), os.path.join(ROOT_DIR, spec["file"])]
            assert any(os.path.isfile(c) for c in cands), f"source '{name}': file '{spec['file']}' not found"
        if "pack" in spec:
            assert spec["pack"] in schema["sections"], f"source '{name}': pack section '{spec['pack']}' undefined"
        if "packDict" in spec:
            sec = schema["sections"].get(spec["packDict"])
            assert sec and sec.get("dict"), f"source '{name}': packDict '{spec['packDict']}' is not a dict section"


def test_enum_fields_reference_defined_enums(schema):
    enums = schema["enums"]
    all_fields = list(schema["manifest"])
    for sec in schema["sections"].values():
        all_fields += sec.get("fields") or []
    for f in walk_fields(all_fields):
        if f.get("type") == "enum":
            assert f.get("enum") in enums or f.get("values"), \
                f"enum field '{f.get('key')}' references no defined enum and has no inline values"


# ---- CRITICAL: every ContentPack.cs top-level section appears in the schema ----
def parse_contentpack_sections():
    """Light regex parse of the ContentPack class body: returns (manifest_names, section_names) in camelCase."""
    src = open(CONTENTPACK_CS, encoding="utf-8").read()
    m = re.search(r"class ContentPack\b.*?\{(.*?)\n\}", src, re.S)
    assert m, "could not locate the ContentPack class body"
    body = m.group(1)
    manifest, sections = [], []
    for typ, name in re.findall(r"public\s+([\w<>,?\[\]\s]+?)\s+(\w+)\s*(?:=[^;]+)?;", body):
        camel = name[0].lower() + name[1:]
        if typ.strip().startswith(("List<", "Dictionary<", "PlayerSkinsDef")):
            (manifest if camel == "dependencies" else sections).append(camel)
        else:
            manifest.append(camel)
    return manifest, sections


def test_contentpack_parity(schema):
    manifest_names, section_names = parse_contentpack_sections()
    assert len(section_names) >= 25, f"ContentPack parse looks broken: only {section_names}"

    schema_manifest = {f["key"] for f in schema["manifest"]}
    for name in manifest_names:
        assert name in schema_manifest, f"ContentPack manifest field '{name}' missing from schema manifest"

    sections = schema["sections"]
    aliases = {a: k for k, sec in sections.items() for a in sec.get("aliases", [])}
    for name in section_names:
        if name in JSON_ONLY_SECTIONS:
            continue
        assert name in sections or name in aliases, \
            f"ContentPack section '{name}' is missing from schema.json (and is not a declared alias)"


def test_furniture_aliases_cover_placeables_and_structures(schema):
    assert set(schema["sections"]["furniture"].get("aliases", [])) == {"placeables", "structures"}


def test_groups_reference_real_sections_and_cover_everything(schema):
    grouped = []
    for grp in schema.get("groups", []):
        for k in grp["sections"]:
            assert k in schema["sections"], f"group '{grp['label']}' lists unknown section '{k}'"
            grouped.append(k)
    assert len(grouped) == len(set(grouped)), "a section appears in more than one group"
    ungrouped = set(schema["sections"]) - set(grouped)
    assert not ungrouped, f"sections missing from groups: {ungrouped}"
