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

    def test_wheel_landing_matches_all_37_number_centres_and_both_restart_names(self):
        css = (STYLE/'ano_casino_roulette.css').read_text()
        self.assertEqual(set(ORDER),set(range(37)))
        for i,n in enumerate(ORDER):
            end = 1440-i*360/37
            for parity in ['a','b']:
                pattern = r"@keyframes 'roulette-"+str(n)+'-'+parity+r"' \{ 0% \{ transform: rotateZ\(0deg\); \} 100% \{ transform: rotateZ\((-?[\d.]+)deg\);"
                self.assertAlmostEqual(float(re.search(pattern,css)[1]),end,places=5)
            pattern = r'\.land-n'+str(n)+r' \.casino-wheel-rotor \{ transform: rotateZ\((-?[\d.]+)deg\);'
            self.assertAlmostEqual(float(re.search(pattern,css)[1]),end,places=5)
        self.assertIn('animation-timing-function: ease-out',css)
        self.assertIn('.spinning .casino-wheel-result { opacity: 0; }',css)

    def test_reel_loop_matches_repeated_strip_and_symbols_cannot_squish(self):
        css = (STYLE/'ano_casino_slots.css').read_text()
        self.assertIn('height: 1792px; min-height: 1792px',css)
        self.assertIn('translateY(-896px)',css)
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_slots.xml')
        for reel in range(3):
            panels = tree.find('.//*[@id="casino_slots_reel'+str(reel)+'"]').find('./Panel').findall('./Panel')
            self.assertEqual(len(panels),16)
            self.assertEqual([p.attrib['class'] for p in panels[:8]], [p.attrib['class'] for p in panels[8:]])

    def test_round_shell_has_explicit_size_and_each_game_still_has_controls(self):
        css = (STYLE/'ano_casino_rounds.css').read_text()
        self.assertIn('width:1100px; height:940px;',css)
        self.assertNotIn('max-height:92%',css)
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_rounds.xml')
        for action in ['play','hit','stand','double','split','bank','risk','free','back']:
            self.assertIsNotNone(tree.find('.//*[@id="casino_round_'+action+'"]'))
