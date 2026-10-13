# Changelog — Content Forge Editor (`ForgeEditor.exe`)

The editor's version follows the mod release it ships with.

## 1.1.0
- New pack elements default to the vanilla `scroll:brown` icon (was `sam`, the game's placeholder photo).
- Validation warns when a recipe ingredient is a construction resource (`resource:...`) — those can't be crafted with.

## 1.0.1
- Knows the new from-scratch spac field **`feetRow`** (pixel row where the art's feet touch the ground).
- `ForgeEditor.exe` rebuilt against the 1.0.1 schema.
- Now downloaded as `ForgeEditor-1.0.1.zip`: the exe plus `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` (Python, PySide6/Qt).
- README: doc links point at `docs/PACKS.md`; event types section renamed to "competitive event types".

## 1.0.0
- Initial release: a visual editor for content packs. Forms built from `schema.json`, dropdowns for every
  game id (from the `reference/` catalogs), PNG previews, per-language text, Yarn dialogue helpers and
  one-click validation. Writes the same `.json` packs the mod reads. Standalone `ForgeEditor.exe` for Windows
  (no Python needed) or run `forge_editor.py` with Python + PySide6.
