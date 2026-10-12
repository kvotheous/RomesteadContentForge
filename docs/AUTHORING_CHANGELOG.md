# Changelog — Content Forge Authoring kit

What changed in `RomesteadContentForge-Authoring-<version>.zip` (docs, example packs, editor source, reference
lists, tools). Mod changes are in [../CHANGELOG.md](../CHANGELOG.md); editor changes in
[../editor/CHANGELOG.md](../editor/CHANGELOG.md).

## 1.0.1
- Docs: `PACKS.md` documents the spac field `feetRow` and the `forge_creature_anim` params `sprite-scale` and
  `sprite-offset-y`.
- Editor source now includes `labels.json` (friendly field names), the app icon and `requirements.txt`.
- `cfvalidate` (C# validator) builds again and no longer warns about stations declared by the pack's own
  placeables.
- Test pack: art derived from vanilla sprites removed — the T37 rotter is now a color tint and the T42 hair
  sample is gone; `TESTING.md` / `TESTPLAN.md` updated to match.
- Added the MIT `LICENSE` file.

## 1.0.0
- Initial creator kit: full docs (field reference, cookbook, getting started, advanced), the GUI editor
  source, 3 example packs (worldoftales, necromancer, full test pack), 17 id reference lists, and art/asset
  tools.
