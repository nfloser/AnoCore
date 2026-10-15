"""Contracts for supplied artwork, fixed reels and the calculated original profile."""
import hashlib
import importlib.util
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'
STYLE = UI / 'styles/custom_game/anocore'
spec = importlib.util.spec_from_file_location('biggest_audit', ROOT / 'tools/audit_biggest_boy.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class BiggestBoyTests(unittest.TestCase):
    def test_each_reel_is_the_authoritative_double_strip_plus_visible_window(self):
        _, _, _, strips = audit.profile()
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
        for reel in range(5):
            motion = tree.find(f'.//*[@id="casino_round_biggest_offset{reel}"]//Panel[@class="biggest-motion"]')
            self.assertEqual(205, len(motion))
            for index, panel in enumerate(motion):
                self.assertIn(f'biggest-symbol-{strips[reel][index % 100]}', panel.attrib['class'].split())
                self.assertIn(f'biggest-row-{index}', panel.attrib['class'].split())
        css = (STYLE / 'ano_casino_rounds.css').read_text()
        self.assertIn('animation-iteration-count:5', css)
        self.assertIn('translateY(-8000px)', css)
        for cell in range(25):
            self.assertIsNotNone(tree.find(f'.//*[@id="casino_round_biggest_symbol{cell}"]'))

    def test_layout_reuses_book_styles_with_five_full_width_rows(self):
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
        for reel in range(5):
            panel = tree.find(f'.//*[@id="casino_round_biggest_reel{reel}"]')
            self.assertIsNotNone(panel)
            self.assertIn('book-reel', panel.attrib['class'].split())
            final = panel.find('./Panel[@class="biggest-final"]')
            self.assertEqual(10, len(final))
            for cell in final:
                self.assertIn('book-symbol', cell.attrib['class'].split())
        css = (STYLE / 'ano_casino_rounds.css').read_text()
        self.assertIn('.book-reels, .biggest-reels { width:100%;', css)
        self.assertIn('width:fill-parent-flow(1)', css)
        self.assertIn("@keyframes 'biggest-settle-a'", css)
        self.assertNotIn('.biggest-reel { width:82px;', css)

    def test_every_symbol_texture_is_compiled_by_both_builders(self):
        for name in ['kebap','beer','cigarette','butter','shot','funnel','g','o','a','t','scatter']:
            png = STYLE / f'casino/biggest_{name}.png'
            self.assertTrue(png.read_bytes().startswith(b'\x89PNG'))
            self.assertIn(f'biggest_{name}.png', (STYLE / f'casino/biggest_{name}.vtex').read_text())
            for build in ['build.ps1', 'build-casino.ps1']:
                self.assertIn(f'"biggest_{name}"', (UI / build).read_text())

    def test_exact_probability_and_rtp_include_graded_trigger_and_selected_expansion(self):
        result = audit.audit()
        self.assertAlmostEqual(.00856, result['bonus_probability'], places=12)
        self.assertAlmostEqual(10.733884714235794, result['expected_bonus_spins'], places=12)
        self.assertGreater(result['minimum_total_rtp'], .939)
        self.assertLess(result['maximum_total_rtp'], .950)
        self.assertAlmostEqual(.941747510434108, result['beer_total_rtp'], places=12)
        self.assertLessEqual(result['maximum_round_multiplier'] * 10_000, 1_000_000_000)

    def test_bonus_picker_contains_all_nine_allowed_symbols(self):
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
        for symbol in range(1, 10):
            self.assertIsNotNone(tree.find(f'.//*[@id="casino_round_bonus{symbol}"]'))
        self.assertIsNone(tree.find('.//*[@id="casino_round_bonus0"]'))
        self.assertIsNone(tree.find('.//*[@id="casino_round_bonus10"]'))


if __name__ == '__main__':
    unittest.main()
