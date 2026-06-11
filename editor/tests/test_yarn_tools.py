"""Unit tests for yarn_tools (pure Python — no Qt): node split, auto-tag idempotence, lint findings,
csv round-trip (commas, diacritics, quoting), original extraction + speaker-prefix prefill data."""
import os

from conftest import ROOT_DIR

import yarn_tools as yt

HERMIT_YARN = os.path.join(ROOT_DIR, "examples", "test", "hermit.yarn")
HERMIT_CSV = os.path.join(ROOT_DIR, "examples", "test", "hermit.tr_TR.csv")
SAGE_YARN = os.path.join(ROOT_DIR, "examples", "worldoftales", "sage.yarn")

TWO_NODES = """title: Start
---
hero: First line. #line:x_01
-> An option #line:x_opt
    hero: Reply. #line:x_02
===

title: Second
---
hero: Other node. #line:x_03
===
"""

UNTAGGED = """title: Start
---
<<Actor tomb>>
tomb: A voice echoes from the stone...
-> Bless me, old stone. (heal)
    <<ForgeHeal 15>>
    tomb: The stone hums warmly.
-> Remain silent.
    tomb: ...
===
"""


# ---- node split ----------------------------------------------------------------------------------
def test_parse_nodes_two_nodes():
    nodes, problems = yt.parse_nodes(TWO_NODES)
    assert problems == []
    assert [n["title"] for n in nodes] == ["Start", "Second"]
    assert len(nodes[0]["body"]) == 3
    assert len(nodes[1]["body"]) == 1


def test_parse_nodes_real_examples():
    for p in (HERMIT_YARN, SAGE_YARN):
        nodes, problems = yt.parse_nodes(open(p, encoding="utf-8").read())
        assert problems == []
        assert nodes[0]["title"] == "Start"


def test_parse_nodes_structural_problems():
    _, p1 = yt.parse_nodes("title: A\n---\nhero: hi\n")          # missing ===
    assert any("===" in m for m in p1)
    _, p2 = yt.parse_nodes("---\nhero: hi\n===\n")               # missing title
    assert any("title" in m for m in p2)
    _, p3 = yt.parse_nodes("title: A\nhero: hi\n")               # header never closed
    assert any("---" in m for m in p3)


# ---- auto-tag ------------------------------------------------------------------------------------
def test_auto_tag_tags_all_translatable_lines():
    tagged, added = yt.auto_tag(UNTAGGED, "tomb")
    assert added == 5            # 3 speaker lines + 2 options
    lines = tagged.split("\n")
    assert "#line:tomb_01" in lines[3]                  # speaker line
    assert any("-> Bless me" in l and "#line:" in l for l in lines)
    assert all("#line:" not in l for l in lines if "<<ForgeHeal" in l)   # command-only: never tagged
    assert "#line:" not in lines[0] and "#line:" not in lines[1]         # title/--- never tagged


def test_auto_tag_idempotent():
    once, n1 = yt.auto_tag(UNTAGGED, "tomb")
    twice, n2 = yt.auto_tag(once, "tomb")
    assert n1 == 5 and n2 == 0 and once == twice


def test_auto_tag_skips_existing_and_avoids_collisions():
    text = "title: T\n---\nhero: tagged. #line:slug_01\nhero: untagged.\n===\n"
    out, added = yt.auto_tag(text, "slug")
    assert added == 1
    assert "#line:slug_02" in out          # slug_01 taken -> next free number
    assert out.count("#line:slug_01") == 1


def test_auto_tag_real_hermit_already_tagged():
    text = open(HERMIT_YARN, encoding="utf-8").read()
    _, added = yt.auto_tag(text, "hermit")
    assert added == 0


# ---- lint ----------------------------------------------------------------------------------------
BAD_YARN = """title: Start
---
<<Actor ghost>>
known: Hello there. #line:b_01
stranger: Who am I? #line:b_02
known: untagged line here
<<ForgeGiveItem "item:x">>
<<ForgeTakeItem "item:x" 1>>
<<ForgeHeal 10 20>>
"""


