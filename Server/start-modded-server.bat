@echo off
rem Starts the Romestead dedicated server WITH BepInEx mods (Kvotheous Dedicated Server Kit, Content Forge, ...).
rem The Romestead BepInEx Mod Loader hooks the game CLIENT through d3d11.dll; a dedicated server has no
rem graphics, so it is loaded here through .NET's standard startup hook instead. Put this file next to
rem Server.exe and start the server with it (not with Steam's Play button).
cd /d "%~dp0"
if not exist "%~dp0BepInEx.NET.CoreCLR.dll" (
  echo BepInEx.NET.CoreCLR.dll not found next to Server.exe - extract the Romestead BepInEx Mod Loader here first.
  pause
  exit /b 1
)
set DOTNET_STARTUP_HOOKS=%~dp0BepInEx.NET.CoreCLR.dll
"%~dp0Server.exe" %*
pause
