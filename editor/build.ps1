<#
.SYNOPSIS
  Build the Content Forge Editor into a double-clickable app.

.DESCRIPTION
  IMPORTANT: a binary can only be built FOR the OS it is built ON (PyInstaller can't cross-compile; macOS
  builds also require a Mac per Apple's licensing). So:
    - Local OS  -> always built natively here.
    - Linux     -> optionally built via Docker (-Linux), from any host that has Docker.
    - macOS     -> needs a Mac. Use the GitHub Actions workflow (.github/workflows/build-editor.yml) to build
                   Windows + macOS + Linux automatically in one run.

  Runs under Windows PowerShell or cross-platform PowerShell (pwsh) on Mac/Linux.

.PARAMETER Linux
  Also build a Linux binary in a python:3.12 Docker container (needs Docker installed).

.EXAMPLE
  .\build.ps1            # build for this OS
  .\build.ps1 -Linux     # build for this OS + a Linux binary via Docker
#>
param([switch]$Linux)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Build-Local {
    Write-Host "==> Building for local OS..." -ForegroundColor Cyan
    python -m pip install --quiet --upgrade pip PySide6 pyinstaller
    python (Join-Path $here 'build.py')
}

function Build-LinuxDocker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Warning "Docker not found - skipping the Linux build. Install Docker, or use the CI workflow."
        return
    }
    Write-Host "==> Building Linux binary via Docker..." -ForegroundColor Cyan
    # Mount the repo so the container can read editor/ + ../reference; output lands back in editor/dist.
    $repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path
    $cmd = 'apt-get update -qq && apt-get install -y -qq libegl1 libxkbcommon0 libdbus-1-3 >/dev/null 2>&1; ' +
           'pip install --quiet PySide6 pyinstaller; ' +
           'cd /src/RomesteadContentForge/editor && python build.py'
    docker run --rm -v "${repoRoot}:/src" python:3.12-slim bash -c $cmd
    Write-Host "Linux binary -> editor/dist/ForgeEditor" -ForegroundColor Green
}

Build-Local
if ($Linux) { Build-LinuxDocker }

Write-Host ""
Write-Host "Done. Artifacts in: $(Join-Path $here 'dist')" -ForegroundColor Green
Write-Host "macOS: build on a Mac, or run .github/workflows/build-editor.yml (CI) to produce all three at once." -ForegroundColor Yellow
