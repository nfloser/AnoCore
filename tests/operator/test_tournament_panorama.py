"""Contract checks for the universal, server-data-driven tournament Workshop UI."""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LAYOUT = ROOT / "ui/AnoCore/layout/custom_game/anocore/ano_tournament.xml"
STYLE = ROOT / "ui/AnoCore/styles/custom_game/anocore/ano_tournament.css"
BUILD = ROOT / "ui/AnoCore/build.ps1"


class TournamentPanoramaContractTests(unittest.TestCase):
    def setUp(self):
        self.root = ET.parse(LAYOUT).getroot()
        self.ids = [node.attrib["id"] for node in self.root.iter()
                    if "id" in node.attrib]

    def test_native_xml_shape_and_known_panels(self):
        self.assertEqual("root", self.root.tag)
        self.assertEqual(1, len(self.root.findall("Panel")))
        self.assertEqual(len(self.ids), len(set(self.ids)), "Panel IDs must be unique")
        allowed = {"root": set(), "styles": set(), "include": {"src"},
                   "Panel": {"id", "class", "hittest"},
                   "Button": {"id", "class"},
                   "Label": {"id", "class", "text"}}
        for element in self.root.iter():
            self.assertIn(element.tag, allowed)
            self.assertFalse(set(element.attrib) - allowed[element.tag],
                             (element.tag, element.attrib))
        self.assertTrue(self.root.find(".//Panel[@id='ano_tournament_root']") is not None)

    def test_stable_dynamic_slots_cover_up_to_ten_teams_and_all_tabs(self):
        required = {"ano_tournament_root", "ano_tournament_title",
                    "ano_tournament_status", "ano_tournament_close",
                    "ano_tournament_prev", "ano_tournament_next",
                    "ano_tournament_page", "ano_tournament_heading",
                    "ano_tournament_description", "ano_tournament_team_name",
                    "ano_tournament_team_status"}
        for tab in ("overview", "table", "bracket", "matches", "teams"):
            required.add("ano_tournament_tab_" + tab)
            required.add("ano_tournament_view_" + tab)
        for i in range(10):
            required.update({f"ano_tournament_table_{i}",
                             f"ano_tournament_table_{i}_name",
                             f"ano_tournament_table_{i}_stats"})
            required.update({f"ano_tournament_team_{i}",
                             f"ano_tournament_team_{i}_name"})
        for i in range(16):
            required.update({f"ano_tournament_match_{i}",
                             f"ano_tournament_match_{i}_text"})
        for i in range(7):
            required.add(f"ano_tournament_roster_{i}")
        for i in range(4):
            required.add(f"ano_tournament_summary_{i}")
        self.assertFalse(required - set(self.ids),
                         f"Missing stable server-facing IDs: {required - set(self.ids)}")

    def test_dashboard_is_opt_in_until_workshop_pack_is_installed(self):
        plugin = (ROOT / "src/AnoCore.Plugin/AnoCorePlugin.cs").read_text(
            encoding="utf-8")
        self.assertRegex(plugin, re.compile(
            r"public bool TournamentDashboardEnabled\\s*\\{ get; set; \\}"))
        self.assertIn("_tournamentDashboardEnabled = settings.TournamentDashboardEnabled;", plugin)
        self.assertIn("if (_tournamentDashboardEnabled && _panoramaMenusEnabled && _customHud is not null)", plugin)
        self.assertIn("TournamentDashboardEnabled requires PanoramaMenusEnabled", plugin)

    def test_empty_fixture_rows_can_be_hidden_by_the_server(self):
        presenter = (ROOT / "src/AnoCore.Modules.Tournament/TournamentPanoramaPresenter.cs").read_text(
            encoding="utf-8")
        css = STYLE.read_text(encoding="utf-8")
        self.assertIn('SetClass(player, HudId, $"ano_tournament_fixture_{i}", "hidden", !fixtureShow)', presenter)
        self.assertIn(".ano-fixture-label.hidden { visibility: collapse; }", css)

    def test_all_server_data_is_plain_text_and_part_of_one_compiled_pack(self):
        labels = [node for node in self.root.iter() if node.tag == "Label"
                  and "id" in node.attrib]
        self.assertTrue(labels)
        self.assertTrue(all(node.get("text") == "{s:text}" for node in labels))
        css = STYLE.read_text(encoding="utf-8")
        self.assertIn(".ano-tournament-overlay.shown", css)
        for tab in ("overview", "table", "bracket", "matches", "teams"):
            self.assertIn(f".ano-tournament-overlay.view-{tab}", css)
        xml = LAYOUT.read_text(encoding="utf-8")
        self.assertIn("ano_tournament.vcss_c", xml)
        self.assertIn('"ano_tournament"', BUILD.read_text(encoding="utf-8"))
        self.assertNotRegex(xml, re.compile(r"https?://|<script|<Image", re.I))


if __name__ == "__main__":
    unittest.main()
