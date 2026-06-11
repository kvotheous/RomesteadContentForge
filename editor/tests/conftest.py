"""Shared test setup: import paths + headless Qt."""
import os
import sys

TESTS_DIR = os.path.dirname(os.path.abspath(__file__))
EDITOR_DIR = os.path.dirname(TESTS_DIR)
ROOT_DIR = os.path.dirname(EDITOR_DIR)

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
if EDITOR_DIR not in sys.path:
    sys.path.insert(0, EDITOR_DIR)
