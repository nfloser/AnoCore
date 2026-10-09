"""Exercise the actual PowerShell build with a fake compiler, not Valve rendering."""
import os
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")


class VetoLayoutTests(unittest.TestCase):
    def test_card_layout_is_loaded_with_previews_and_all_click_targets(self):
        controller = (ROOT / "src/AnoCore.Modules.AnoVeto/AnoVetoHudController.cs").read_text()
        self.assertIn('anocore/ano_veto_cards.xml"', controller)
        layout = ET.parse(ROOT / "ui/AnoCore/layout/custom_game/anocore/ano_veto_cards.xml").getroot()
        sources = [x.attrib["src"] for x in layout.findall("styles/include")]
        self.assertIn("s2r://panorama/styles/custom_game/anocore/ano_veto_cards.vcss_c", sources)
        self.assertIn("s2r://panorama/styles/custom_game/anocore/ano_veto_previews.vcss_c", sources)
        ids = {x.attrib["id"]: x for x in layout.iter() if "id" in x.attrib}
        self.assertIn("ano_veto_close", ids)
        for i in range(8):
            button = ids[f"ano_veto_map_{i}"]
            self.assertEqual("Button", button.tag)
            self.assertIsNotNone(button.find(f"Panel[@id='ano_veto_map_{i}_image']"))
            self.assertIsNotNone(button.find(f"Label[@id='ano_veto_map_{i}_text']"))


@unittest.skipUnless(PWSH, "PowerShell integration runs on the CI runner")
class VetoAddonBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.cs2 = Path(self.temp.name) / "CS2 with spaces"
        self.style = self.cs2 / "content/csgo_addons/anomeme_ui/panorama/styles/custom_game/anocore"
        self.style.mkdir(parents=True)
        compiler = self.cs2 / "game/bin/win64/resourcecompiler.exe"
        compiler.parent.mkdir(parents=True)
        compiler.write_text('''#!/usr/bin/env python3
import pathlib,sys
source=pathlib.Path(sys.argv[sys.argv.index('-i')+1])
parts=source.parts
index=parts.index('content')
target=pathlib.Path(*parts[:index])/'game'/pathlib.Path(*parts[index+1:])
ext={'.css':'.vcss_c','.xml':'.vxml_c','.vtex':'.vtex_c'}[source.suffix]
target=target.with_suffix(ext)
target.parent.mkdir(parents=True,exist_ok=True)
target.write_bytes(source.read_bytes())
''')
        compiler.chmod(0o755)

    def run_build(self, *args):
        return subprocess.run([PWSH, "-NoProfile", "-File", str(ROOT / "ui/AnoCore/build.ps1"),
                               "-Cs2", str(self.cs2), *args], capture_output=True, text=True, timeout=45)

    def add_existing_preview(self):
        css = '.ano_preview_w_123 { background-image: url("s2r://panorama/styles/custom_game/anocore/previews/ano_preview_w_123.vtex"); }\n'
        (self.style / "ano_veto_previews.css").write_text(css)
        previews = self.style / "previews"
        previews.mkdir()
        (previews / "ano_preview_w_123.vtex").write_text("fixture descriptor")
        (previews / "ano_preview_w_123.png").write_bytes(b"fixture image")
        return css

    def test_plain_rebuild_preserves_and_recompiles_existing_preview_mapping(self):
        css = self.add_existing_preview()
        result = self.run_build()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(css, (self.style / "ano_veto_previews.css").read_text())
        game = self.cs2 / "game/csgo_addons/anomeme_ui/panorama"
        self.assertTrue((game / "styles/custom_game/anocore/previews/ano_preview_w_123.vtex_c").is_file())
        self.assertTrue((game / "layout/custom_game/anocore/ano_veto_cards.vxml_c").is_file())

    def test_broken_existing_mapping_fails_without_silently_erasing_it(self):
        css = self.add_existing_preview()
        (self.style / "previews/ano_preview_w_123.vtex").unlink()
        result = self.run_build()
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(css, (self.style / "ano_veto_previews.css").read_text())

    def test_neutral_first_build_and_explicit_reset(self):
        result = self.run_build()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.add_existing_preview()
        result = self.run_build("-ClearPreviews")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertNotIn(".ano_preview_w_123", (self.style / "ano_veto_previews.css").read_text())

    def test_explicit_source_and_local_client_copy(self):
        self.add_existing_preview()
        prepared = Path(self.temp.name) / "prepared"
        prepared.mkdir()
        shutil.copytree(self.style / "previews", prepared / "previews")
        css = (self.style / "ano_veto_previews.css").read_text()
        (prepared / "ano_veto_previews.css").write_text(css)
        (prepared / "manifest.json").write_text('{"Maps": []}')
        result = self.run_build("-PreviewSource", str(prepared), "-InstallLocalClient")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        local = self.cs2 / "game/csgo/panorama/styles/custom_game/anocore"
        self.assertEqual(css, (local / "ano_veto_previews.vcss_c").read_text())
        self.assertTrue((local / "previews/ano_preview_w_123.vtex_c").is_file())


if os.environ.get("CI") and not PWSH:
    raise RuntimeError("CI must exercise the PowerShell addon build")
