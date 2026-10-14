import pathlib
import unittest
import xml.etree.ElementTree as ET
from PIL import Image
ROOT=pathlib.Path(__file__).resolve().parents[2]
UI=ROOT/'ui/AnoCore'
class CasinoResourcesTests(unittest.TestCase):
    def test_all_buttons_and_bounded_symbol_panels_exist(self):
        root=ET.parse(UI/'layout/custom_game/anocore/ano_casino_native_v2.xml').getroot()
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
    def test_native_animation_panels_cover_settled_outcomes_and_five_lines(self):
        root=ET.parse(UI/'layout/custom_game/anocore/ano_casino_native_v2.xml').getroot()
        ids={p.attrib['id'] for p in root.iter() if 'id' in p.attrib}
        for name in ['coin','wheel','pointer','wheelresult']+[f'line{i}' for i in range(5)]:
            self.assertIn('casino_'+name,ids)
        css=(UI/'styles/custom_game/anocore/ano_casino_native_v2.css').read_text()
        for key in ['casino-coin-flip','casino-wheel-spin','casino-pointer-settle']:
            self.assertIn('@keyframes '+key,css)
        self.assertIn('.line-indicator.revealed',css)
        self.assertIn('.line-indicator.active',css)
        presenter=(ROOT/'src/AnoCore.Modules.Arcade/Casino/CasinoPresenter.cs').read_text()
        tick=presenter.split('public void Tick()',1)[1].split('private void SetOutcome',1)[0]
        for marker in ['TimeSpan.FromMilliseconds(2600 + i * 500)','TimeSpan.FromMilliseconds(3600)','TimeSpan.FromMilliseconds(4000)','state.LinesRevealed']:
            self.assertIn(marker,tick)
        self.assertNotIn('_wallet',tick)
        self.assertIn('WinningLines = [[0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]]',presenter)

        for number in range(37):
            self.assertIn(f'#casino_wheel.land-n{number} .wheel-rim',css)
        self.assertIn('#casino_coin.flipping #casino_result { visibility:collapse; }',css)
        self.assertIn('#casino_wheel.spinning #casino_wheelresult { visibility:collapse; }',css)
        self.assertIn('for (var number = 0; number <= 36; number++) Class(state, "wheel", "land-n" + number',presenter)
        self.assertIn('for (var number = 0; number <= 36; number++) Class(s, "wheel", "land-n" + number, false)',presenter)
        presenter_tests=(ROOT/'tests/AnoCore.Tests/Arcade/CasinoPresenterTests.cs').read_text()
        self.assertIn('Class("wheel", "land-n0")',presenter_tests)
        self.assertIn('Assert.IsFalse(f.Class("wheel", "land-n0"))',presenter_tests)
    def test_native_animation_alternates_without_server_frame_stream(self):
        css=(UI/'styles/custom_game/anocore/ano_casino_native_v2.css').read_text()
        for key in ['casino-roll-a','casino-roll-b','casino-flip-a','casino-flip-b']:self.assertIn('@keyframes '+key,css)
        self.assertIn('.casino-hud-host',css)
        self.assertIn('.casino-overlay.shown',css)
        self.assertIn('.casino-window { width:1180px; height:880px;',css)
        self.assertIn('.casino-build-marker',css)
    def test_native_v2_is_cache_busting_and_matches_server_contract(self):
        layout=(UI/'layout/custom_game/anocore/ano_casino_native_v2.xml').read_text()
        css=(UI/'styles/custom_game/anocore/ano_casino_native_v2.css').read_text()
        presenter=(ROOT/'src/AnoCore.Modules.Arcade/Casino/CasinoPresenter.cs').read_text()
        self.assertIn('ano_casino_native_v2.vcss_c',layout)
        self.assertIn('UI V2 · LOCAL TEST 2026-10-14',layout)
        self.assertIn('ano_casino_native_v2.xml',presenter)
        for generic in ['class="host"','class="overlay"','class="window"','class="button"']:
            self.assertNotIn(generic,layout)
        for required in ['casino-hud-host','casino-overlay','casino-window','casino-button']:
            self.assertIn(required,layout)
        self.assertNotIn('Label {',css)
        for build in ['build.ps1','build-casino.ps1']:
            self.assertIn('ano_casino_native_v2',(UI/build).read_text())

    def test_round_games_have_all_server_buttons_and_bounded_book_grid(self):
        root=ET.parse(UI/'layout/custom_game/anocore/ano_casino_rounds.xml').getroot()
        ids=[p.attrib['id'] for p in root.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids),len(set(ids)))
        for name in ['root','title','account','bankroll','payout','detail','dealer','stake','status',
                     'play','minus','plus','hit','stand','double','split','risk','bank','free',
                     'retry','close','back','refresh','rules']:
            self.assertIn('casino_round_'+name,ids)
        self.assertEqual(15,len([name for name in ids if name.startswith('casino_round_symbol')]))
        self.assertEqual(5,len([p for p in root.iter('Panel') if p.attrib.get('class','').startswith('book-reel reel-')]))
        self.assertFalse(any(p.tag=='scripts' for p in root.iter()))
        css=(UI/'styles/custom_game/anocore/ano_casino_rounds.css').read_text()
        for key in ['book-roll-a','book-roll-b','card-deal-a','card-deal-b','risk-turn-a','risk-turn-b']:
            self.assertIn('@keyframes '+key,css)
        for selector in ['.active.view-book .book-actions','.active.view-blackjack .blackjack-actions',
                         '.active.view-risk .risk-actions','.can-double .double-action','.can-split .split-action',
                         '.uncertain .retry','.motion-a .reel-final','.motion-b .reel-final']:
            self.assertIn(selector,css)
        for build in ['build.ps1','build-casino.ps1']:
            self.assertIn('ano_casino_rounds',(UI/build).read_text())
