"""validate.py: the shipped example packs stay clean, and broken packs produce the SPECIFIC error/warning."""
import os

import pytest

import validate
from conftest import ROOT_DIR

EXAMPLES = [
    os.path.join(ROOT_DIR, "examples", "test", "test.json"),
    os.path.join(ROOT_DIR, "examples", "worldoftales", "worldoftales.json"),
    os.path.join(ROOT_DIR, "examples", "necromancer", "necromancer.json"),
]


@pytest.mark.parametrize("path", EXAMPLES, ids=[os.path.basename(p) for p in EXAMPLES])
def test_example_pack_has_zero_errors(path):
    assert os.path.isfile(path), f"example pack missing: {path}"
    errors, _warnings = validate.validate_pack(path)
    assert errors == [], f"{os.path.basename(path)} should validate clean, got: {errors}"


# ---- synthetic bad packs: each must trip its SPECIFIC message ----
BASE = {"id": "mymod", "namespace": "mymod", "version": "1.0.0"}

BAD_CASES = [
    # (case id, pack fragment, expected substring, kind: "error" | "warn")
    ("item_missing_id",
     {"items": [{"name": "No Id"}]},
     "item with empty id", "error"),
    ("item_bad_equipment_type",
     {"items": [{"id": "mymod:x", "equippable": {"equipmentType": "Hat"}}]},
     "invalid equipmentType", "error"),
    ("item_unnamespaced_id",
     {"items": [{"id": "other:thing"}]},
     "is not namespaced", "warn"),
    ("recipe_bad_station",
     {"recipes": [{"id": "mymod:r", "crafted": "mymod:x", "station": "nonsense-station"}]},
     "unknown station", "warn"),
    ("placeable_station_needs_clone",
     {"furniture": [{"id": "mymod:f", "station": "mymod:forge_tab"}]},
     "'station' requires 'cloneEntity'", "error"),
    ("favour_missing_parent",
     {"favours": [{"id": "mymod:fav", "position": [0, 10]}]},
     "has no 'parent'", "error"),
    ("favour_bad_position",
     {"favours": [{"id": "mymod:fav", "parent": "minerva_favour:lumber_axe_power"}]},
     "needs 'position'", "error"),
    ("furniture_sprite_file_missing",
     {"furniture": [{"id": "mymod:f", "cloneEntity": "WorkbenchGuid", "sprite": "missing.png"}]},
     "sprite 'missing.png' not found", "error"),
    ("arena_map_not_pvp",
     {"maps": [{"id": "mymod:m", "tmx": "nope.tmx"}],
      "arenas": [{"id": "mymod:a", "map": "mymod:m"}]},
     "not flagged \"pvp\": true", "error"),
    ("arena_map_unknown",
     {"arenas": [{"id": "mymod:a", "map": "mymod:nowhere"}]},
     "is not a map in this pack", "error"),
    ("drop_chance_over_100",
     {"drops": [{"table": "enemy:fallen", "item": "mymod:x", "chance": 150}]},
     "outside 0..100", "warn"),
    ("duplicate_spell_id",
     {"spells": [{"id": "mymod:zap"}, {"id": "mymod:zap"}]},
     "duplicate spell id", "error"),
    ("spell_two_behavior_blocks",
     {"spells": [{"id": "mymod:s", "aoe": {"radius": 48}, "explosion": {"radius": 32}}]},
     "more than one behavior block", "error"),
    ("crop_stage_count_mismatch",
     {"crops": [{"id": "mymod:c", "stageCount": 4, "seed": {"id": "mymod:seed"},
                 "display": {"sheet": "media/sprites/flora/wheat_animated",
                             "stages": [{"frames": [0]}, {"frames": [1]}]}}]},
     "MUST match", "error"),
    ("npc_missing_yarn",
     {"maps": [{"id": "mymod:m", "tmx": "nope.tmx"}],
      "npcs": [{"id": "mymod:n", "map": "mymod:m", "position": [10, 10]}]},
     "has no 'yarn'", "error"),
    ("map_door_on_exit_edge",
     {"maps": [{"id": "mymod:m", "tmx": "nope.tmx", "exitDirection": 1,
                "doors": [{"edge": 1, "target": "mymod:m2"}]}]},
     "same edge as exitDirection", "error"),
    ("sound_file_missing",
     {"sounds": {"ding": "ding.wav"}},
     "not found next to the pack", "error"),
    ("event_missing_type",
     {"events": [{"id": "mymod:e"}]},
     "has no 'type'", "error"),
    ("event_unknown_type",
     {"events": [{"id": "mymod:e", "type": "discoParty"}]},
     "unknown type", "error"),
    ("event_koth_missing_point",
     {"maps": [{"id": "mymod:m", "tmx": "nope.tmx"}],
      "events": [{"id": "mymod:e", "type": "koth", "map": "mymod:m"}]},
     "koth needs 'point'", "error"),
    ("event_race_missing_checkpoints",
     {"maps": [{"id": "mymod:m", "tmx": "nope.tmx"}],
      "events": [{"id": "mymod:e", "type": "race", "map": "mymod:m"}]},
     "race needs 'checkpoints'", "error"),
    ("townbuilding_missing_base",
     {"townBuildings": [{"id": "mymod:bakery"}]},
     "has no 'base'", "error"),
    ("workrecipe_missing_job",
     {"workRecipes": [{"id": "recipe:mymod:bread", "output": "food:bread"}]},
     "has no 'job'", "error"),
    ("spac_scratch_missing_animations",
     {"spacs": [{"id": "mymod:anim", "sheets": {}, "frameWidth": 32, "frameHeight": 32}]},
     "needs 'animations'", "error"),
    ("shopaddition_missing_shop",
     {"shopAdditions": [{"items": ["mymod:x"]}]},
     "has no 'shop'", "error"),
]


@pytest.mark.parametrize("name,fragment,expected,kind", BAD_CASES, ids=[c[0] for c in BAD_CASES])
def test_bad_pack_reports(name, fragment, expected, kind, tmp_path):
    pack = dict(BASE)
    pack.update(fragment)
    errors, warnings = validate.validate_pack_data(pack, str(tmp_path))
    haystack = errors if kind == "error" else warnings
    assert any(expected in m for m in haystack), (
        f"expected {kind} containing {expected!r}; errors={errors} warnings={warnings}")


def test_clean_minimal_pack_passes(tmp_path):
    pack = dict(BASE)
    pack["items"] = [{"id": "mymod:gem", "name": "Gem", "maxStack": 10}]
    errors, warnings = validate.validate_pack_data(pack, str(tmp_path))
    assert errors == [] and warnings == []
