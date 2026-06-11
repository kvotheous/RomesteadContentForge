#!/usr/bin/env python3
"""
Build a double-clickable Content Forge Editor for THIS operating system, via PyInstaller.

  pip install pyinstaller
  python build.py

Output: dist/ForgeEditor(.exe / .app / binary). Run it on each OS to get that OS's artifact — there is no single
cross-platform binary; the CI workflow (.github/workflows/build-editor.yml) builds Windows/macOS/Linux at once.
"""
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SEP = ";" if os.name == "nt" else ":"   # PyInstaller --add-data separator differs per OS


def add(src, dst):
    return ["--add-data", f"{src}{SEP}{dst}"]


def main():
    args = ["pyinstaller", "--noconfirm", "--onefile", "--windowed", "--name", "ForgeEditor",
            "--hidden-import", "validate"]
    args += add(os.path.join(HERE, "schema.json"), ".")
    args += add(os.path.join(HERE, "labels.json"), ".")
    if os.path.exists(os.path.join(HERE, "forge_icon.png")):                # runtime window icon
        args += add(os.path.join(HERE, "forge_icon.png"), ".")
    for f in glob.glob(os.path.join(HERE, "..", "reference", "*.txt")):   # id catalogs for the dropdowns
        args += add(f, "reference")
    for f in glob.glob(os.path.join(HERE, "lang", "*.json")):             # extra UI languages
        args += add(f, "lang")
    icon = os.path.join(HERE, "forge_icon.ico" if os.name == "nt" else "forge_icon.png")
    if os.path.exists(icon):
        args += ["--icon", icon]
    args.append(os.path.join(HERE, "forge_editor.py"))

    print("Running:", " ".join(args))
    subprocess.run(args, check=True, cwd=HERE)
    out = "ForgeEditor.exe" if os.name == "nt" else "ForgeEditor"
    print(f"\nDone -> {os.path.join(HERE, 'dist', out)}  (double-click to run; no Python needed)")


if __name__ == "__main__":
    main()
