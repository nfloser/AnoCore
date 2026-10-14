"""Exercise Arcade-only repair against a fake compiler; native rendering stays a client check."""
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")
FILES = (
    "layout/custom_game/anocore/ano_arcade.vxml_c",
    "styles/custom_game/anocore/ano_arcade.vcss_c",
    "styles/custom_game/anocore/arcade/cherry.vtex_c",
    "styles/custom_game/anocore/arcade/lemon.vtex_c",
    "styles/custom_game/anocore/arcade/plum.vtex_c",
)


class ArcadeRepairResourceTests(unittest.TestCase):
    def test_native_entity_uses_compiled_resource_and_cleans_legacy_source_names(self):
        adapter = (ROOT / "src/AnoCore.Plugin/Hud/CounterStrikeCustomHudService.cs").read_text()
        self.assertIn("var layoutResource = registration.Definition.CompiledLayoutResource;", adapter)
        self.assertIn("entity.StrLayout = layoutResource;", adapter)
        self.assertNotIn("entity.StrLayout = registration.Definition.LayoutResource;", adapter)
        self.assertIn("existing.StrLayout,", adapter)

    def test_arcade_has_all_presenter_targets_and_visible_delivery_marker(self):
        layout = ET.parse(ROOT / "ui/AnoCore/layout/custom_game/anocore/ano_arcade.xml").getroot()
        ids = [node.attrib["id"] for node in layout.iter() if "id" in node.attrib]
        self.assertEqual(len(ids), len(set(ids)), "Duplicate IDs make client targeting ambiguous")
        for name in ("root", "profile", "account", "bank", "stake", "win", "status",
                     "reel_0", "reel_1", "reel_2", "close", "home", "slots", "coinflip",
                     "roulette", "cases", "load", "return", "minus", "plus", "spin", "refresh"):
            self.assertIn("ano_arcade_" + name, ids)
        self.assertTrue(any(node.tag == "Label" and node.attrib.get("text") == "ARCADE UI CHECK 2026-10-14"
                            for node in layout.iter()), "A static marker must identify the loaded client resource")


