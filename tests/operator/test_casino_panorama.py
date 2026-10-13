import pathlib
import unittest
import xml.etree.ElementTree as ET
from PIL import Image
ROOT=pathlib.Path(__file__).resolve().parents[2]
UI=ROOT/'ui/AnoCore'
class CasinoResourcesTests(unittest.TestCase):
    def test_all_buttons_and_bounded_symbol_panels_exist(self):
        root=ET.parse(UI/'layout/custom_game/anocore/ano_casino.xml').getroot()
        ids=[p.attrib['id'] for p in root.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids),len(set(ids)))
        for name in ['root','account','bank','stake','win','choice','load','return','play','slots','coinflip','roulette','head','tail','retry','menu']:
            self.assertIn('casino_'+name,ids)
        for i in range(9): self.assertIn('casino_symbol'+str(i),ids)
        for n in range(37): self.assertIn("casino_r"+str(n),ids)
        for n in range(37,49): self.assertIn('casino_bet'+str(n),ids)
        self.assertFalse(any(p.tag=='scripts' for p in root.iter()))
    def test_symbols_are_individual_alpha_textures_and_included_in_builds(self):
        for symbol in ['cherry','lemon','plum','orange','grapes','watermelon','bell','seven']:
            png=UI/f'styles/custom_game/anocore/arcade/{symbol}.png'
            with Image.open(png) as im:
                self.assertEqual(im.width,im.height)
                self.assertIn('A',im.getbands())
                self.assertEqual(im.getchannel('A').getextrema()[0],0)
            descriptor=png.with_suffix('.vtex').read_text()
            self.assertIn(symbol+'.png',descriptor)
            for build in ['build.ps1','build-casino.ps1']:
                self.assertIn('"'+symbol+'"',(UI/build).read_text())
        for build in ['build.ps1','build-casino.ps1']:self.assertIn('ano_casino',(UI/build).read_text())
    def test_native_animation_alternates_without_server_frame_stream(self):
        css=(UI/'styles/custom_game/anocore/ano_casino.css').read_text()
        for key in ['casino-roll-a','casino-roll-b','casino-flip-a','casino-flip-b']:self.assertIn('@keyframes '+key,css)
        self.assertIn('overflow:clip',css)
