#!/usr/bin/env python3
"""
Content Forge — visual pack editor (PySide6, cross-platform).

Reads schema.json (the field manifest mirroring ContentPack.cs / PACKS.md) and builds the forms from it, so
adding a field to the manifest makes it appear here. Edits the same pack .json the mod loads and cfvalidate checks.

Run:  pip install -r requirements.txt   then   python forge_editor.py
"""
import json
import os
import re
import sys

from PySide6.QtCore import Qt
from PySide6.QtGui import (
    QAction, QColor, QFont, QKeySequence, QPixmap, QSyntaxHighlighter, QTextCharFormat, QTextCursor,
)
from PySide6.QtWidgets import (
    QApplication, QCheckBox, QComboBox, QDialog, QDoubleSpinBox, QFileDialog, QFormLayout, QGroupBox,
    QHBoxLayout, QHeaderView, QLabel, QLineEdit, QListWidget, QListWidgetItem, QMainWindow, QMessageBox,
    QPlainTextEdit, QPushButton, QScrollArea, QSpinBox, QSplitter, QTableWidget, QTableWidgetItem,
    QVBoxLayout, QWidget,
)

import yarn_tools

# When frozen by PyInstaller, data files live under sys._MEIPASS; in dev, next to this file.
HERE = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))


def data_path(rel):
    """Resolve a bundled data file, working both in dev (some files live in ../reference) and frozen."""
    for base in (HERE, os.path.join(HERE, "..")):
        p = os.path.join(base, rel)
        if os.path.exists(p):
            return p
    return os.path.join(HERE, rel)


SCHEMA = json.load(open(os.path.join(HERE, "schema.json"), encoding="utf-8"))
ENUMS = SCHEMA["enums"]
VALUE_TYPES = SCHEMA.get("valueTypes", {})
try:
    FIELD_LABELS = json.load(open(os.path.join(HERE, "labels.json"), encoding="utf-8"))
except Exception:
    FIELD_LABELS = {}

# ---- the Forge dark theme (consistent with the asset catalog's dark look) ----
C_BG = "#1e1f22"        # window background
C_PANEL = "#25272b"     # lists / toolbars / boxes
C_FIELD = "#2b2d31"     # inputs
C_BORDER = "#3d4046"
C_TEXT = "#e6e2da"
C_SUB = "#8b8e98"       # captions / secondary text
C_ACCENT = "#d89a4e"    # forge amber
C_ACCENT_DIM = "#b46a32"  # forge bronze
C_ERR = "#ff6b6b"
C_WARN = "#e0b452"
C_OK = "#7ec97e"

QSS = f"""
QWidget {{ background: {C_BG}; color: {C_TEXT}; font-size: 13px; }}
QMainWindow::separator {{ background: {C_BORDER}; width: 1px; }}
QToolBar {{ background: {C_PANEL}; border: 0; border-bottom: 1px solid {C_BORDER}; padding: 4px; spacing: 4px; }}
QToolBar QToolButton {{ background: transparent; color: {C_TEXT}; border: 1px solid transparent;
                        border-radius: 5px; padding: 5px 10px; }}
QToolBar QToolButton:hover {{ background: {C_FIELD}; border-color: {C_BORDER}; }}
QToolBar QToolButton:pressed {{ background: {C_ACCENT_DIM}; color: #fff; }}
QMenuBar {{ background: {C_PANEL}; border-bottom: 1px solid {C_BORDER}; }}
QMenuBar::item {{ padding: 4px 10px; background: transparent; }}
QMenuBar::item:selected {{ background: {C_FIELD}; border-radius: 4px; }}
QMenu {{ background: {C_PANEL}; border: 1px solid {C_BORDER}; }}
QMenu::item {{ padding: 5px 24px; }}
QMenu::item:selected {{ background: {C_ACCENT_DIM}; color: #fff; }}
QListWidget {{ background: {C_PANEL}; border: 1px solid {C_BORDER}; border-radius: 6px; padding: 3px; outline: 0; }}
QListWidget::item {{ padding: 4px 6px; border-radius: 4px; }}
QListWidget::item:hover {{ background: {C_FIELD}; }}
QListWidget::item:selected {{ background: {C_ACCENT_DIM}; color: #ffffff; }}
QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox, QPlainTextEdit {{
    background: {C_FIELD}; border: 1px solid {C_BORDER}; border-radius: 5px; padding: 4px 6px;
    selection-background-color: {C_ACCENT_DIM}; selection-color: #fff; }}
QLineEdit:focus, QComboBox:focus, QSpinBox:focus, QDoubleSpinBox:focus, QPlainTextEdit:focus {{
    border-color: {C_ACCENT}; }}
QComboBox::drop-down {{ border: 0; width: 20px; }}
QComboBox::down-arrow {{ image: none; border-left: 4px solid transparent; border-right: 4px solid transparent;
                         border-top: 5px solid {C_SUB}; margin-right: 6px; }}
QComboBox QAbstractItemView {{ background: {C_PANEL}; border: 1px solid {C_BORDER};
                               selection-background-color: {C_ACCENT_DIM}; selection-color: #fff; }}
QSpinBox::up-button, QDoubleSpinBox::up-button, QSpinBox::down-button, QDoubleSpinBox::down-button {{
    background: {C_FIELD}; border-left: 1px solid {C_BORDER}; width: 16px; }}
QSpinBox::up-arrow, QDoubleSpinBox::up-arrow {{ border-left: 3px solid transparent; border-right: 3px solid transparent;
    border-bottom: 4px solid {C_SUB}; }}
QSpinBox::down-arrow, QDoubleSpinBox::down-arrow {{ border-left: 3px solid transparent; border-right: 3px solid transparent;
    border-top: 4px solid {C_SUB}; }}
QPushButton {{ background: {C_FIELD}; border: 1px solid {C_BORDER}; border-radius: 5px; padding: 5px 12px; }}
QPushButton:hover {{ border-color: {C_ACCENT}; color: {C_ACCENT}; }}
QPushButton:pressed {{ background: {C_ACCENT_DIM}; color: #fff; }}
QGroupBox {{ border: 1px solid {C_BORDER}; border-radius: 6px; margin-top: 10px; padding-top: 8px;
             background: transparent; }}
QGroupBox::title {{ subcontrol-origin: margin; left: 8px; padding: 0 4px; color: {C_ACCENT}; }}
QGroupBox::indicator {{ width: 14px; height: 14px; border: 1px solid {C_BORDER}; border-radius: 3px;
                        background: {C_FIELD}; }}
QGroupBox::indicator:checked, QCheckBox::indicator:checked {{ background: {C_ACCENT}; border-color: {C_ACCENT}; }}
QCheckBox::indicator {{ width: 14px; height: 14px; border: 1px solid {C_BORDER}; border-radius: 3px;
                        background: {C_FIELD}; }}
QScrollArea {{ border: 0; }}
QScrollBar:vertical {{ background: {C_BG}; width: 11px; margin: 0; }}
QScrollBar::handle:vertical {{ background: {C_BORDER}; border-radius: 5px; min-height: 24px; }}
QScrollBar::handle:vertical:hover {{ background: {C_ACCENT_DIM}; }}
QScrollBar:horizontal {{ background: {C_BG}; height: 11px; margin: 0; }}
QScrollBar::handle:horizontal {{ background: {C_BORDER}; border-radius: 5px; min-width: 24px; }}
QScrollBar::add-line, QScrollBar::sub-line {{ height: 0; width: 0; }}
QScrollBar::add-page, QScrollBar::sub-page {{ background: transparent; }}
QToolTip {{ background: {C_PANEL}; color: {C_TEXT}; border: 1px solid {C_ACCENT_DIM}; padding: 4px 6px; }}
QStatusBar {{ background: {C_PANEL}; border-top: 1px solid {C_BORDER}; color: {C_SUB}; }}
QLabel#formTitle {{ font-size: 17px; font-weight: bold; color: {C_ACCENT}; padding: 2px 0 0 2px;
                    background: transparent; }}
QLabel#formSub {{ color: {C_SUB}; font-size: 12px; padding: 0 0 6px 2px; background: transparent; }}
QLabel[cls="caption"] {{ color: {C_SUB}; font-size: 11px; padding-left: 2px; background: transparent; }}
QMessageBox {{ background: {C_PANEL}; }}
"""


