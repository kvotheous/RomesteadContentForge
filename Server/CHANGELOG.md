# Changelog — Romestead Content Forge (Dedicated Server build)

The dedicated-server build, `RomesteadContentForge.Server.dll`, shipped as `RomesteadContentForge-Server-<version>.zip`.
It shares its version number with the mod. Most mod changes also apply here; this file lists only what matters to
server hosts. Full mod changelog: [../CHANGELOG.md](../CHANGELOG.md).

## 1.1.0
First release of the server build.

- **New: Content Forge on a headless Romestead Dedicated Server.** Runs items, spells, recipes, creatures + AI,
  NPCs, maps and portals, events and arenas, crafting stations and placeables. It's the same code as the mod,
  without the parts that only run in the game client (visuals, UI, sounds).
- **New: `start-modded-server.bat`.** It loads BepInEx on the server through .NET's startup hook, because the
  loader's `d3d11.dll` hook only works in the game client. Put it next to `Server.exe` and start the server with it,
  not with Steam's Play button.
- **Safe next to the game client.** If the server DLL is installed in a normal game folder by mistake, it stays
  inactive.
- **Fixed: the co-op handshake mixed up players on a dedicated server.** It used the network connection number as
  the player id, and on a dedicated server those are different numbers. A modded player could be reported as
  "has no Content Forge". It now looks up the real player.
- **Fixed: hunt contests counted no kills.** On a dedicated server the server simulates creature deaths itself and
  reports them without a killer, so every `huntContest` ended with "no victor". Kills are now credited to the
  player who landed the killing blow, and the event reward is handed out.
- **Host-only:** the dialogue commands `ForgeGiveItem` / `ForgeTakeItem` / `ForgeHeal` still need a player host.
  Dialogue itself still works on a dedicated server.

Setup steps: *Dedicated servers* in [../nexus/README_NEXUS.md](../nexus/README_NEXUS.md#dedicated-servers).
