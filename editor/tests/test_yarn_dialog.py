"""Headless (offscreen) tests for the two editor features: the LocalizedEditor language table and
the YarnDialog dialogue editor (construction, grid, prefix prefill, auto-tag, lint, save)."""
import json
import os
import shutil

import pytest

from conftest import ROOT_DIR

fe = pytest.importorskip("forge_editor")
import yarn_tools as yt

TEST_PACK = os.path.join(ROOT_DIR, "examples", "test", "test.json")
EX_TEST = os.path.join(ROOT_DIR, "examples", "test")


@pytest.fixture(scope="module")
def app():
    a = fe.QApplication.instance() or fe.QApplication([])
    a.setStyleSheet(fe.QSS)
    return a


@pytest.fixture()
def editor(app):
    return fe.Editor()


LOC_FIELD = {"key": "nameLocalized", "type": "dict", "valueType": "string", "keySource": "languages"}


# ---- LocalizedEditor -----------------------------------------------------------------------------
def test_localized_fields_get_the_localized_widget(editor):
    data = {"nameLocalized": {"tr_TR": "Alev Kılıcı", "de_DE": "Flammenklinge"}}
    w = fe.make_editor(dict(LOC_FIELD), data, lambda: None, editor)
    assert isinstance(w, fe.LocalizedEditor)
    # the 'localized' sugar type routes to the same widget
    w2 = fe.make_editor({"key": "x", "type": "localized"}, {}, lambda: None, editor)
    assert isinstance(w2, fe.LocalizedEditor)


def test_localized_combo_excludes_en_but_is_free_typeable(editor):
    w = fe.make_editor(dict(LOC_FIELD), {}, lambda: None, editor)
    cb = w._key_widget("")
    codes = [cb.itemText(i) for i in range(cb.count())]
    assert "en" not in codes
    for c in ("zh_CN", "zh_TW", "de_DE", "es_ES", "fr_FR", "pl_PL", "pt_BR",
              "ru_RU", "ja_JP", "tr_TR", "ko_KR", "sk_SK"):
        assert c in codes
    assert cb.isEditable()                       # future codes still typeable


def test_localized_roundtrip_identical_json(editor):
    original = {"tr_TR": "Alev Kılıcı", "de_DE": "Flammenklinge"}
    data = {"nameLocalized": dict(original)}
    w = fe.make_editor(dict(LOC_FIELD), data, lambda: None, editor)
    w._sync()                                    # rendering + sync must not mutate
    assert data["nameLocalized"] == original


def test_localized_add_and_remove_rows(editor):
    data = {}
    w = fe.make_editor(dict(LOC_FIELD), data, lambda: None, editor)
    w._add_row("tr_TR", "Merhaba"); w._sync()
    assert data["nameLocalized"] == {"tr_TR": "Merhaba"}
    w._add_row("xx_XX", "Future"); w._sync()     # free-typed unknown code
    assert data["nameLocalized"]["xx_XX"] == "Future"
    # clearing all rows removes the key entirely (same contract as DictEditor)
    for rw in [w.rows.itemAt(i).widget() for i in range(w.rows.count())]:
        if rw:
            rw.setParent(None)
    w._sync()
    assert "nameLocalized" not in data


# ---- YarnDialog ----------------------------------------------------------------------------------
@pytest.fixture()
def yarn_pack(editor, tmp_path):
    """A temp pack folder with the hermit yarn + tr_TR csv and a saved pack path."""
    shutil.copy(os.path.join(EX_TEST, "hermit.yarn"), tmp_path / "hermit.yarn")
    shutil.copy(os.path.join(EX_TEST, "hermit.tr_TR.csv"), tmp_path / "hermit.tr_TR.csv")
    pack = json.load(open(TEST_PACK, encoding="utf-8"))
    npc = next(n for n in pack["npcs"] if n.get("yarn") == "hermit.yarn")
    editor.pack = pack
    editor.path = str(tmp_path / "pack.json")
    return editor, npc, tmp_path


def test_dialog_constructs_offscreen(yarn_pack):
    ed, npc, _ = yarn_pack
    dlg = fe.YarnDialog(ed, npc, "hermit.yarn")
    titles = [dlg.nodes.item(i).text() for i in range(dlg.nodes.count())]
    assert titles == ["Start"]
    assert dlg.table.rowCount() == 16
    assert dlg.locale_cb.currentText() == "tr_TR"
    assert "16/16" in dlg.coverage_lbl.text()    # the shipped csv is complete


