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

    def test_three_rows_and_every_motion_cell_follows_the_authoritative_strip(self):
        ui = ROOT / 'ui/AnoCore'
        tree = ET.parse(ui / 'layout/custom_game/anocore/ano_casino_biggest.xml')
        strips = profile()[2]
        final = [n for n in tree.iter('Panel') if n.attrib.get('id','').startswith('casino_round_bb_symbol')]
        self.assertEqual(15, len(final))
        for reel in range(5):
            panel = tree.find(f'.//*[@id="casino_round_bb_reel{reel}"]')
            motion = panel.find('.//*[@class="bb-motion"]')
            self.assertEqual(303, len(motion))
            for i, p in enumerate(motion):
                self.assertIn(f'bb-symbol-{strips[reel][i % 100]}', p.attrib['class'].split())
        css = (ui / 'styles/custom_game/anocore/ano_casino_biggest.css').read_text()
        self.assertIn('height:240px', css)
        self.assertIn('width:fill-parent-flow(1)', css)
        self.assertIn('animation-fill-mode:forwards', css)
        for n in range(30,130):
            chunk = css.split(f"@keyframes 'bb-travel-{n}' {{",1)[1].split('@keyframes',1)[0]
            frames = [(float(t),int(y)) for t,y in re.findall(r'([\d.]+)%\s*\{\s*transform:translateY\((\d+)px\)',chunk)]
            self.assertEqual(2*n+1,len(frames))
            self.assertEqual((0.0,0),frames[0]); self.assertEqual((100.0,n*80),frames[-1])
            for step in range(n):
                a,b,c=frames[2*step:2*step+3]
                self.assertEqual(80,b[1]-a[1]); self.assertEqual(b[1],c[1]); self.assertLess(b[0],c[0])

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
            self.assertLess(text.index('original golden Book PNG is missing'),text.index('New-Item'))

    def test_reproducible_generation_and_exact_math(self):
        result=audit(); self.assertAlmostEqual(.941800002051252,result['total_rtp'],places=12)
        self.assertLess(result['maximum_round_multiplier']*10000,1_000_000_000)
        paths=[ROOT/'ui/AnoCore'/p for p in ['styles/custom_game/anocore/ano_casino_biggest.css','layout/custom_game/anocore/ano_casino_biggest.xml']]
        before=[p.read_bytes() for p in paths]
        subprocess.run([sys.executable,str(ROOT/'tools/generate_biggest_boy_ui.py')],check=True)
        self.assertEqual(before,[p.read_bytes() for p in paths])

    def test_missing_original_asset_is_reported_instead_of_substituted(self):
        missing=missing_assets()
        self.assertTrue(all(path.endswith('biggest_book.png') for path in missing),missing)
        book=ROOT/'ui/AnoCore/styles/custom_game/anocore/casino/biggest_book.png'
        self.assertEqual(not book.exists(),bool(missing))


if __name__=='__main__':unittest.main()