@unittest.skipUnless(PWSH, "PowerShell integration runs on the CI runner")
class ArcadeRepairTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.cs2 = Path(self.temp.name) / "CS2 with spaces"
        self.addon = self.cs2 / "game/csgo_addons/anomeme_ui/panorama"
        self.content = self.cs2 / "content/csgo_addons/anomeme_ui/panorama"
        self.content.mkdir(parents=True)
        self.local = self.cs2 / "game/csgo/panorama"
        self.log = Path(self.temp.name) / "compiler.log"
        self.compiler = self.cs2 / "game/bin/win64/resourcecompiler.exe"
        self.compiler.parent.mkdir(parents=True)
        self.compiler.write_text('''#!/usr/bin/env python3
import os,pathlib,sys
source=pathlib.Path(sys.argv[sys.argv.index('-i')+1])
with open(os.environ['ARCADE_TEST_COMPILER_LOG'],'a') as log: log.write(source.name+'\\n')
mode=os.environ.get('ARCADE_TEST_COMPILER_MODE','success')
if mode=='failure' and source.suffix=='.css': sys.exit(17)
if mode=='no-output': sys.exit(0)
parts=source.parts
index=parts.index('content')
target=pathlib.Path(*parts[:index])/'game'/pathlib.Path(*parts[index+1:])
target=target.with_suffix({'.css':'.vcss_c','.xml':'.vxml_c','.vtex':'.vtex_c'}[source.suffix])
target.parent.mkdir(parents=True,exist_ok=True)
target.write_bytes(b'fresh-compiled:'+source.name.encode())
''')
        self.compiler.chmod(0o755)
        for relative in FILES:
            for root, prefix in ((self.local, b"old-client:"), (self.addon, b"stale-addon:")):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(prefix + relative.encode())
        self.unrelated = self.local / "layout/custom_game/anocore/menu.vxml_c"
        self.unrelated.write_bytes(b"working-menu-untouched")
        self.other = self.local / "styles/custom_game/anocore/arcade/other.vtex_c"
        self.other.write_bytes(b"unrelated-texture-untouched")

    def run_repair(self, *args, compiler_mode="success", script="repair-arcade.ps1"):
        env = os.environ.copy()
        env["ARCADE_TEST_COMPILER_LOG"] = str(self.log)
        env["ARCADE_TEST_COMPILER_MODE"] = compiler_mode
        return subprocess.run([PWSH, "-NoProfile", "-File", str(ROOT / "ui/AnoCore" / script),
                               "-Cs2", str(self.cs2), *args], env=env, capture_output=True, text=True, timeout=90)

    def assert_original_client(self):
        for relative in FILES:
            self.assertEqual(b"old-client:" + relative.encode(), (self.local / relative).read_bytes())
        self.assert_unrelated_preserved()

    def assert_unrelated_preserved(self):
        self.assertEqual(b"working-menu-untouched", self.unrelated.read_bytes())
        self.assertEqual(b"unrelated-texture-untouched", self.other.read_bytes())

    def assert_backup_contains_originals(self):
        backup = self.cs2 / "anocore-ui-backups"
        files = list(backup.rglob("*")) if backup.exists() else []
        files = [path for path in files if path.is_file() and path.name != "manifest.json"]
        self.assertEqual(5, len(files), "Only the five Arcade loose-client files should be backed up")
        for relative in FILES:
            matches = [path for path in files if path.as_posix().endswith(relative)]
            self.assertEqual(1, len(matches), relative)
            self.assertEqual(b"old-client:" + relative.encode(), matches[0].read_bytes())

    def assert_manifest_matches_fresh_build(self):
        manifests = list((self.cs2 / "anocore-ui-backups").rglob("manifest.json"))
        self.assertEqual(1, len(manifests))
        entries = json.loads(manifests[0].read_text(encoding="utf-8-sig"))
        self.assertEqual(5, len(entries))
        self.assertCountEqual(FILES, [entry["Resource"] for entry in entries])
        for entry in entries:
            compiled = self.addon / entry["Resource"]
            digest = hashlib.sha256(compiled.read_bytes()).hexdigest()
            self.assertEqual(digest, entry["SHA256"].lower())

    def seed_all_stale_build_outputs(self, addon_name):
        panorama = self.cs2 / "game/csgo_addons" / addon_name / "panorama"
        outputs = ["styles/custom_game/anocore/ano_veto_previews.vcss_c",
                   "styles/custom_game/anocore/anomeme_banner.vtex_c"]
        outputs += [f"styles/custom_game/anocore/arcade/{symbol}.vtex_c" for symbol in
                    ("cherry", "lemon", "plum", "orange", "grapes", "watermelon", "bell", "seven")]
        for name in ("ano_veto_cards", "ano_veto", "menu", "ano_tournament", "ano_arcade", "ano_casino", "ano_casino_rounds"):
            outputs += [f"layout/custom_game/anocore/{name}.vxml_c", f"styles/custom_game/anocore/{name}.vcss_c"]
        for relative in outputs:
            path = panorama / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"stale-compiled-output")

    def test_combined_build_rejects_zero_exit_missing_fresh_output_even_with_stale_outputs(self):
        self.seed_all_stale_build_outputs("anomeme_ui")
        result = self.run_repair(compiler_mode="no-output", script="build.ps1")
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue(self.log.exists(), "Rejection must exercise compilation, not fixture setup")
        self.assert_original_client()

    def test_casino_build_rejects_zero_exit_missing_fresh_texture_even_with_stale_outputs(self):
        self.seed_all_stale_build_outputs("anocasino_ui")
        result = self.run_repair(compiler_mode="no-output", script="build-casino.ps1")
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue(self.log.exists(), "Rejection must exercise compilation, not fixture setup")
        self.assert_original_client()

    def test_default_install_compiles_exact_resources_backs_up_and_preserves_other_ui(self):
        result = self.run_repair()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertCountEqual(["ano_arcade.xml", "ano_arcade.css", "cherry.vtex", "lemon.vtex", "plum.vtex"],
                              self.log.read_text().splitlines())
        self.assert_backup_contains_originals()
        self.assert_manifest_matches_fresh_build()
        for relative in FILES:
            self.assertEqual((self.addon / relative).read_bytes(), (self.local / relative).read_bytes())
            self.assertTrue((self.local / relative).read_bytes().startswith(b"fresh-compiled:"))
        self.assert_unrelated_preserved()

    def test_zero_exit_without_output_cannot_reuse_stale_compiled_files_or_install(self):
        result = self.run_repair(compiler_mode="no-output")
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(self.log.exists(), "The fake compiler must actually have run")
        self.assert_original_client()

    def test_compile_failure_cannot_partially_install(self):
        result = self.run_repair(compiler_mode="failure")
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(self.log.exists())
        self.assert_original_client()

    def test_build_only_does_not_modify_local_client(self):
        result = self.run_repair("-Mode", "BuildOnly")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assert_original_client()
        self.assertEqual(5, len(self.log.read_text().splitlines()))
        self.assert_manifest_matches_fresh_build()

    def test_workshop_mode_removes_only_five_loose_files_without_compiler(self):
        self.compiler.unlink()
        result = self.run_repair("-Mode", "UseWorkshop")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse(self.log.exists())
        self.assert_backup_contains_originals()
        for relative in FILES:
            self.assertFalse((self.local / relative).exists())
            self.assertEqual(b"stale-addon:" + relative.encode(), (self.addon / relative).read_bytes())
        self.assert_unrelated_preserved()

    def run_with_transfer_failure(self, operation):
        def literal(value):
            return "'" + str(value).replace("'", "''") + "'"

        wrapper = Path(self.temp.name) / (operation + "-failure.ps1")
        source_prefix = self.local if operation == "Move" else self.addon
        shim = r"""param([string]$LiteralPath, [string]$Destination, [switch]$Force)
    if ($LiteralPath.StartsWith($global:arcadeFixtureSourcePrefix) -and
        ($global:arcadeFixtureOperation -eq 'Move' -or $Destination.StartsWith($global:arcadeFixtureClientPrefix))) {
        $global:arcadeFixtureTransferCount++
        if ($global:arcadeFixtureTransferCount -eq 2) { throw 'Injected second transfer failure' }
    }
"""
        wrapper.write_text(
            "$ErrorActionPreference = 'Stop'\n"
            + "$global:arcadeFixtureTransferCount = 0\n"
            + "$global:arcadeFixtureOperation = " + literal(operation) + "\n"
            + "$global:arcadeFixtureSourcePrefix = " + literal(source_prefix) + "\n"
            + "$global:arcadeFixtureClientPrefix = " + literal(self.local) + "\n"
            + "function " + operation + "-Item {\n" + shim
            + "    Microsoft.PowerShell.Management\\" + operation + "-Item @PSBoundParameters\n}\n"
            + "& " + literal(ROOT / "ui/AnoCore/repair-arcade.ps1")
            + " -Cs2 " + literal(self.cs2)
            + " -Mode " + ("UseWorkshop" if operation == "Move" else "InstallLocalClient") + "\n")
        return self.run_repair(script=str(wrapper))

    def test_workshop_second_move_failure_restores_all_original_client_files(self):
        result = self.run_with_transfer_failure("Move")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Injected second transfer failure", result.stdout + result.stderr)
        self.assert_original_client()

    def test_install_second_copy_failure_restores_all_original_client_files(self):
        result = self.run_with_transfer_failure("Copy")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Injected second transfer failure", result.stdout + result.stderr)
        self.assert_original_client()

    def test_invalid_addon_paths_are_rejected_before_any_compilation_or_client_change(self):
        for addon in ("../escape", "nested/addon", "nested\\addon", ".."):
            with self.subTest(addon=addon):
                result = self.run_repair("-Addon", addon)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(self.log.exists())
                self.assert_original_client()


if os.environ.get("CI") and not PWSH:
    raise RuntimeError("CI must exercise the PowerShell Arcade repair")
