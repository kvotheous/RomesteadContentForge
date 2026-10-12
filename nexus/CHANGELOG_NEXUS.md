# Nexus changelog entries (paste-ready, one line per change)

Full versions: mod [../CHANGELOG.md](../CHANGELOG.md) · authoring kit
[../docs/AUTHORING_CHANGELOG.md](../docs/AUTHORING_CHANGELOG.md) · editor
[../editor/CHANGELOG.md](../editor/CHANGELOG.md).

## Mod — 1.0.1
Compatible with the Romestead 2026-10-04 update (1.0.0 is not).
Interactive placeables: holding the interact key (vanilla pickup/demolish) no longer triggers their action.
Each patch is applied separately: if a future game update breaks one feature, only that feature turns off and the rest keeps working.
Summons no longer walk through walls (they spawned at spell-cast height). Old saves are fixed on load.
Companion teleport never lands inside walls or ruins and no longer copies the player's height mid-jump.
From-scratch creature sprites stand upright on the ground line. New spac field "feetRow"; new forge_creature_anim params "sprite-scale" and "sprite-offset-y".
Portal sounds now also play for the player who travels.
Dialogue placeables: an invalid "dialogue-distance" falls back to the default instead of closing the window.
Every download now includes THIRD-PARTY-NOTICES.txt (licenses of the bundled open-source libraries).

## Mod — 1.0.0
Initial release: content packs for items, weapons, spells, creatures, NPCs, maps, events, crafting stations, quests and more.

## Authoring kit — 1.0.1
PACKS.md documents "feetRow", "sprite-scale" and "sprite-offset-y".
Editor source now includes labels.json, the app icon and requirements.txt.
cfvalidate builds again and no longer warns about stations declared by the pack's own placeables.
Test pack: art derived from vanilla sprites removed (T37 rotter is now a color tint, T42 hair sample removed).
Added the MIT LICENSE file and THIRD-PARTY-NOTICES.txt.

## Authoring kit — 1.0.0
Initial creator kit: docs, editor source, 3 example packs, 17 id reference lists, art/asset tools.

## Forge Editor — 1.0.1
Knows the new from-scratch spac field "feetRow".
Rebuilt against the 1.0.1 schema.
Now a zip: ForgeEditor.exe + LICENSE.txt + THIRD-PARTY-NOTICES.txt.

## Forge Editor — 1.0.0
Initial release: visual pack editor with id dropdowns, PNG previews, per-language text and one-click validation.
