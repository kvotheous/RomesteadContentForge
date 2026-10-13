# Nexus changelog entries (paste-ready, one line per change)

Full versions: mod [../CHANGELOG.md](../CHANGELOG.md) · authoring kit
[../docs/AUTHORING_CHANGELOG.md](../docs/AUTHORING_CHANGELOG.md) · editor
[../editor/CHANGELOG.md](../editor/CHANGELOG.md) · server build [../Server/CHANGELOG.md](../Server/CHANGELOG.md).

## Mod — 1.1.0
New: dedicated server support — a separate server build (RomesteadContentForge-Server zip) runs Content Forge on a headless Romestead Dedicated Server. Setup steps on the mod page.
Fixed: the co-op handshake could report a modded player as "has no Content Forge" on a dedicated server.
Fixed: hunt contests counted no kills on a dedicated server and always ended with no victor.
The ForgeGiveItem / ForgeTakeItem / ForgeHeal dialogue commands still need a player host.
Changed: the default icon is now a plain scroll instead of the game's "sam" placeholder photo.
Validators warn when a recipe ingredient is a construction resource (resource:...), which can't be crafted with.

## Server build — 1.1.0
New: first release of the dedicated-server build (RomesteadContentForge-Server zip). It runs Content Forge on a headless Romestead Dedicated Server: items, spells, recipes, creatures + AI, NPCs, maps and portals, events and arenas, crafting stations, placeables.
New: start-modded-server.bat starts the server with BepInEx loaded (use it instead of Steam's Play button).
The server DLL stays inactive if it's installed next to the game client by mistake.
Fixed: the co-op handshake could report a modded player as "has no Content Forge".
Fixed: hunt contests counted no kills and always ended with no victor.
The ForgeGiveItem / ForgeTakeItem / ForgeHeal dialogue commands still need a player host.

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

## Authoring kit — 1.1.0
README documents dedicated servers and the new server zip.
Default icon in the docs, examples and editor: scroll:brown (was the sam placeholder photo).
Test pack: T65 recipe fixed (wood stick instead of a construction resource).
Validators warn about resource:... recipe ingredients.

## Authoring kit — 1.0.1
PACKS.md documents "feetRow", "sprite-scale" and "sprite-offset-y".
Editor source now includes labels.json, the app icon and requirements.txt.
cfvalidate builds again and no longer warns about stations declared by the pack's own placeables.
Test pack: art derived from vanilla sprites removed (T37 rotter is now a color tint, T42 hair sample removed).
Added the MIT LICENSE file and THIRD-PARTY-NOTICES.txt.

## Authoring kit — 1.0.0
Initial creator kit: docs, editor source, 3 example packs, 17 id reference lists, art/asset tools.

## Forge Editor — 1.1.0
New pack elements default to the scroll icon (was the sam placeholder photo).
Validation warns about resource:... recipe ingredients.

## Forge Editor — 1.0.1
Knows the new from-scratch spac field "feetRow".
Rebuilt against the 1.0.1 schema.
Now a zip: ForgeEditor.exe + LICENSE.txt + THIRD-PARTY-NOTICES.txt.

## Forge Editor — 1.0.0
Initial release: visual pack editor with id dropdowns, PNG previews, per-language text and one-click validation.
