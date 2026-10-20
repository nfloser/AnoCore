"""Guard the tournament playoff schema repair after incomplete upgrades."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PERSISTENCE = ROOT / "src" / "AnoCore.Modules.Tournament.Persistence"


class TournamentPlayoffMigrationTests(unittest.TestCase):
    def test_migration_23_is_cross_engine_and_recovers_partial_schema(self):
        text = (PERSISTENCE / "TournamentPlayoffSchemaMigration023.cs").read_text()
        self.assertIn("information_schema.COLUMNS", text)
        self.assertIn("ADD COLUMN playoff_results_json LONGTEXT NULL", text)
        self.assertIn("SET playoff_results_json = '[]'", text)
        self.assertIn("MODIFY COLUMN playoff_results_json LONGTEXT NOT NULL", text)
        self.assertNotIn("ADD COLUMN IF NOT EXISTS", text.replace(
            "// MySQL does not universally support ADD COLUMN IF NOT EXISTS.", ""))

    def test_repair_runs_even_if_version_23_is_marked_applied(self):
        repair = (PERSISTENCE / "TournamentPlayoffSchemaRepairMigration027.cs").read_text()
        bootstrap = (PERSISTENCE / "TournamentPersistenceBootstrap.cs").read_text()
        self.assertIn("public long Version => 27;", repair)
        self.assertIn("new TournamentPlayoffSchemaMigration023().ApplyAsync", repair)
        self.assertIn("new TournamentPlayoffSchemaRepairMigration027()", bootstrap)


if __name__ == "__main__":
    unittest.main()
