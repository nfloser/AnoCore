"""Static contracts for the separate AnoVeto creation layout and Workshop builder."""
import pathlib
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "ui" / "AnoCore"
LAYOUT = ASSETS / "layout" / "custom_game" / "anocore" / "ano_veto_create.xml"
CSS = ASSETS / "styles" / "custom_game" / "anocore" / "ano_veto_create.css"


class AnoVetoCreateResourcesTest(unittest.TestCase):
    def test_veto_authorization_survives_startup_ownership_transfer(self):
        plugin = (ROOT / "src/AnoCore.Plugin/AnoCorePlugin.cs").read_text()
        self.assertIn("created = null;", plugin)
        self.assertIn("createdAnoVeto = null;", plugin)
        self.assertNotIn("() => ReferenceEquals(_runtime, created) && createdAnoVeto?.IsActive == true", plugin)
        self.assertIn("() => ReferenceEquals(_runtime, vetoRuntime) && _anoVeto?.IsActive == true", plugin)

    def test_button_labels_do_not_intercept_native_clicks(self):
        tree = ET.parse(LAYOUT)
        for button in tree.findall('.//Button'):
            for label in button.findall('.//Label'):
                self.assertEqual(label.attrib.get('hittest'), 'false', button.attrib['id'])

    def test_all_buttons_exist_once(self):
        tree = ET.parse(LAYOUT)
        ids = [element.attrib.get("id") for element in tree.iter() if element.tag == "Button"]
        self.assertEqual(
            {"ano_veto_create_close", "ano_veto_create_manual",
             "ano_veto_create_random", "ano_veto_create_saved",
             "ano_veto_create_back", "ano_veto_create_prev",
             "ano_veto_create_next", "ano_veto_create_confirm",
             "ano_veto_create_save", "ano_veto_create_duplicate",
             "ano_veto_create_delete",
             *(f"ano_veto_create_map_{i}" for i in range(24))},
            set(ids),
        )
        self.assertEqual(len(ids), len(set(ids)))
        self.assertIsNotNone(tree.find(".//*[@id='ano_veto_create_root']"))

    def test_stylesheet_and_compiler_manifest_match(self):
        contents = LAYOUT.read_text(encoding="utf-8")
        self.assertIn("ano_veto_create.vcss_c", contents)
        self.assertIn("ano_veto_create", (ASSETS / "build.ps1").read_text(encoding="utf-8"))
        self.assertIn(".ano-create-overlay.shown", CSS.read_text(encoding="utf-8"))

    def test_no_unverified_keyboard_search(self):
        contents = LAYOUT.read_text(encoding="utf-8")
        self.assertNotIn("TextEntry", contents)
        self.assertNotIn("TextInput", contents)


if __name__ == "__main__":
    unittest.main()