def test_lint_findings_on_bad_yarn():
    findings = yt.lint(BAD_YARN, {"known": "Known One"})
    msgs = [m for _, m in findings]
    errors = [m for lvl, m in findings if lvl == "error"]
    warns = [m for lvl, m in findings if lvl == "warn"]
    assert any("===" in m for m in errors)                              # unterminated node
    assert any("<<Actor ghost>>" in m for m in warns)                   # unknown <<Actor>>
    assert any("'stranger'" in m for m in warns)                        # unknown speaker prefix
    assert any("no #line: tag" in m for m in warns)                     # untagged translatable line
    assert any("ForgeGiveItem" in m and "got 1" in m for m in errors)   # arity 1 != 2
    assert any("ForgeHeal" in m and "got 2" in m for m in errors)       # arity 2 != 1
    assert not any("ForgeTakeItem" in m for m in msgs)                  # correct arity passes


def test_lint_clean_on_real_examples():
    hermit = open(HERMIT_YARN, encoding="utf-8").read()
    findings = yt.lint(hermit, {"cftest_hermit": "Test Hermit"})
    assert findings == []


# ---- csv round-trip ------------------------------------------------------------------------------
def test_csv_roundtrip_commas_diacritics_quotes(tmp_path):
    rows = {
        "line:a_01": "hero: Bir, iki, üç — virgüller güvenli.",
        "line:a_02": "hero: Şu ğüşıöç diakritikler de.",
        "line:a_opt": "Elveda, dostum.",
    }
    p = tmp_path / "a.tr_TR.csv"
    yt.write_translation_csv(str(p), rows)
    raw = p.read_bytes()
    assert not raw.startswith(b"\xef\xbb\xbf")           # UTF-8, no BOM
    assert yt.read_translation_csv(str(p)) == rows


def test_csv_parse_quoted_header_tabs():
    text = 'id,text\nline:q_01,"He said ""go"", twice"\nline:t_01\thero: tab separated\n\n'
    out = yt.parse_translation_csv(text)
    assert out == {"line:q_01": 'He said "go", twice',
                   "line:t_01": "hero: tab separated"}    # header skipped, quotes unescaped


def test_csv_reads_real_hermit_sidecar():
    out = yt.read_translation_csv(HERMIT_CSV)
    assert out["line:hermit_01"].startswith("cftest_hermit: Ah... bir ziyaret")
    assert out["line:hermit_opt_who"] == "Sen kimsin?"    # options keep NO prefix
    assert len(out) == 16


# ---- extraction / prefix prefill -----------------------------------------------------------------
def test_extract_lines_hermit_matches_csv_conventions():
    text = open(HERMIT_YARN, encoding="utf-8").read()
    rows = {r["id"]: r for r in yt.extract_lines(text)}
    assert len(rows) == 16
    # speaker lines keep the prefix in the original AND expose it for prefilling
    assert rows["line:hermit_01"]["text"].startswith("cftest_hermit: Ah... a visitor.")
    assert rows["line:hermit_01"]["prefix"] == "cftest_hermit: "
    # options: no prefix, no '->', inline <<conditions>> stripped
    assert rows["line:hermit_opt_who"] == {"id": "line:hermit_opt_who", "text": "Who are you?", "prefix": ""}
    assert rows["line:hermit_opt_seek"]["text"] == "I was sent to seek you."
    assert "<<" not in rows["line:hermit_opt_seek"]["text"]


def test_yarn_template_uses_first_actor():
    t = yt.yarn_template({"my_actor": "My Actor"})
    assert "title: Start" in t and "my_actor: " in t and t.rstrip().endswith("===")
    nodes, problems = yt.parse_nodes(t)
    assert problems == [] and nodes[0]["title"] == "Start"
