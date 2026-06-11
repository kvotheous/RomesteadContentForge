"""Headless (QT_QPA_PLATFORM=offscreen) editor smoke tests: every section form builds, and a real
pack survives load -> render-everything -> save with byte-identical semantics."""
import json
import os

import pytest

from conftest import ROOT_DIR

fe = pytest.importorskip("forge_editor")

TEST_PACK = os.path.join(ROOT_DIR, "examples", "test", "test.json")
TALES_PACK = os.path.join(ROOT_DIR, "examples", "worldoftales", "worldoftales.json")


@pytest.fixture(scope="module")
def app():
    a = fe.QApplication.instance() or fe.QApplication([])
    a.setStyleSheet(fe.QSS)
    return a


def visit_everything(ed):
    """Select every section (group headers are skipped automatically) and every entry within it."""
    for i in range(ed.sections.count()):
        if ed.sections.item(i).data(fe.Qt.UserRole) is None:
            continue  # group header
        ed.sections.setCurrentRow(i)
        for j in range(ed.entries.count()):
            ed.entries.setCurrentRow(j)


def load(ed, path):
    ed.pack = json.load(open(path, encoding="utf-8"))
    ed.path = path
    ed._select_first_section()
    ed.on_section(ed.sections.currentRow())


def test_editor_constructs(app):
    ed = fe.Editor()
    assert ed.sections.count() > len(fe.SCHEMA["sections"])  # sections + group headers + manifest


def test_all_section_forms_build_on_empty_pack(app):
    ed = fe.Editor()
    visit_everything(ed)


def test_all_section_forms_build_with_a_blank_entry(app):
    ed = fe.Editor()
    for i in range(ed.sections.count()):
        key = ed.sections.item(i).data(fe.Qt.UserRole)
        if key in (None, "_manifest"):
            continue
        sec = fe.SCHEMA["sections"][key]
        ed.sections.setCurrentRow(i)
        if not (sec.get("singleton") or sec.get("dict")):
            ed.add_entry()  # renders the full form with empty data


def test_all_sections_render_with_real_pack(app):
    ed = fe.Editor()
    load(ed, TEST_PACK)
    visit_everything(ed)


def test_roundtrip_test_pack(app, tmp_path):
    original = json.load(open(TEST_PACK, encoding="utf-8"))
    ed = fe.Editor()
    load(ed, TEST_PACK)
    visit_everything(ed)               # rendering must NOT mutate the pack
    out = tmp_path / "out.json"
    ed.path = str(out)
    ed.save()
    saved = json.loads(out.read_text(encoding="utf-8"))
    assert saved == original


def test_roundtrip_worldoftales_placeables_alias(app, tmp_path):
    original = json.load(open(TALES_PACK, encoding="utf-8"))
    ed = fe.Editor()
    load(ed, TALES_PACK)
    # the pack uses the canonical 'placeables' key; the editor's 'furniture' section must surface it
    for i in range(ed.sections.count()):
        if ed.sections.item(i).data(fe.Qt.UserRole) == "furniture":
            ed.sections.setCurrentRow(i)
            break
    assert ed.entries.count() == len(original["placeables"])
    visit_everything(ed)
    out = tmp_path / "out.json"
    ed.path = str(out)
    ed.save()
    saved = json.loads(out.read_text(encoding="utf-8"))
    assert saved == original


def test_validate_results_panel_populates(app):
    ed = fe.Editor()
    ed.pack = {"id": "broken", "items": [{"name": "no id"}]}
    ed.validate()
    assert ed.results.isVisible() or ed.results.count() > 0
    texts = [ed.results.item(i).text() for i in range(ed.results.count())]
    assert any("item with empty id" in t for t in texts)


def test_status_bar_reports_pack(app):
    ed = fe.Editor()
    load(ed, TEST_PACK)
    ed.update_status()
    assert "cf_testbench" in ed.status_lbl.text()


def test_source_values_merge_pack_sound_events(app):
    ed = fe.Editor()
    load(ed, TEST_PACK)
    vals = ed.source_values("soundEvents")
    assert "cf:sound:cftest:t49" in vals          # pack sounds dict key, prefixed
    assert any(v.startswith("event:/") for v in vals)  # the FMOD catalog
