# Changelog — Content Forge Authoring kit

What changed in `RomesteadContentForge-Authoring-<version>.zip` (docs, example packs, editor source, reference
lists, tools). Mod changes are in [../CHANGELOG.md](../CHANGELOG.md); editor changes in
[../editor/CHANGELOG.md](../editor/CHANGELOG.md).

## 1.1.0
- README documents dedicated servers (the new `RomesteadContentForge-Server` zip).
- Default icon is now `scroll:brown` everywhere (docs, examples, editor) — `sam` is the game's placeholder photo.
- Test pack: the T65 recipe takes a wood stick (`resource:stone` is a construction resource, not an item).
- Both validators warn about `resource:` recipe ingredients.

## 1.0.1
- Docs: `PACKS.md` documents the spac field `feetRow` and the `forge_creature_anim` params `sprite-scale` and
  `sprite-offset-y`.
- Editor source now includes `labels.json` (friendly field names), the app icon and `requirements.txt`.
- `cfvalidate` (C# validator) builds again and no longer warns about stations declared by the pack's own
  placeables.
- Test pack: art derived from vanilla sprites removed — the T37 rotter is now a color tint and the T42 hair
  sample is gone; `TESTING.md` / `TESTPLAN.md` updated to match.
- Added the MIT `LICENSE` file and `THIRD-PARTY-NOTICES.txt`.

## 1.0.0
- Initial creator kit: full docs (field reference, cookbook, getting started, advanced), the GUI editor
  source, 3 example packs (worldoftales, necromancer, full test pack), 17 id reference lists, and art/asset
  tools.