def test_dialog_new_locale_prefills_speaker_prefix(yarn_pack):
    ed, npc, _ = yarn_pack
    dlg = fe.YarnDialog(ed, npc, "hermit.yarn")
    dlg.new_locale_cb.setCurrentText("de_DE")
    dlg.add_locale()
    assert dlg.locale_cb.currentText() == "de_DE"
    ids = [dlg.table.item(r, 0).text() for r in range(dlg.table.rowCount())]
    r_sp = ids.index("line:hermit_01")           # speaker line -> prefilled prefix, untranslated
    r_opt = ids.index("line:hermit_opt_who")     # option -> empty, no prefix
    assert dlg.table.item(r_sp, 2).text() == "cftest_hermit: "
    assert dlg.table.item(r_opt, 2).text() == ""
    assert "0/16" in dlg.coverage_lbl.text()


def test_dialog_save_writes_yarn_and_new_csv_and_updates_entry(yarn_pack):
    ed, npc, tmp = yarn_pack
    dlg = fe.YarnDialog(ed, npc, "hermit.yarn")
    dlg.new_locale_cb.setCurrentText("de_DE")
    dlg.add_locale()
    ids = [dlg.table.item(r, 0).text() for r in range(dlg.table.rowCount())]
    dlg.table.item(ids.index("line:hermit_01"), 2).setText("cftest_hermit: Ah... ein Besucher.")
    dlg.table.item(ids.index("line:hermit_opt_who"), 2).setText("Wer bist du?")
    assert "2/16" in dlg.coverage_lbl.text()
    dlg.save()
    # yarn rewritten unchanged, semantically
    assert yt.extract_lines((tmp / "hermit.yarn").read_text(encoding="utf-8")) \
        == yt.extract_lines(open(os.path.join(EX_TEST, "hermit.yarn"), encoding="utf-8").read())
    # new sidecar csv written, prefix kept, prefix-only rows skipped
    de = yt.read_translation_csv(str(tmp / "hermit.de_DE.csv"))
    assert de == {"line:hermit_01": "cftest_hermit: Ah... ein Besucher.",
                  "line:hermit_opt_who": "Wer bist du?"}
    # the tr_TR sidecar survives a save untouched in content
    tr = yt.read_translation_csv(str(tmp / "hermit.tr_TR.csv"))
    assert len(tr) == 16 and tr["line:hermit_opt_who"] == "Sen kimsin?"
    # the in-memory pack entry gained the locale
    assert npc["translations"] == {"tr_TR": "hermit.tr_TR.csv", "de_DE": "hermit.de_DE.csv"}


def test_dialog_auto_tag_and_lint_buttons(yarn_pack, tmp_path):
    ed, npc, tmp = yarn_pack
    (tmp / "rough.yarn").write_text("title: Start\n---\ncftest_hermit: untagged hello\n===\n", encoding="utf-8")
    entry = {"id": "cftest:rough", "yarn": "rough.yarn", "actors": {"cftest_hermit": "H"}}
    dlg = fe.YarnDialog(ed, entry, "rough.yarn")
    assert dlg.table.rowCount() == 0             # nothing tagged yet
    dlg.lint()
    texts = [dlg.lint_list.item(i).text() for i in range(dlg.lint_list.count())]
    assert any("no #line: tag" in t for t in texts)
    dlg.auto_tag()
    assert "#line:rough_01" in dlg.text.toPlainText()
    assert dlg.table.rowCount() == 1             # grid refreshed from the tagged text
    dlg.auto_tag()                               # idempotent
    assert "added 0" in dlg.status_lbl.text()
    dlg.lint()
    texts = [dlg.lint_list.item(i).text() for i in range(dlg.lint_list.count())]
    assert not any("no #line: tag" in t for t in texts)


def test_yarn_field_grows_edit_dialogue_button(yarn_pack):
    ed, npc, _ = yarn_pack
    field = {"key": "yarn", "type": "file", "filter": "yarn"}
    row = fe.make_editor(field, dict(npc), lambda: None, ed)
    buttons = [row.layout().itemAt(i).widget() for i in range(row.layout().count())]
    assert any(isinstance(b, fe.QPushButton) and "dialogue" in b.text().lower() for b in buttons)