def flabel(ctx, field):
    """Translated form label for a field."""
    return ctx.field_label(field["key"]) if ctx is not None else field["key"]


def caption(text):
    lbl = QLabel(text)
    lbl.setProperty("cls", "caption")
    lbl.setWordWrap(True)
    return lbl


def add_form_row(form, f, w, ctx):
    """Add one schema field's widget (+ help caption) to a QFormLayout, matching the field's shape."""
    if f["type"] in ("object", "dict", "damageMap", "array", "pointList", "localized"):
        form.addRow(w)
    else:
        lbl = QLabel(flabel(ctx, f))
        if f.get("help"):
            lbl.setToolTip(f["help"]); w.setToolTip(f["help"])
        form.addRow(lbl, w)
    if f.get("help"):
        form.addRow(caption(f["help"]))


def editable_combo(values, current):
    """An editable combobox seeded from `values` (a known catalog) but still free-typeable for new ids."""
    cb = QComboBox(); cb.setEditable(True); cb.setInsertPolicy(QComboBox.NoInsert)
    cb.addItems([""] + list(values))
    comp = cb.completer(); comp.setCompletionMode(comp.CompletionMode.PopupCompletion); comp.setFilterMode(Qt.MatchContains)
    cb.setCurrentText(str(current or ""))
    return cb


def file_picker_row(initial, base_dir, on_text):
    """A filename line-edit + browse button + 48px thumbnail (for `file` fields and file dict values)."""
    row = QWidget(); lay = QHBoxLayout(row); lay.setContentsMargins(0, 0, 0, 0)
    le = QLineEdit(str(initial or ""))
    le.textChanged.connect(lambda t: on_text(t.strip()))
    lay.addWidget(le)
    thumb = QLabel(); thumb.setFixedSize(48, 48); thumb.setStyleSheet(f"border:1px solid {C_BORDER}; background:{C_FIELD};")

    def refresh_thumb(p):
        full = os.path.join(base_dir, p) if base_dir and p else p
        if p and os.path.isfile(full):
            pm = QPixmap(full)
            if not pm.isNull():
                thumb.setPixmap(pm.scaled(48, 48, Qt.KeepAspectRatio, Qt.FastTransformation)); return
        thumb.clear()

    def browse():
        fn, _ = QFileDialog.getOpenFileName(row, "Pick file", base_dir or HERE,
                                            "Images & assets (*.png *.wav *.ogg *.mp3 *.flac *.yarn *.tmx);;All files (*)")
        if fn:
            le.setText(os.path.basename(fn))

    b = QPushButton("…"); b.setFixedWidth(28); b.clicked.connect(browse)
    le.textChanged.connect(refresh_thumb); refresh_thumb(initial or "")
    lay.addWidget(b); lay.addWidget(thumb)
    row.setProperty("le", le)
    return row


def normalize(field):
    """Schema compat: 'localized' is sugar for a per-language string dict."""
    if field.get("type") == "localized":
        field = dict(field, type="dict", valueType="string", keySource="languages")
    return field


# ---- editors for one field; each returns a widget and writes back into `data[key]` on change ----
def make_editor(field, data, on_change, ctx):
    field = normalize(field)
    key, ftype = field["key"], field["type"]
    default = field.get("default")
    base_dir = ctx.base_dir()

    def commit(v):
        if v in (None, "", [], {}):
            data.pop(key, None)
        else:
            data[key] = v
        on_change()

    # id field backed by a game catalog -> editable dropdown (still type-able for your own new ids)
    if ftype == "string" and field.get("source"):
        cb = editable_combo(ctx.source_values(field["source"]), data.get(key, default))
        cb.currentTextChanged.connect(lambda t: commit(t.strip()))
        cb.lineEdit().textChanged.connect(lambda t: commit(t.strip()))
        return cb

    if ftype == "file":
        row = file_picker_row(data.get(key, ""), base_dir, commit)
        if field.get("filter") == "yarn":
            b = QPushButton(ctx.t("editdialogue") if hasattr(ctx, "t") else "Edit dialogue…")
            b.clicked.connect(lambda: launch_yarn_editor(ctx, data, row.property("le"), on_change))
            row.layout().addWidget(b)
        return row

    if ftype == "string":
        le = QLineEdit(str(data.get(key, "")))
        le.textChanged.connect(lambda t: commit(t.strip()))
        return le

    if ftype == "bool":
        cb = QCheckBox(); cb.setChecked(bool(data.get(key, default or False)))
        cb.stateChanged.connect(lambda _: commit(cb.isChecked()))
        return cb

    if ftype == "int":
        sp = QSpinBox(); sp.setRange(-10_000_000, 10_000_000)
        sp.setValue(int(data.get(key, default or 0)))
        sp.valueChanged.connect(lambda v: commit(v))
        return sp

    if ftype == "float":
        sp = QDoubleSpinBox(); sp.setRange(-1_000_000, 1_000_000); sp.setDecimals(3); sp.setSingleStep(0.1)
        sp.setValue(float(data.get(key, default or 0.0)))
        sp.valueChanged.connect(lambda v: commit(round(v, 4)))
        return sp

    if ftype == "enum":
        values = ENUMS.get(field.get("enum"), field.get("values") or [])
        cb = QComboBox(); cb.addItems(values)
        cur = str(data.get(key, default or ""))
        if cur in values:
            cb.setCurrentText(cur)
        cb.currentTextChanged.connect(lambda t: commit(t))
        return cb

    if ftype == "object":
        return ObjectEditor(field, data, on_change, ctx)

    if ftype in ("dict", "damageMap"):
        if field.get("valueType") == "object":
            return DictObjectEditor(field, data, on_change, ctx)
        if field.get("keySource") == "languages" and field.get("valueType") == "string":
            return LocalizedEditor(field, data, on_change, ctx)   # all `localized` / *Localized fields
        return DictEditor(field, data, on_change, ctx)

    if ftype == "pointList":
        return PointListEditor(field, data, on_change, ctx)

    if ftype == "array":
        item = field.get("item") or {}
        if item.get("type") == "object":
            return ObjectArrayEditor(field, data, on_change, ctx)
        if item.get("source"):
            return StringListEditor(field, data, on_change, ctx)
        return ArrayEditor(field, data, on_change, ctx)

    return QLabel(f"<unsupported: {ftype}>")


