"""V4 source contracts: fifteen cells, fixed physical strips and protected Book UI."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from audit_biggest_boy_expanding import audit, profile
from check_biggest_boy_assets import missing_assets


class BiggestBoyExpandingUiTests(unittest.TestCase):
    def test_book_resources_and_rules_are_unchanged(self):
        for path, expected in json.loads((ROOT / 'docs/biggest-boy-v4-source-lock.json').read_text()).items():
            self.assertEqual(expected, hashlib.sha256((ROOT / path).read_bytes()).hexdigest(), path)

    def test_three_rows_and_bounded_motion_panels_use_only_original_symbols(self):
        ui = ROOT / 'ui/AnoCore'
        tree = ET.parse(ui / 'layout/custom_game/anocore/ano_casino_biggest.xml')
        final = [n for n in tree.iter('Panel') if n.attrib.get('id', '').startswith('casino_round_bb_symbol')]
        self.assertEqual(15, len(final))
        for reel in range(5):
            panel = tree.find(f'.//*[@id="casino_round_bb_reel{reel}"]')
            motion = panel.find('.//*[@class="bb-motion"]')
            self.assertEqual(4, len(motion))
            self.assertEqual([f'casino_round_bb_motion{reel}_{row}' for row in range(4)],
                             [child.attrib['id'] for child in motion])
            self.assertIsNone(panel.find('.//*[@class="bb-offset-base"]'))
        css = (ui / 'styles/custom_game/anocore/ano_casino_biggest.css').read_text()
        self.assertIn('height:480px', css)
        self.assertIn('width:fill-parent-flow(1)', css)
        self.assertIn('overflow:clip', css)
        self.assertIn("animation-name:'bb-step-a'", css)
        self.assertNotIn('48480px', css)
        self.assertNotIn('bb-distance-', css)
        self.assertNotIn('bb-offset-base', css)
        self.assertIn('.bb-symbol.winning { border:3px solid #ff3030', css)

    def test_same_book_buttons_and_short_style_holds_are_native(self):
        ui=ROOT/'ui/AnoCore'; tree=ET.parse(ui/'layout/custom_game/anocore/ano_casino_biggest.xml')
        ids=[p.attrib['id'] for p in tree.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids),len(set(ids)))
        for button in ['play','minus','plus','free','rules','close','back','retry']:
            self.assertIn('casino_round_'+button,ids)
        for button in tree.iter('Button'):
            for child in button.iter():
                if child is not button:self.assertEqual('false',child.attrib.get('hittest'))
        for element in tree.iter():self.assertNotIn('style',element.attrib)
        for build in ['build.ps1','build-casino.ps1']:
            text=(ui/build).read_text();self.assertIn('"biggest_book"',text);self.assertIn('"ano_casino_biggest"',text)
            self.assertLess(text.index('original Biggest Boy Wild PNG is missing'),text.index('New-Item'))

    def test_reproducible_generation_and_exact_math(self):
        result=audit(); self.assertAlmostEqual(.941800002051252,result['total_rtp'],places=12)
        self.assertLess(result['maximum_round_multiplier']*10000,1_000_000_000)
        paths=[ROOT/'ui/AnoCore'/p for p in ['styles/custom_game/anocore/ano_casino_biggest.css','layout/custom_game/anocore/ano_casino_biggest.xml']]
        before=[p.read_bytes() for p in paths]
        subprocess.run([sys.executable,str(ROOT/'tools/generate_biggest_boy_ui.py')],check=True)
        self.assertEqual(before,[p.read_bytes() for p in paths])

    def test_approved_full_width_layout_and_original_artwork(self):
        ui=ROOT/'ui/AnoCore'
        tree=ET.parse(ui/'layout/custom_game/anocore/ano_casino_biggest.xml')
        self.assertEqual(4,len(tree.findall('.//*[@class="bb-divider"]')))
        self.assertEqual(4,len(tree.findall('.//*[@class="bb-bonus-item"]')))
        controls=tree.find('.//*[@class="bb-controls"]')
        self.assertEqual(['bb-stake','bb-payout','bb-bottom-bank','bb-actions'],[p.attrib['class'] for p in controls])
        self.assertEqual(2, len(tree.findall('.//*[@class="bb-account"]')))
        self.assertIn('casino_round_bankroll_footer', [p.attrib.get('id') for p in tree.iter()])
        self.assertFalse(any(p.attrib.get('text') == 'Echtgeld' for p in tree.iter('Label')))
        self.assertFalse(missing_assets())
        self.assertIn('width:100%; height:100%',(ui/'styles/custom_game/anocore/ano_casino_biggest.css').read_text())

    def test_profile_math_and_original_hashes(self):
        expected={'standard':.941800002051252,'frequent':.9238081885566861,'generous':.9826858821485963}
        for name,rtp in expected.items(): self.assertAlmostEqual(rtp,audit(name)['total_rtp'],places=12)
        for name,item in json.loads((ROOT/'docs/biggest-boy-original-assets.json').read_text()).items():
            self.assertEqual(item['sha256'],hashlib.sha256((ROOT/'ui/AnoCore/styles/custom_game/anocore/casino'/name).read_bytes()).hexdigest())

    def test_missing_original_asset_is_reported_instead_of_substituted(self):
        missing=missing_assets()
        self.assertTrue(all(path.endswith('biggest_book.png') for path in missing),missing)
        book=ROOT/'ui/AnoCore/styles/custom_game/anocore/casino/biggest_book.png'
        self.assertEqual(not book.exists(),bool(missing))


if __name__=='__main__':unittest.main()
