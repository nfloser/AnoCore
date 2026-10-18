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


def locked_source(path):
    data = (ROOT / path).read_bytes()
    if path.endswith('ano_casino_rounds.css'):
        data = data.split(b'\n/* Blackjack table:', 1)[0]
    return data


class BiggestBoyExpandingUiTests(unittest.TestCase):
    def test_book_resources_and_rules_are_unchanged(self):
        for path, expected in json.loads((ROOT / 'docs/biggest-boy-v4-source-lock.json').read_text()).items():
            self.assertEqual(expected, hashlib.sha256(locked_source(path)).hexdigest(), path)

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


    def test_all_twelve_approved_uploads_are_byte_identical(self):
        """Lock the actual twelve user-uploaded originals, not lookalike replacements."""
        approved = {
            'biggest_kebap.png': ('döner.png', 'fb8d1d99752322846bdc44f03e5df5066be168b1a327a4421aae38af45bf49e4'),
            'biggest_beer.png': ('beer.png', '93f8c9a24769cf2220464ee594b42f43a76c449180dc2b92f74462dd4dacc590'),
            'biggest_cigarette.png': ('zigarette.png', '235bb5ef31ad799c996b1bbfda8be3f4a3c433d2d0250ba7f3d435808224418d'),
            'biggest_butter.png': ('budder.png', 'd6b46c4045011e5dae97801ef2f3011ef5f42894bc74a351f1b3b2fc163822e2'),
            'biggest_shot.png': ('shot.png', '26a0d93f44fcd536246c28b4fc6849165a6468cc24e3798d2961c45d6b0b7845'),
            'biggest_funnel.png': ('trichter.png', '49bc95e4f0e0cfc68a957c3b779dd672bfa1a9a2401ffc2dc76d6ece1eab993f'),
            'biggest_g.png': ('G.png', '2771ffa56e59fbd2eff873f78a59b2527f1cad1b88fa882670a42a1d185d56e9'),
            'biggest_o.png': ('O.png', 'ef551c54d65d3fde92b00317e3f9f4b9e25de2097ebc31a6aae6fbd1ccad841d'),
            'biggest_a.png': ('A.png', 'afb7c606176a69673cb230d7f0753a3477b8144ae362f541904ebc7c29756eb9'),
            'biggest_t.png': ('T.png', 'b01689a778cad1490038308b178bfb22dc804d3c04ea5c92928e34a666389b3c'),
            'biggest_scatter.png': ('süßilix.png', '8477eeb32c6270e5fe05b1fd7b29bc5f51adc2d6950b6e4f4d8a70299d73d8d9'),
            'biggest_book.png': ('biggest boy.png', 'cc58271464dfaeffbde2b361433dd7f021db9440c8f1da6d21ff450a250597de'),
        }
        self.assertEqual(12, len(approved))
        documented = json.loads((ROOT / 'docs/biggest-boy-original-assets.json').read_text())
        self.assertEqual(set(approved), set(documented))
        images = ROOT / 'ui/AnoCore/styles/custom_game/anocore/casino'
        for name, (source_name, sha256) in approved.items():
            self.assertEqual(source_name, documented[name]['original_filename'], name)
            self.assertEqual(sha256, documented[name]['sha256'], name)
            self.assertEqual(sha256, hashlib.sha256((images / name).read_bytes()).hexdigest(), name)
            descriptor = (images / name.replace('.png', '.vtex')).read_text()
            self.assertIn(name, descriptor)
        css = (ROOT / 'ui/AnoCore/styles/custom_game/anocore/ano_casino_biggest.css').read_text()
        for name in approved:
            self.assertIn(name.replace('.png', '.vtex'), css)

    def test_shared_native_book_cabinet_reuses_original_fruit_textures(self):
        ui = ROOT / 'ui/AnoCore'
        tree = ET.parse(ui / 'layout/custom_game/anocore/ano_casino_biggest.xml')
        self.assertEqual(35, len(tree.findall('.//*[@class="bb-book-label"]')))
        self.assertEqual(1, len(tree.findall('.//*[@class="bb-rules bb-book-rules"]')))
        css = (ui / 'styles/custom_game/anocore/ano_casino_biggest.css').read_text()
        for fruit in ('cherry', 'lemon', 'plum', 'orange', 'grapes', 'bell', 'seven'):
            self.assertIn('arcade/' + fruit + '.vtex', css)
        self.assertIn('.bb-v4.book-mode.biggest-rules .bb-book-rules', css)
        self.assertIn('.bb-v4.book-mode .bb-symbol.winning { border:3px solid #ff3030', css)
        presenter = (ROOT / 'src/AnoCore.Modules.Arcade/Casino/CasinoRoundPresenter.cs').read_text()
        self.assertIn('state.Game == CasinoRoundGame.Book || state.Game == CasinoRoundGame.BiggestBoy', presenter)
        self.assertIn('BookReelMotion.VisibleSymbol(reel, travelled, row)', presenter)


    def test_book_coconut_uses_real_uploaded_asset_and_counters_do_not_clip(self):
        ui = ROOT / 'ui/AnoCore'
        style = (ui / 'styles/custom_game/anocore/ano_casino_biggest.css').read_text()
        self.assertIn('.bb-v4.book-mode .bb-symbol-7 { background-image:url("s2r://panorama/styles/custom_game/anocore/casino/book_coconut.vtex")', style)
        self.assertIn('.bb-v4.book-mode .bb-symbol-7 .bb-book-label { visibility:collapse;', style)
        self.assertIn('.bb-v4 .bb-accounts { height:100px;', style)
        self.assertIn('.bb-v4 .bb-account { height:84px;', style)
        self.assertIn('.bb-v4 .bb-account Label { margin:0px;', style)
        self.assertIn('book_coconut.png', (ui / 'styles/custom_game/anocore/casino/book_coconut.vtex').read_text())
        for script in ('build.ps1', 'build-casino.ps1'):
            content = (ui / script).read_text()
            self.assertIn('BookCoconutPng', content)
            self.assertIn('The original golden coconut PNG is missing', content)
            self.assertIn('book_coconut', content)
        self.assertIn('BookCoconutPng', (ui / 'build-biggest-boy.ps1').read_text())

    def test_missing_original_asset_is_reported_instead_of_substituted(self):
        missing=missing_assets()
        self.assertTrue(all(path.endswith('biggest_book.png') for path in missing),missing)
        book=ROOT/'ui/AnoCore/styles/custom_game/anocore/casino/biggest_book.png'
        self.assertEqual(not book.exists(),bool(missing))


if __name__=='__main__':unittest.main()