class ObjectEditor(QGroupBox):
    """An optional nested object (equippable, weapon, …). Checkbox toggles its presence."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__("")
        self.setCheckable(True)
        self.field, self.parent_data, self.on_change, self.ctx = field, parent_data, on_change, ctx
        self.setTitle(ctx.field_label(field["key"]))
        if field.get("help"):
            self.setToolTip(field["help"])
        present = isinstance(parent_data.get(field["key"]), dict)
        self.setChecked(present)
        self.body = QWidget(); self.form = QFormLayout(self.body)
        outer = QVBoxLayout(self); outer.addWidget(self.body)
        self.toggled.connect(self._toggle)
        if present:
            self._render()

    def _toggle(self, on):
        if on and not isinstance(self.parent_data.get(self.field["key"]), dict):
            self.parent_data[self.field["key"]] = {}; self._render()
        elif not on:
            self.parent_data.pop(self.field["key"], None); self._clear()
        self.on_change()

    def _clear(self):
        while self.form.rowCount():
            self.form.removeRow(0)

    def _render(self):
        self._clear()
        self.setTitle(self.ctx.field_label(self.field["key"]))
        data = self.parent_data[self.field["key"]]
        for f in self.field["fields"]:
            w = make_editor(f, data, self.on_change, self.ctx)
            add_form_row(self.form, normalize(f), w, self.ctx)


class DictEditor(QGroupBox):
    """key -> value rows. Keys: damage types / a catalog (keySource) / free text. Values: float, int, string,
    file (with browse + thumbnail), or a statBonus pair (additive + multiplier spinboxes)."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__("")
        self.field, self.parent_data, self.on_change, self.ctx = field, parent_data, on_change, ctx
        self.setTitle(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.vtype = "float" if field["type"] == "damageMap" else field.get("valueType", "int")
        self.lay = QVBoxLayout(self)
        self.rows_box = QWidget(); self.rows = QVBoxLayout(self.rows_box); self.rows.setContentsMargins(0, 0, 0, 0)
        self.lay.addWidget(self.rows_box)
        self.add_btn = QPushButton(ctx.t("add") if hasattr(ctx, "t") else "+ add")
        self.add_btn.clicked.connect(lambda: self._add_row("", None)); self.lay.addWidget(self.add_btn)
        for k, v in dict(parent_data.get(field["key"], {}) or {}).items():
            self._add_row(k, v)

    def _key_widget(self, k):
        if self.field["type"] == "damageMap":
            w = QComboBox(); w.addItems(ENUMS["damageType"])
            if k in ENUMS["damageType"]:
                w.setCurrentText(k)
            return w
        if self.field.get("keySource"):
            return editable_combo(self.ctx.source_values(self.field["keySource"]), k)
        return QLineEdit(str(k))

    def _add_row(self, k, v):
        row = QWidget(); h = QHBoxLayout(row); h.setContentsMargins(0, 0, 0, 0)
        kw = self._key_widget(k)
        h.addWidget(kw, 1)
        if self.vtype == "statBonus":
            v = v if isinstance(v, dict) else {}
            add_sp = QDoubleSpinBox(); add_sp.setRange(-1e6, 1e6); add_sp.setDecimals(3); add_sp.setPrefix("+ ")
            add_sp.setValue(float(v.get("additive", 0)))
            mul_sp = QDoubleSpinBox(); mul_sp.setRange(-1e6, 1e6); mul_sp.setDecimals(3); mul_sp.setPrefix("× ")
            mul_sp.setValue(float(v.get("multiplier", 0)))
            row.setProperty("vw", (add_sp, mul_sp))
            h.addWidget(add_sp); h.addWidget(mul_sp)
            add_sp.valueChanged.connect(lambda _: self._sync()); mul_sp.valueChanged.connect(lambda _: self._sync())
        elif self.vtype == "file":
            vw = file_picker_row("" if v is None else str(v), self.ctx.base_dir(), lambda _: self._sync())
            row.setProperty("vw", vw); h.addWidget(vw, 2)
        elif self.vtype == "string":
            vw = QLineEdit("" if v is None else str(v)); vw.setMinimumWidth(220)
            row.setProperty("vw", vw); h.addWidget(vw, 2); vw.textChanged.connect(lambda _: self._sync())
        elif self.vtype == "int":
            vw = QSpinBox(); vw.setRange(-10_000_000, 10_000_000); vw.setValue(int(v or 0))
            row.setProperty("vw", vw); h.addWidget(vw); vw.valueChanged.connect(lambda _: self._sync())
        else:
            vw = QDoubleSpinBox(); vw.setRange(-1e6, 1e6); vw.setDecimals(3); vw.setValue(float(v or 0))
            row.setProperty("vw", vw); h.addWidget(vw); vw.valueChanged.connect(lambda _: self._sync())
        if isinstance(kw, QComboBox):
            kw.currentTextChanged.connect(lambda _: self._sync())
        else:
            kw.textChanged.connect(lambda _: self._sync())
        rm = QPushButton("✕"); rm.setFixedWidth(28)
        rm.clicked.connect(lambda: (row.setParent(None), self._sync()))
        row.setProperty("kw", kw); h.addWidget(rm)
        self.rows.addWidget(row)

    def _sync(self):
        d = {}
        for i in range(self.rows.count()):
            rw = self.rows.itemAt(i).widget()
            if rw is None:
                continue
            kk, vv = rw.property("kw"), rw.property("vw")
            key = kk.currentText().strip() if isinstance(kk, QComboBox) else kk.text().strip()
            if not key:
                continue
            if isinstance(vv, tuple):
                sb = {}
                if vv[0].value(): sb["additive"] = round(vv[0].value(), 4)
                if vv[1].value(): sb["multiplier"] = round(vv[1].value(), 4)
                d[key] = sb
            elif isinstance(vv, QLineEdit):
                d[key] = vv.text()
            elif isinstance(vv, QSpinBox):
                d[key] = vv.value()
            elif isinstance(vv, QDoubleSpinBox):
                d[key] = round(vv.value(), 4)
            else:  # file picker row
                d[key] = vv.property("le").text().strip()
        if d:
            self.parent_data[self.field["key"]] = d
        else:
            self.parent_data.pop(self.field["key"], None)
        self.on_change()


class LocalizedEditor(DictEditor):
    """Per-language text (`nameLocalized` & friends): a (language, text) row table. The language cell
    is a combo seeded from the game's supported codes ('en' excluded — that's the base name/description
    field), still free-typeable for future codes. Same JSON shape as DictEditor (lang -> string)."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__(field, parent_data, on_change, ctx)
        self.add_btn.setText(ctx.t("addlang") if hasattr(ctx, "t") else "+ language")

    def _key_widget(self, k):
        codes = [c for c in self.ctx.source_values("languages") if c != "en"]
        return editable_combo(codes, k)


class DictObjectEditor(QGroupBox):
    """key -> OBJECT rows (spac animations, playerAnimations, citizenOverrides): each row is a named sub-form."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__("")
        self.field, self.parent_data, self.on_change, self.ctx = field, parent_data, on_change, ctx
        self.setTitle(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.entries = []  # [(key_widget, row_dict, box)]
        lay = QVBoxLayout(self)
        self.rows_box = QWidget(); self.rows = QVBoxLayout(self.rows_box); self.rows.setContentsMargins(0, 0, 0, 0)
        lay.addWidget(self.rows_box)
        add = QPushButton("+ add"); add.clicked.connect(lambda: self._add_row("", {})); lay.addWidget(add)
        for k, v in dict(parent_data.get(field["key"], {}) or {}).items():
            self._add_row(k, dict(v) if isinstance(v, dict) else {})

    def _add_row(self, k, row_dict):
        box = QGroupBox(); form = QFormLayout(box)
        if self.field.get("keySource"):
            kw = editable_combo(self.ctx.source_values(self.field["keySource"]), k)
            kw.currentTextChanged.connect(lambda _: self._sync())
            kw.lineEdit().textChanged.connect(lambda _: self._sync())
        else:
            kw = QLineEdit(str(k))
            kw.textChanged.connect(lambda _: self._sync())
        form.addRow(QLabel("key"), kw)
        for f in self.field.get("fields", []):
            w = make_editor(f, row_dict, self._sync, self.ctx)
            add_form_row(form, normalize(f), w, self.ctx)
        rm = QPushButton("✕ remove")
        entry = (kw, row_dict, box)
        rm.clicked.connect(lambda: self._remove(entry))
        form.addRow(rm)
        self.entries.append(entry)
        self.rows.addWidget(box)

    def _remove(self, entry):
        if entry in self.entries:
            self.entries.remove(entry)
        entry[2].setParent(None)
        self._sync()

    def _sync(self):
        d = {}
        for kw, row_dict, _box in self.entries:
            key = kw.currentText().strip() if isinstance(kw, QComboBox) else kw.text().strip()
            if key:
                d[key] = row_dict
        if d:
            self.parent_data[self.field["key"]] = d
        else:
            self.parent_data.pop(self.field["key"], None)
        self.on_change()


class ArrayEditor(QGroupBox):
    """Scalar arrays (dependencies: string[], frames: int[]). Edited as one comma/space separated line."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.field, self.parent_data, self.on_change = field, parent_data, on_change
        lay = QVBoxLayout(self)
        cur = parent_data.get(field["key"], []) or []
        self.le = QLineEdit(", ".join(str(x) for x in cur))
        self.le.setPlaceholderText("comma-separated")
        self.le.textChanged.connect(self._sync)
        lay.addWidget(self.le)

    def _sync(self, _):
        raw = [p.strip() for p in self.le.text().replace(",", " ").split() if p.strip()]
        itype = (self.field.get("item") or {}).get("type")
        if itype == "int":
            vals = []
            for p in raw:
                try:
                    vals.append(int(p))
                except ValueError:
                    pass
        elif itype == "float":
            vals = []
            for p in raw:
                try:
                    vals.append(float(p))
                except ValueError:
                    pass
        else:
            vals = raw
        if vals:
            self.parent_data[self.field["key"]] = vals
        else:
            self.parent_data.pop(self.field["key"], None)
        self.on_change()


class StringListEditor(QGroupBox):
    """A string array whose items come from a catalog (shop items, biomes, bosses): one combo row per value."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.field, self.parent_data, self.on_change, self.ctx = field, parent_data, on_change, ctx
        lay = QVBoxLayout(self)
        self.rows_box = QWidget(); self.rows = QVBoxLayout(self.rows_box); self.rows.setContentsMargins(0, 0, 0, 0)
        lay.addWidget(self.rows_box)
        add = QPushButton("+ add"); add.clicked.connect(lambda: self._add_row("")); lay.addWidget(add)
        for v in list(parent_data.get(field["key"], []) or []):
            self._add_row(v)

    def _add_row(self, v):
        row = QWidget(); h = QHBoxLayout(row); h.setContentsMargins(0, 0, 0, 0)
        cb = editable_combo(self.ctx.source_values(self.field["item"]["source"]), v)
        cb.currentTextChanged.connect(lambda _: self._sync())
        cb.lineEdit().textChanged.connect(lambda _: self._sync())
        rm = QPushButton("✕"); rm.setFixedWidth(28)
        rm.clicked.connect(lambda: (row.setParent(None), self._sync()))
        row.setProperty("cb", cb)
        h.addWidget(cb, 1); h.addWidget(rm)
        self.rows.addWidget(row)

    def _sync(self):
        vals = []
        for i in range(self.rows.count()):
            rw = self.rows.itemAt(i).widget()
            if rw is None:
                continue
            t = rw.property("cb").currentText().strip()
            if t:
                vals.append(t)
        if vals:
            self.parent_data[self.field["key"]] = vals
        else:
            self.parent_data.pop(self.field["key"], None)
        self.on_change()


class PointListEditor(QGroupBox):
    """A list of [x, y] points (race checkpoints): one 'x, y' pair per line."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.field, self.parent_data, self.on_change = field, parent_data, on_change
        lay = QVBoxLayout(self)
        cur = parent_data.get(field["key"], []) or []
        self.te = QPlainTextEdit("\n".join(", ".join(str(v) for v in p) for p in cur if isinstance(p, (list, tuple))))
        self.te.setPlaceholderText("one point per line:  x, y")
        self.te.setFixedHeight(90)
        self.te.textChanged.connect(self._sync)
        lay.addWidget(self.te)

    def _sync(self):
        pts = []
        for line in self.te.toPlainText().splitlines():
            nums = []
            for p in line.replace(",", " ").split():
                try:
                    nums.append(float(p))
                except ValueError:
                    pass
            if len(nums) >= 2:
                pts.append(nums[:2])
        if pts:
            self.parent_data[self.field["key"]] = pts
        else:
            self.parent_data.pop(self.field["key"], None)
        self.on_change()


# ---- yarn dialogue editor (Feature: NPC / talking-placeable .yarn files) -------------------------
class YarnHighlighter(QSyntaxHighlighter):
    """Dark-theme yarn syntax: structure amber, -> options bronze, <<commands>> blue, speaker
    prefixes green, #line: tags dim, // comments dim italic."""
    def __init__(self, doc):
        super().__init__(doc)

        def fmt(color, bold=False, italic=False):
            f = QTextCharFormat(); f.setForeground(QColor(color))
            if bold: f.setFontWeight(QFont.Bold)
            if italic: f.setFontItalic(True)
            return f

        self.f_struct = fmt(C_ACCENT, bold=True)
        self.f_option = fmt(C_ACCENT_DIM)
        self.f_cmd = fmt("#6f9fd8")
        self.f_speaker = fmt(C_OK)
        self.f_tag = fmt(C_SUB)
        self.f_comment = fmt(C_SUB, italic=True)

    def highlightBlock(self, line):
        s = line.strip()
        if s in ("---", "===") or s.startswith("title:"):
            self.setFormat(0, len(line), self.f_struct)
            return
        if line.lstrip().startswith("->"):
            self.setFormat(0, len(line), self.f_option)
        m = yarn_tools.SPEAKER_RE.match(line)
        if m:
            self.setFormat(0, m.end(1) + 1, self.f_speaker)
        for mm in yarn_tools.CMD_RE.finditer(line):
            self.setFormat(mm.start(), mm.end() - mm.start(), self.f_cmd)
        for mm in re.finditer(r"#line:\S+", line):
            self.setFormat(mm.start(), mm.end() - mm.start(), self.f_tag)
        idx = line.find("//")
        if idx >= 0:
            self.setFormat(idx, len(line) - idx, self.f_comment)


class YarnDialog(QDialog):
    """Modal dialogue editor for one .yarn file: node outline (left), highlighted text + lint results
    (center), per-locale translation grid backed by the `line:id,text` sidecar csvs (right).
    Save writes the .yarn and every locale csv, and grows the entry's `translations` map in-memory."""
    def __init__(self, ctx, entry, yarn_name, on_change=None, parent=None):
        super().__init__(parent if parent is not None else (ctx if isinstance(ctx, QWidget) else None))
        self.setWindowFlags(self.windowFlags() | Qt.WindowMaximizeButtonHint | Qt.WindowMinimizeButtonHint)
        self.ctx, self.entry, self.yarn_name = ctx, entry, yarn_name
        self.on_change = on_change or (lambda: None)
        self.base = ctx.base_dir()
        self.yarn_path = os.path.join(self.base, yarn_name)
        stem = os.path.splitext(os.path.basename(yarn_name))[0]
        self.slug = re.sub(r"\W+", "_", stem).strip("_") or "line"
        self.csv_names = dict(entry.get("translations") or {})
        self.tr = {loc: yarn_tools.read_translation_csv(os.path.join(self.base, f))
                   for loc, f in self.csv_names.items()}

        self.setWindowTitle(f"Dialogue — {yarn_name}")
        self.resize(1280, 760)
        self.setStyleSheet(
            f"QTableWidget {{ background: {C_PANEL}; border: 1px solid {C_BORDER}; gridline-color: {C_BORDER}; }}"
            f"QHeaderView::section {{ background: {C_FIELD}; color: {C_SUB}; border: 0;"
            f" border-bottom: 1px solid {C_BORDER}; padding: 4px 6px; }}"
            f"QTableWidget::item:selected {{ background: {C_ACCENT_DIM}; color: #fff; }}")

        outer = QVBoxLayout(self)
        bar = QHBoxLayout()
        self.tag_btn = QPushButton("Auto-tag lines"); self.tag_btn.clicked.connect(self.auto_tag)
        self.lint_btn = QPushButton("Lint"); self.lint_btn.clicked.connect(self.lint)
        self.status_lbl = QLabel(""); self.status_lbl.setProperty("cls", "caption")
        self.save_btn = QPushButton("Save"); self.save_btn.clicked.connect(self.save)
        close_btn = QPushButton("Close"); close_btn.clicked.connect(self.accept)
        bar.addWidget(self.tag_btn); bar.addWidget(self.lint_btn); bar.addWidget(self.status_lbl)
        bar.addStretch(1); bar.addWidget(self.save_btn); bar.addWidget(close_btn)
        outer.addLayout(bar)

        split = QSplitter(Qt.Horizontal)
        # left: node outline
        self.nodes = QListWidget(); self.nodes.itemClicked.connect(self._jump_to_node)
        split.addWidget(self.nodes)
        # center: yarn text + lint results
        mid = QWidget(); midl = QVBoxLayout(mid); midl.setContentsMargins(0, 0, 0, 0)
        self.text = QPlainTextEdit()
        font = QFont("Consolas"); font.setStyleHint(QFont.Monospace); font.setPointSize(11)
        self.text.setFont(font); self.text.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.highlighter = YarnHighlighter(self.text.document())
        self.lint_list = QListWidget(); self.lint_list.setMaximumHeight(140); self.lint_list.setVisible(False)
        midl.addWidget(self.text, 1); midl.addWidget(self.lint_list)
        split.addWidget(mid)
        # right: translations grid
        right = QWidget(); rl = QVBoxLayout(right); rl.setContentsMargins(0, 0, 0, 0)
        locrow = QHBoxLayout()
        self.locale_cb = QComboBox(); self.locale_cb.currentTextChanged.connect(lambda _: self._rebuild_grid())
        self.new_locale_cb = editable_combo(self._known_codes(), "")
        addloc = QPushButton(self.ctx.t("addlang") if hasattr(self.ctx, "t") else "+ language")
        addloc.clicked.connect(self.add_locale)
        refresh = QPushButton("↻"); refresh.setFixedWidth(28); refresh.setToolTip("Refresh the grid from the yarn text")
        refresh.clicked.connect(self._rebuild_grid)
        locrow.addWidget(self.locale_cb, 1); locrow.addWidget(self.new_locale_cb, 1)
        locrow.addWidget(addloc); locrow.addWidget(refresh)
        rl.addLayout(locrow)
        self.table = QTableWidget(0, 3)
        self.table.setHorizontalHeaderLabels(["line id", "original", "translation"])
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(2, QHeaderView.Stretch)
        self.table.verticalHeader().setVisible(False)
        self.table.cellChanged.connect(self._cell_changed)
        rl.addWidget(self.table, 1)
        self.coverage_lbl = QLabel(""); self.coverage_lbl.setProperty("cls", "caption")
        rl.addWidget(self.coverage_lbl)
        split.addWidget(right)
        split.setSizes([170, 560, 550])
        outer.addWidget(split, 1)

        try:
            initial = open(self.yarn_path, encoding="utf-8-sig").read()
        except OSError:
            initial = yarn_tools.yarn_template(entry.get("actors"))
        self.text.setPlainText(initial)
        self.text.textChanged.connect(self._refresh_nodes)
        self._refresh_nodes()
        self.locale_cb.addItems(list(self.tr.keys()))
        self._rebuild_grid()

    # ---- helpers ----
    def _known_codes(self):
        have = set(self.tr)
        try:
            codes = self.ctx.source_values("languages")
        except Exception:
            codes = []
        return [c for c in codes if c != "en" and c not in have]

    def _refresh_nodes(self):
        nodes, _ = yarn_tools.parse_nodes(self.text.toPlainText())
        self.nodes.clear()
        for n in nodes:
            it = QListWidgetItem(n["title"] or "(untitled)")
            it.setData(Qt.UserRole, n["start"])
            self.nodes.addItem(it)

    def _jump_to_node(self, item):
        block = self.text.document().findBlockByNumber(item.data(Qt.UserRole))
        cur = QTextCursor(block)
        self.text.setTextCursor(cur); self.text.centerCursor(); self.text.setFocus()

    # ---- toolbar actions ----
    def auto_tag(self):
        new_text, added = yarn_tools.auto_tag(self.text.toPlainText(), self.slug)
        if added:
            pos = self.text.textCursor().position()
            self.text.setPlainText(new_text)
            cur = self.text.textCursor(); cur.setPosition(min(pos, len(new_text))); self.text.setTextCursor(cur)
            self._rebuild_grid()
        self.status_lbl.setText(f"added {added} #line: tag(s)")

    def lint(self):
        findings = yarn_tools.lint(self.text.toPlainText(), self.entry.get("actors") or {})
        self.lint_list.clear()
        if not findings:
            it = QListWidgetItem("✔  " + (self.ctx.t("looksgood") if hasattr(self.ctx, "t") else "Looks good."))
            it.setForeground(QColor(C_OK)); self.lint_list.addItem(it)
        for level, msg in findings:
            if level == "error":
                it = QListWidgetItem("✖  ERROR: " + msg); it.setForeground(QColor(C_ERR))
            else:
                it = QListWidgetItem("⚠  warn: " + msg); it.setForeground(QColor(C_WARN))
            self.lint_list.addItem(it)
        self.lint_list.setVisible(True)

    # ---- translation grid ----
    def add_locale(self):
        loc = self.new_locale_cb.currentText().strip()
        if not loc or loc == "en" or loc in self.tr:
            return
        self.tr[loc] = {}
        self.locale_cb.addItem(loc); self.locale_cb.setCurrentText(loc)
        self.new_locale_cb.clear(); self.new_locale_cb.addItems([""] + self._known_codes())

    def _rebuild_grid(self):
        loc = self.locale_cb.currentText()
        rows = yarn_tools.extract_lines(self.text.toPlainText())
        self._rows = rows
        self.table.blockSignals(True)
        self.table.setRowCount(len(rows))
        store = self.tr.get(loc, {})
        for r, row in enumerate(rows):
            tr = (store.get(row["id"]) or "").strip()
            id_it = QTableWidgetItem(row["id"]); id_it.setFlags(id_it.flags() & ~Qt.ItemIsEditable)
            orig_it = QTableWidgetItem(row["text"]); orig_it.setFlags(orig_it.flags() & ~Qt.ItemIsEditable)
            tr_it = QTableWidgetItem(tr if tr else row["prefix"])   # prefill the speaker prefix
            if not loc:
                tr_it.setFlags(tr_it.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 0, id_it); self.table.setItem(r, 1, orig_it); self.table.setItem(r, 2, tr_it)
            self._paint_row(r)
        self.table.blockSignals(False)
        self._update_coverage()

    def _row_translated(self, r):
        row = self._rows[r]
        v = (self.table.item(r, 2).text() or "").strip()
        return bool(v) and v != row["prefix"].strip()

    def _paint_row(self, r):
        dim_amber = QColor(C_WARN); dim_amber.setAlpha(36)
        clear = QColor(0, 0, 0, 0)
        bg = clear if self._row_translated(r) else dim_amber
        for c in range(3):
            it = self.table.item(r, c)
            if it:
                it.setBackground(bg)

    def _update_coverage(self):
        total = self.table.rowCount()
        done = sum(1 for r in range(total) if self._row_translated(r))
        loc = self.locale_cb.currentText()
        self.coverage_lbl.setText(f"{loc}: translated {done}/{total}" if loc else "no locale — add one to translate")

    def _cell_changed(self, r, c):
        if c != 2 or r >= len(self._rows):
            return
        loc = self.locale_cb.currentText()
        if not loc:
            return
        self.tr.setdefault(loc, {})[self._rows[r]["id"]] = self.table.item(r, 2).text()
        self.table.blockSignals(True); self._paint_row(r); self.table.blockSignals(False)
        self._update_coverage()

    # ---- save ----
    def save(self):
        text = self.text.toPlainText()
        with open(self.yarn_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text if text.endswith("\n") else text + "\n")
        rows = yarn_tools.extract_lines(text)
        ids = {r["id"] for r in rows}
        stem = os.path.splitext(os.path.basename(self.yarn_name))[0]
        written = []
        for loc, store in self.tr.items():
            out = {}
            for r in rows:                                   # yarn order first
                v = (store.get(r["id"]) or "").strip()
                if v and v != r["prefix"].strip():
                    out[r["id"]] = v
            for lid, v in store.items():                     # keep translator rows for removed lines
                if lid not in out and lid not in ids and (v or "").strip():
                    out[lid] = v.strip()
            fname = self.csv_names.get(loc) or f"{stem}.{loc}.csv"
            if out or os.path.isfile(os.path.join(self.base, fname)):
                yarn_tools.write_translation_csv(os.path.join(self.base, fname), out)
                self.csv_names[loc] = fname
                written.append(loc)
        if self.csv_names:
            self.entry["translations"] = dict(self.csv_names)   # pack entry gains new locales
        self.on_change()
        self.status_lbl.setText(f"saved {self.yarn_name}" + (f" + csv: {', '.join(written)}" if written else ""))


def launch_yarn_editor(ctx, entry, le, on_change):
    """The file picker's 'Edit dialogue…' button: resolve the yarn next to the pack (offering a
    template when it doesn't exist yet) and open the modal YarnDialog."""
    base = ctx.base_dir()
    if not base:
        QMessageBox.information(ctx, "Content Forge", ctx.t("savefirst"))
        return
    name = (le.text() or "").strip()
    if not name:
        ident = (entry.get("id") or "dialogue").split(":")[-1]
        name = (re.sub(r"\W+", "_", ident).strip("_") or "dialogue") + ".yarn"
        le.setText(name)                                     # commits into the entry via the picker
    path = os.path.join(base, name)
    if not os.path.isfile(path):
        q = QMessageBox.question(ctx, "Create dialogue file?",
                                 f"'{name}' doesn't exist next to the pack.\nCreate it from a minimal template?")
        if q != QMessageBox.StandardButton.Yes:
            return
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(yarn_tools.yarn_template(entry.get("actors")))
    YarnDialog(ctx, entry, name, on_change).exec()


# ---- app-UI translations (the editor's own chrome; separate from content text). External overrides:
# drop a editor/lang/<code>.json with the same keys to add/replace a language. ----
UI_LANGS = {
    "en": {"_name": "English", "title": "Content Forge — Pack Editor", "new": "New", "open": "Open", "save": "Save",
           "saveas": "Save As", "validate": "Validate", "add": "+ Add", "remove": "Remove", "language": "Language",
           "manifest": "Manifest", "noentries": "  (no entries — click + Add)", "savefirst": "Save the pack first.",
           "looksgood": "Looks good — 0 errors, 0 warnings.", "pack": "Pack",
           "addlang": "+ language", "editdialogue": "Edit dialogue…",
           "Items": "Items", "Spells": "Spells", "Recipes": "Recipes", "Palettes": "Palettes", "VFX": "VFX", "Drops": "Drops", "Classes": "Classes", "Quests": "Quests"},
    "tr_TR": {"_name": "Türkçe", "title": "Content Forge — Paket Düzenleyici", "new": "Yeni", "open": "Aç", "save": "Kaydet",
              "saveas": "Farklı Kaydet", "validate": "Doğrula", "add": "+ Ekle", "remove": "Sil", "language": "Dil",
              "manifest": "Künye", "noentries": "  (öğe yok — + Ekle'ye tıkla)", "savefirst": "Önce paketi kaydet.",
              "looksgood": "Sorun yok — 0 hata, 0 uyarı.", "pack": "Paket",
              "addlang": "+ dil", "editdialogue": "Diyaloğu düzenle…",
              "Items": "Eşyalar", "Spells": "Büyüler", "Recipes": "Tarifler", "Palettes": "Paletler", "VFX": "Efektler", "Drops": "Düşenler", "Classes": "Sınıflar", "Quests": "Görevler"},
    "de_DE": {"_name": "Deutsch", "title": "Content Forge — Paket-Editor", "new": "Neu", "open": "Öffnen", "save": "Speichern",
              "saveas": "Speichern unter", "validate": "Prüfen", "add": "+ Hinzufügen", "remove": "Entfernen", "language": "Sprache",
              "manifest": "Manifest", "noentries": "  (keine Einträge — + Hinzufügen)", "savefirst": "Erst das Paket speichern.",
              "looksgood": "Sieht gut aus — 0 Fehler, 0 Warnungen.", "pack": "Paket",
              "addlang": "+ Sprache", "editdialogue": "Dialog bearbeiten…",
              "Items": "Gegenstände", "Spells": "Zauber", "Recipes": "Rezepte", "Palettes": "Paletten", "VFX": "Effekte", "Drops": "Beute", "Classes": "Klassen", "Quests": "Quests"},
    "es_ES": {"_name": "Español", "title": "Content Forge — Editor de Paquetes", "new": "Nuevo", "open": "Abrir", "save": "Guardar",
              "saveas": "Guardar como", "validate": "Validar", "add": "+ Añadir", "remove": "Quitar", "language": "Idioma",
              "manifest": "Manifiesto", "noentries": "  (sin entradas — + Añadir)", "savefirst": "Guarda el paquete primero.",
              "looksgood": "Todo bien — 0 errores, 0 avisos.", "pack": "Paquete",
              "addlang": "+ idioma", "editdialogue": "Editar diálogo…",
              "Items": "Objetos", "Spells": "Hechizos", "Recipes": "Recetas", "Palettes": "Paletas", "VFX": "Efectos", "Drops": "Botín", "Classes": "Clases", "Quests": "Misiones"},
}
# External lang files override / add languages.
_lang_dir = os.path.join(HERE, "lang")
if os.path.isdir(_lang_dir):
    for fn in os.listdir(_lang_dir):
        if fn.endswith(".json"):
            try:
                code = fn[:-5]
                tbl = json.load(open(os.path.join(_lang_dir, fn), encoding="utf-8"))
                UI_LANGS.setdefault(code, {}).update(tbl)
            except Exception:
                pass


def app_icon():
    """The bundled forge icon, falling back to a drawn 'CF' anvil-orange tile."""
    from PySide6.QtGui import QIcon, QPainter
    for fn in ("forge_icon.png", "forge_icon.ico"):
        p = data_path(fn)
        if os.path.isfile(p):
            ic = QIcon(p)
            if not ic.isNull():
                return ic
    pm = QPixmap(64, 64); pm.fill(Qt.transparent)
    p = QPainter(pm); p.setRenderHint(QPainter.Antialiasing)
    p.setBrush(QColor(C_ACCENT_DIM)); p.setPen(Qt.NoPen); p.drawRoundedRect(4, 4, 56, 56, 12, 12)
    p.setPen(QColor("#fff5e8")); f = QFont("Arial", 26, QFont.Bold); p.setFont(f)
    p.drawText(pm.rect(), Qt.AlignCenter, "CF")
    p.end()
    return QIcon(pm)


class ObjectArrayEditor(QGroupBox):
    """A list of objects (e.g. spell `chain` = [{event, spell, target}, ...]) with add/remove + a sub-form each."""
    def __init__(self, field, parent_data, on_change, ctx):
        super().__init__("")
        self.field, self.parent_data, self.on_change, self.ctx = field, parent_data, on_change, ctx
        self.setTitle(flabel(ctx, field))
        if field.get("help"):
            self.setToolTip(field["help"])
        self.itemfields = field["item"]["fields"]
        lay = QVBoxLayout(self)
        self.items_box = QWidget(); self.items = QVBoxLayout(self.items_box); self.items.setContentsMargins(0, 0, 0, 0)
        lay.addWidget(self.items_box)
        add = QPushButton("+ add"); add.clicked.connect(self._add); lay.addWidget(add)
        for el in list(parent_data.get(field["key"], []) or []):
            self._render_item(el)

    def _add(self):
        el = {}; self.parent_data.setdefault(self.field["key"], []).append(el)
        self._render_item(el); self.on_change()

    def _render_item(self, el):
        box = QGroupBox(); f = QFormLayout(box)
        for fld in self.itemfields:
            w = make_editor(fld, el, self.on_change, self.ctx)
            add_form_row(f, normalize(fld), w, self.ctx)
        rm = QPushButton("✕ remove")
        rm.clicked.connect(lambda: self._remove(el, box))
        f.addRow(rm)
        self.items.addWidget(box)

    def _remove(self, el, box):
        lst = self.parent_data.get(self.field["key"], [])
        if el in lst:
            lst.remove(el)
        if not lst:
            self.parent_data.pop(self.field["key"], None)
        box.setParent(None); self.on_change()


# which section a validator message about '<kind> ...' belongs to (for click-to-jump)
KIND2SECTION = {
    "item": "items", "spell": "spells", "recipe": "recipes", "palette": "palettes", "vfx": "vfx",
    "drop": "drops", "quest": "quests", "class": "classes", "furniture": "furniture", "placeable": "furniture",
    "creature": "creatures", "map": "maps", "shop": "shops", "particle": "particles", "emitter": "emitters",
    "crop": "crops", "npc": "npcs", "skill": "skills", "playerskins": "playerSkins", "favour": "favours",
    "arena": "arenas", "event": "events", "citizen": "citizens", "spac": "spacs", "sound": "sounds",
    "icon": "icons", "townbuilding": "townBuildings", "workrecipe": "workRecipes",
}


class Editor(QMainWindow):
    def __init__(self):
        super().__init__()
        self.resize(1220, 840)
        self.path = None
        self.pack = {"id": "mypack", "namespace": "mymod", "version": "1.0.0"}
        self._src_cache = {}
        self.ui_lang = "en"
        self.setWindowIcon(app_icon())

        tb = self.addToolBar("main")
        tb.setMovable(False)
        self.acts = {}
        for k, fn, seq in [("new", self.new, QKeySequence.New), ("open", self.open, QKeySequence.Open),
                           ("save", self.save, QKeySequence.Save), ("saveas", self.save_as, QKeySequence("Ctrl+Shift+S")),
                           ("validate", self.validate, QKeySequence("F5"))]:
            a = QAction("", self); a.setShortcut(seq); a.triggered.connect(fn); tb.addAction(a); self.acts[k] = a

        # Language menu (switches the editor's own UI language)
        self.lang_menu = self.menuBar().addMenu("")
        for code, tbl in UI_LANGS.items():
            act = QAction(tbl.get("_name", code), self)
            act.triggered.connect(lambda _=False, c=code: self.set_ui_lang(c))
            self.lang_menu.addAction(act)

        central = QWidget(); outer = QVBoxLayout(central); self.setCentralWidget(central)
        body = QWidget(); root = QHBoxLayout(body); root.setContentsMargins(0, 0, 0, 0)
        outer.addWidget(body, 1)

        # left: sections, grouped like PACKS.md (group headers are non-selectable separators)
        self.sections = QListWidget(); self.sections.setFixedWidth(200)
        self._build_section_list()
        self.sections.currentRowChanged.connect(self.on_section)
        root.addWidget(self.sections)

        # middle: entries
        mid = QWidget(); midl = QVBoxLayout(mid); mid.setFixedWidth(230); midl.setContentsMargins(0, 0, 0, 0)
        self.entries = QListWidget(); self.entries.currentRowChanged.connect(self.on_entry)
        midl.addWidget(self.entries)
        btns = QHBoxLayout()
        self.add_btn = QPushButton(""); self.add_btn.clicked.connect(self.add_entry)
        self.del_btn = QPushButton(""); self.del_btn.clicked.connect(self.del_entry)
        btns.addWidget(self.add_btn); btns.addWidget(self.del_btn); midl.addLayout(btns)
        root.addWidget(mid)

        # right: form
        self.scroll = QScrollArea(); self.scroll.setWidgetResizable(True)
        root.addWidget(self.scroll, 1)

        # bottom: inline validate-results panel (hidden until Validate runs; click a line to jump)
        self.results = QListWidget(); self.results.setMaximumHeight(150); self.results.setVisible(False)
        self.results.itemClicked.connect(self._jump_to_result)
        outer.addWidget(self.results)

        # status bar: pack id + content counts
        self.status_lbl = QLabel("")
        self.statusBar().addPermanentWidget(self.status_lbl)

        self.cur_section = None
        self.retranslate()
        self._select_first_section()
        self.update_status()

    # ---- section list (groups from the schema; falls back to flat) ----
    def _build_section_list(self):
        self.sections.clear()
        listed = set()

        def header(text):
            it = QListWidgetItem(text.upper())
            it.setFlags(Qt.NoItemFlags)
            it.setForeground(QColor(C_ACCENT))
            f = it.font(); f.setBold(True); f.setPointSizeF(f.pointSizeF() - 1.5); it.setFont(f)
            it.setData(Qt.UserRole, None)
            it.setData(Qt.UserRole + 1, text)   # raw group label, for retranslate
            self.sections.addItem(it)

        def entry(key):
            it = QListWidgetItem("")
            it.setData(Qt.UserRole, key)
            self.sections.addItem(it)
            listed.add(key)

        header("pack")   # i18n key; group labels below are literal (t() falls back to the label itself)
        entry("_manifest")
        for grp in SCHEMA.get("groups", []):
            keys = [k for k in grp.get("sections", []) if k in SCHEMA["sections"]]
            if not keys:
                continue
            header(grp.get("label", ""))
            for k in keys:
                entry(k)
        rest = [k for k in SCHEMA["sections"] if k not in listed]
        if rest:
            header("Other")
            for k in rest:
                entry(k)

    def _select_first_section(self):
        for i in range(self.sections.count()):
            if self.sections.item(i).data(Qt.UserRole) is not None:
                self.sections.setCurrentRow(i)
                return

    # ---- i18n of the editor's own UI ----
    def t(self, key):
        return UI_LANGS.get(self.ui_lang, {}).get(key) or UI_LANGS["en"].get(key, key)

    def field_label(self, key):
        e = FIELD_LABELS.get(key, {})
        return e.get(self.ui_lang) or e.get("en") or key

    def set_ui_lang(self, code):
        self.ui_lang = code; self.retranslate()
        # rebuild the open form so its field labels switch language too
        if self.cur_section is None:
            self.show_form(self.manifest_fields(), self.pack, self.t("manifest"))
        else:
            self.on_section(self.sections.currentRow())

    def retranslate(self):
        self.setWindowTitle(self.t("title"))
        self.lang_menu.setTitle(self.t("language"))
        for k, a in self.acts.items():
            seq = a.shortcut().toString()
            a.setText(f"{self.t(k)}  ({seq})" if seq else self.t(k))
            a.setToolTip(f"{self.t(k)} — {seq}" if seq else self.t(k))
        self.add_btn.setText(self.t("add")); self.del_btn.setText(self.t("remove"))
        for i in range(self.sections.count()):
            it = self.sections.item(i); key = it.data(Qt.UserRole)
            if key is None:
                raw = it.data(Qt.UserRole + 1) or ""
                it.setText(self.t(raw).upper())
            elif key == "_manifest":
                it.setText("  " + self.t("manifest"))
            else:
                it.setText("  " + self.t(SCHEMA["sections"][key]["label"]))

    # ---- helpers ----
    def section_key(self):
        item = self.sections.currentItem()
        key = item.data(Qt.UserRole) if item else None
        return None if key in (None, "_manifest") else key

    def data_key(self, sec_key):
        """Resolve the JSON key actually used by this pack for a section (placeables/structures alias furniture)."""
        sec = SCHEMA["sections"].get(sec_key, {})
        for alias in [sec_key] + list(sec.get("aliases", [])):
            if alias in self.pack:
                return alias
        return sec_key

    def section_list(self, sec_key):
        return self.pack.get(self.data_key(sec_key), [])

    def on_section(self, row):
        item = self.sections.item(row) if row >= 0 else None
        if item is None or item.data(Qt.UserRole) is None:
            return
        self.cur_section = self.section_key()
        is_manifest = self.cur_section is None
        self.add_btn.setVisible(not is_manifest); self.del_btn.setVisible(not is_manifest)
        self.entries.clear()
        if is_manifest:
            self.entries.setVisible(False)
            self.show_form(self.manifest_fields(), self.pack, self.t("manifest"))
            return
        sec = SCHEMA["sections"][self.cur_section]
        title = self.t(sec["label"])
        if sec.get("dict"):
            # the whole section is one key -> value map (sounds, icons, strings, playerAnimations, ...)
            self.entries.setVisible(False)
            self.add_btn.setVisible(False); self.del_btn.setVisible(False)
            field = {"key": self.cur_section, "type": "dict",
                     "valueType": sec.get("valueType", "string"), "keySource": sec.get("keySource"),
                     "fields": sec.get("fields"), "help": sec.get("help")}
            self.show_form([field], self.pack, title, sec.get("help"))
            return
        if sec.get("singleton"):
            # one object per pack (e.g. playerSkins) — no entry list, edit the object directly
            self.entries.setVisible(False)
            self.add_btn.setVisible(False); self.del_btn.setVisible(False)
            self.show_form(sec["fields"], self.pack.setdefault(self.cur_section, {}), title, sec.get("help"))
            return
        self.entries.setVisible(True)
        idf = sec["idField"]
        for e in self.section_list(self.cur_section):
            self.entries.addItem(str(e.get(idf, "(new)")))
        if self.entries.count():
            self.entries.setCurrentRow(0)
        else:
            self.scroll.setWidget(QLabel(self.t("noentries")))
        self.update_status()

    def on_entry(self, idx):
        if self.cur_section is None or idx < 0:
            return
        lst = self.section_list(self.cur_section)
        if 0 <= idx < len(lst):
            sec = SCHEMA["sections"][self.cur_section]
            ident = lst[idx].get(sec["idField"]) if sec.get("idField") else None
            title = self.t(sec["label"]) + (f"  ·  {ident}" if ident else "")
            self.show_form(sec["fields"], lst[idx], title, sec.get("help"))

    def manifest_fields(self):
        return SCHEMA["manifest"]

    def base_dir(self):
        return os.path.dirname(self.path) if self.path else None

    def source_values(self, name):
        """Resolve a source -> list of known ids: a reference/*.txt catalog + inline list + ids defined in
        the current pack (intra-pack refs, incl. dict-section keys with an optional prefix)."""
        spec = SCHEMA.get("sources", {}).get(name, {})
        vals = []
        if "file" in spec:
            if name not in self._src_cache:
                ids = []
                try:
                    for l in open(data_path(spec["file"]), encoding="utf-8"):
                        l = l.strip()
                        if not l or l.startswith("#"):
                            continue
                        l = l.split("|")[0].strip()       # 'id | name | effect' catalogs
                        tok = l.split()
                        if tok:
                            ids.append(tok[0])            # 'id   extra columns/comment' catalogs
                except Exception:
                    pass
                self._src_cache[name] = ids
            vals += self._src_cache[name]
        vals += spec.get("list", [])
        if "pack" in spec:
            sec = spec["pack"]; idf = SCHEMA["sections"].get(sec, {}).get("idField", "id")
            vals += [e.get(idf) for e in self.section_list(sec) if isinstance(e, dict) and e.get(idf)]
        if "packDict" in spec:
            ns = (self.pack.get("namespace") or self.pack.get("id") or "").strip()
            prefix = (spec.get("keyPrefix") or "").replace("{ns}", ns)
            vals += [prefix + k for k in (self.pack.get(spec["packDict"]) or {})]
        seen, out = set(), []
        for v in vals:
            if v and v not in seen:
                seen.add(v); out.append(v)
        return out

    def show_form(self, fields, data, title="", subtitle=None):
        host = QWidget(); v = QVBoxLayout(host)
        if title:
            tl = QLabel(title); tl.setObjectName("formTitle"); v.addWidget(tl)
        if subtitle:
            st = QLabel(subtitle); st.setObjectName("formSub"); st.setWordWrap(True); v.addWidget(st)
        formhost = QWidget(); form = QFormLayout(formhost)
        form.setVerticalSpacing(6); form.setHorizontalSpacing(12)
        for f in fields:
            w = make_editor(f, data, self.refresh_titles, self)
            add_form_row(form, normalize(f), w, self)
        v.addWidget(formhost); v.addStretch(1)
        self.scroll.setWidget(host)

    def refresh_titles(self):
        self.update_status()
        if self.cur_section is None:
            return
        sec = SCHEMA["sections"][self.cur_section]
        if sec.get("singleton") or sec.get("dict"):
            return
        idf = sec["idField"]
        lst = self.section_list(self.cur_section)
        for i, e in enumerate(lst):
            if i < self.entries.count():
                self.entries.item(i).setText(str(e.get(idf, "(new)")))

    def update_status(self):
        pid = self.pack.get("id") or "(no id)"
        n_sections = n_entries = 0
        for key, sec in SCHEMA["sections"].items():
            data = self.pack.get(self.data_key(key))
            if isinstance(data, list) and data:
                n_sections += 1; n_entries += len(data)
            elif isinstance(data, dict) and data:
                n_sections += 1; n_entries += len(data) if sec.get("dict") else 1
        fname = os.path.basename(self.path) if self.path else "unsaved"
        self.status_lbl.setText(f"{pid}  ·  {fname}  ·  {n_sections} section(s), {n_entries} entr(ies)")

    # ---- entry add/remove ----
    def add_entry(self):
        if self.cur_section is None:
            return
        self.pack.setdefault(self.data_key(self.cur_section), []).append({})
        self.entries.addItem("(new)"); self.entries.setCurrentRow(self.entries.count() - 1)
        self.update_status()

    def del_entry(self):
        idx = self.entries.currentRow()
        if self.cur_section is None or idx < 0:
            return
        del self.pack[self.data_key(self.cur_section)][idx]
        self.entries.takeItem(idx)
        self.update_status()

    # ---- file ops ----
    def new(self):
        self.path = None; self.pack = {"id": "mypack", "namespace": "mymod", "version": "1.0.0"}
        self._select_first_section(); self.on_section(self.sections.currentRow())
        self.results.setVisible(False); self.update_status()

    def open(self):
        fn, _ = QFileDialog.getOpenFileName(self, "Open pack", HERE, "JSON (*.json)")
        if not fn:
            return
        try:
            self.pack = json.load(open(fn, encoding="utf-8"))
            self.path = fn
            self._select_first_section(); self.on_section(self.sections.currentRow())
            self.results.setVisible(False); self.update_status()
        except Exception as e:
            QMessageBox.critical(self, "Open failed", str(e))

    def save(self):
        if not self.path:
            return self.save_as()
        for k, sec in SCHEMA["sections"].items():
            if (sec.get("singleton") or sec.get("dict")) and self.pack.get(k) == {}:
                del self.pack[k]  # visiting the section shouldn't leave an empty block behind
        json.dump(self.pack, open(self.path, "w", encoding="utf-8"), indent=2)
        self.statusBar().showMessage(f"Saved {self.path}", 4000)
        self.update_status()

    def save_as(self):
        fn, _ = QFileDialog.getSaveFileName(self, "Save pack", os.path.join(HERE, "pack.json"), "JSON (*.json)")
        if fn:
            self.path = fn; self.save()

    # ---- validate (in-process, pure Python; results land in the inline panel) ----
    def validate(self):
        try:
            import validate as v
            errs, warns = v.validate_pack_data(self.pack, self.base_dir() or HERE)
        except Exception as e:
            QMessageBox.critical(self, "Validate failed", str(e))
            return
        self.results.clear()
        if not errs and not warns:
            it = QListWidgetItem("✔  " + self.t("looksgood"))
            it.setForeground(QColor(C_OK))
            self.results.addItem(it)
        for msg in errs:
            it = QListWidgetItem("✖  ERROR: " + msg)
            it.setForeground(QColor(C_ERR)); it.setData(Qt.UserRole, msg)
            self.results.addItem(it)
        for msg in warns:
            it = QListWidgetItem("⚠  warn: " + msg)
            it.setForeground(QColor(C_WARN)); it.setData(Qt.UserRole, msg)
            self.results.addItem(it)
        self.results.setVisible(True)
        self.statusBar().showMessage(f"Validate: {len(errs)} error(s), {len(warns)} warning(s)", 6000)

    def _jump_to_result(self, item):
        msg = item.data(Qt.UserRole)
        if not msg:
            return
        kind = re.match(r"\s*([A-Za-z]+)", msg)
        sec = KIND2SECTION.get((kind.group(1).lower() if kind else ""), None)
        if not sec:
            return
        for i in range(self.sections.count()):
            if self.sections.item(i).data(Qt.UserRole) == sec:
                self.sections.setCurrentRow(i)
                break
        m = re.search(r"'([^']+)'", msg)
        if m and self.entries.isVisible():
            for i in range(self.entries.count()):
                if self.entries.item(i).text() == m.group(1):
                    self.entries.setCurrentRow(i)
                    break


def main():
    app = QApplication(sys.argv)
    app.setStyleSheet(QSS)
    app.setWindowIcon(app_icon())
    w = Editor(); w.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
