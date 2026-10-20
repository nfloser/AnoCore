"""Native asset completeness and result-aligned client animation contracts."""
import pathlib
import re
import unittest
import xml.etree.ElementTree as ET
from PIL import Image
ROOT = pathlib.Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'
STYLE = UI / 'styles/custom_game/anocore'
ORDER = [0,32,15,19,4,21,2,25,17,34,6,27,13,36,11,30,8,23,10,5,24,16,33,1,20,14,31,9,22,18,29,7,28,12,35,3,26]

class CasinoMotionAssetsTests(unittest.TestCase):
    def test_every_referenced_texture_has_source_and_both_builders_compile_it(self):
        for game in ['slots','coinflip','roulette','blackjack','book','risk','wheel']:
            with Image.open(STYLE / 'casino' / (game+'.png')) as image:
                self.assertGreaterEqual(min(image.size), 512)
            self.assertIn('casino/'+game+'.png', (STYLE/'casino'/ (game+'.vtex')).read_text())
            for builder in ['build.ps1','build-casino.ps1']:
                self.assertIn('"'+game+'"', (UI/builder).read_text())
        for game in ['slots','coinflip','roulette']:
            self.assertIn('casino/'+game+'.vtex', (STYLE/('ano_casino_'+game+'.css')).read_text())
        for game in ['blackjack','book','risk']:
            self.assertIn('casino/'+game+'.vtex', (STYLE/'ano_casino_rounds.css').read_text())

    def test_stationary_wheel_has_all_bounded_ball_positions_and_exact_pockets(self):
        import math
        css = (STYLE/'ano_casino_roulette.css').read_text()
        self.assertNotIn('rotateZ(', css)
        self.assertNotIn('@keyframes', css)
        self.assertEqual(set(ORDER), set(range(37)))
        frames = re.findall(r'\.ball-frame-(\d+) \{ transform:translateX\(([-\d.]+)px\) translateY\(([-\d.]+)px\);', css)
        self.assertEqual(888, len(frames))
        for frame, x, y in frames:
            ring, angle = divmod(int(frame), 148)
            self.assertAlmostEqual(math.hypot(float(x)-242, float(y)-242), 228-16*ring, delta=.001)
            self.assertGreaterEqual(float(x), 14)
            self.assertLessEqual(float(x)+16, 486)
        positions = {int(frame): (float(x), float(y)) for frame, x, y in frames}
        for pocket, number in enumerate(ORDER):
            x, y = positions[740 + 4*pocket]
            self.assertAlmostEqual(x, 242 + 148*math.sin(2*math.pi*pocket/37), delta=.001)
            self.assertAlmostEqual(y, 242 - 148*math.cos(2*math.pi*pocket/37), delta=.001)
        self.assertIn('transition-duration:0.05s', css)

    def test_reel_loop_matches_repeated_strip_and_symbols_cannot_squish(self):
        css = (STYLE/'ano_casino_slots.css').read_text()
        self.assertIn('height: 1792px; min-height: 1792px',css)
        self.assertIn('flow-children: none; width: 274px;',css)
        for name in ['slot-fast-a','slot-fast-b']:
            self.assertIn("@keyframes '"+name+"' { 0% { transform: translateY(-896px); } 100% { transform: translateY(0px); } }",css)
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_slots.xml')
        for reel in range(3):
            panels = tree.find('.//*[@id="casino_slots_reel'+str(reel)+'"]').find('./Panel').findall('./Panel')
            self.assertEqual(len(panels),16)
            for row,panel in enumerate(panels):
                self.assertNotIn('style', panel.attrib)
                self.assertIn(f'slot-row-{row}', panel.attrib['class'].split())
                self.assertIn(f'.slot-row-{row} {{ position: 0px {row*112}px 0px; }}', css)
            self.assertEqual([p.attrib['class'].split()[1] for p in panels[:8]], [p.attrib['class'].split()[1] for p in panels[8:]])

    def test_round_shell_has_explicit_size_and_each_game_still_has_controls(self):
        css = (STYLE/'ano_casino_rounds.css').read_text()
        self.assertIn('width:1100px; height:940px;',css)
        self.assertNotIn('max-height:92%',css)
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_rounds.xml')
        for action in ['play','hit','stand','double','split','bank','risk','free','back']:
            self.assertIsNotNone(tree.find('.//*[@id="casino_round_'+action+'"]'))
