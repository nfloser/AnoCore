import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


spec = importlib.util.spec_from_file_location("profile", Path(__file__).resolve().parents[2] / "tools/prepare_anomeme_profile.py")
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)


class ProfilePreparationTests(unittest.TestCase):
    def test_new_profile_stamps_stable_start_and_refuses_existing_output(self):
        with tempfile.TemporaryDirectory() as root:
            output = Path(root) / "prepared"
            profile.prepare(output)
            start = json.loads((output / "gameplay-xp.json").read_text())["EarnFromUtc"]
            self.assertTrue(start.endswith("+00:00"))
            before = (output / "gameplay-xp.json").read_bytes()
            with self.assertRaises(ValueError):
                profile.prepare(output)
            self.assertEqual(before, (output / "gameplay-xp.json").read_bytes())

    def test_existing_policy_and_start_preserved_and_definitions_added_without_replacing_ids(self):
        with tempfile.TemporaryDirectory() as root:
            old = Path(root) / "old"
            old.mkdir()
            xp = {"earnfromutc": "2026-01-01T00:00:00+00:00", "KillXp": 7, "GameplayXp": {"FirstBlood": 99}}
            (old / "gameplay-xp.json").write_text(json.dumps(xp))
            canonical = {"Levels": [{"Level": 1, "MinimumXp": 0}], "Boosts": []}
            (old / "progression.json").write_text(json.dumps(canonical))
            achievements = {"Achievements": [{"Id": "anomeme.headshots", "Name": "Keep this name"}]}
            (old / "achievements.json").write_text(json.dumps(achievements))
            before = {p.name: p.read_bytes() for p in old.iterdir()}
            output = Path(root) / "prepared"
            profile.prepare(output, old)
            result = json.loads((output / "gameplay-xp.json").read_text())
            self.assertEqual(xp["earnfromutc"], result["EarnFromUtc"])
            self.assertEqual(7, result["KillXp"])
            self.assertEqual(99, result["GameplayXp"]["FirstBlood"])
            self.assertEqual(canonical, json.loads((output / "progression.json").read_text()))
            merged = json.loads((output / "achievements.json").read_text())["Achievements"]
            self.assertEqual("Keep this name", next(a for a in merged if a["Id"] == "anomeme.headshots")["Name"])
            self.assertEqual(before, {p.name: p.read_bytes() for p in old.iterdir()})

    def test_legacy_curve_adopted_and_bad_start_does_not_create_output(self):
        with tempfile.TemporaryDirectory() as root:
            old = Path(root) / "old"
            old.mkdir()
            legacy = {"Levels": [{"Level": 1, "MinimumXp": 0}, {"Level": 2, "MinimumXp": 1234}], "Boosts": [], "Achievements": []}
            (old / "achievements.json").write_text(json.dumps(legacy))
            output = Path(root) / "prepared"
            profile.prepare(output, old)
            self.assertEqual(1234, json.loads((output / "progression.json").read_text())["Levels"][1]["MinimumXp"])
            (old / "gameplay-xp.json").write_text('{"KillXp":7}')
            rejected = Path(root) / "rejected"
            with self.assertRaises(ValueError):
                profile.prepare(rejected, old)
            self.assertFalse(rejected.exists())


if __name__ == "__main__":
    unittest.main()
