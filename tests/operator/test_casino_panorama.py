import pathlib
import unittest
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'


class CasinoResourcesTests(unittest.TestCase):
    def test_modular_lobby_matches_menu_veto_shape(self):
        root = ET.parse(UI/'layout/custom_game/anocore/ano_casino_lobby.xml').getroot()
        ids = [p.attrib['id'] for p in root.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertLess(len(ids), 25)
        for name in ['root','profile','account','bank','loadamount','status','close','menu',
                     'slots','coinflip','roulette','blackjack','book','risk','load','return',
                     'loadminus','loadplus','refresh','history']:
            self.assertIn('casino_lobby_'+name, ids)
        self.assertFalse(any(p.tag == 'scripts' for p in root.iter()))

    def test_each_instant_game_has_its_own_small_layout(self):
        expected = {
            'slots': ['root','account','bank','win','stake','status','reel0','reel1','reel2','play','back','close'],
            'coinflip': ['root','account','bank','win','stake','status','coin','result','head','tail','play','back','close'],
            'roulette': ['root','account','bank','win','stake','status','wheel','wheelresult','pointer','play','back','close'],
        }
        for view, names in expected.items():
            root = ET.parse(UI/f'layout/custom_game/anocore/ano_casino_{view}.xml').getroot()
            ids = [p.attrib['id'] for p in root.iter() if 'id' in p.attrib]
            self.assertEqual(len(ids), len(set(ids)))
            for name in names:
                self.assertIn(f'casino_{view}_{name}', ids)
            self.assertFalse(any(p.tag == 'scripts' for p in root.iter()))

        slots = ET.parse(UI/'layout/custom_game/anocore/ano_casino_slots.xml').getroot()
        slot_ids = {p.attrib['id'] for p in slots.iter() if 'id' in p.attrib}
        for i in range(9):
            self.assertIn(f'casino_slots_symbol{i}', slot_ids)
        for i in range(5):
            self.assertIn(f'casino_slots_line{i}', slot_ids)

        roulette = ET.parse(UI/'layout/custom_game/anocore/ano_casino_roulette.xml').getroot()
        roulette_ids = {p.attrib['id'] for p in roulette.iter() if 'id' in p.attrib}
        for n in range(37):
            self.assertIn(f'casino_roulette_r{n}', roulette_ids)
        for n in range(37, 49):
            self.assertIn(f'casino_roulette_bet{n}', roulette_ids)

    def test_presenter_registers_small_huds_not_monolith(self):
        presenter = (ROOT/'src/AnoCore.Modules.Arcade/Casino/CasinoPresenter.cs').read_text()
        for name in ['ano_casino_lobby.xml','ano_casino_slots.xml','ano_casino_coinflip.xml','ano_casino_roulette.xml']:
            self.assertIn(name, presenter)
        self.assertNotIn('ano_casino_native_v2.xml', presenter)
        for hud in ['LobbyHudId','SlotsHudId','CoinflipHudId','RouletteHudId']:
            self.assertIn(hud, presenter)

    def test_panorama_keyframe_names_are_quoted(self):
        for css_file in (UI/'styles/custom_game/anocore').glob('*.css'):
            text = css_file.read_text()
            self.assertNotRegex(text, r'@keyframes\s+[A-Za-z0-9_-]+')
            for match in __import__('re').finditer(r"@keyframes\s+(['\"])([^'\"]+)\1", text):
                name = match.group(2)
                self.assertRegex(text, rf"animation-name\s*:\s*['\"]{__import__('re').escape(name)}['\"]")

    def test_symbols_are_individual_alpha_textures_and_included_in_full_build(self):
        for symbol in ['cherry','lemon','plum','orange','grapes','watermelon','bell','seven']:
            png = UI/f'styles/custom_game/anocore/arcade/{symbol}.png'
            with Image.open(png) as im:
                self.assertEqual(im.width, im.height)
                self.assertIn('A', im.getbands())
                self.assertEqual(im.getchannel('A').getextrema()[0], 0)
            descriptor = png.with_suffix('.vtex').read_text()
            self.assertIn(symbol+'.png', descriptor)
            self.assertIn('"'+symbol+'"', (UI/'build.ps1').read_text())

    def test_modular_resources_are_in_build_scripts(self):
        for build in ['build.ps1','build-casino.ps1']:
            text = (UI/build).read_text()
            for name in ['ano_casino_lobby','ano_casino_slots','ano_casino_coinflip','ano_casino_roulette','ano_casino_rounds']:
                self.assertIn(name, text)

    def test_native_animation_contract_stays_server_authoritative(self):
        presenter = (ROOT/'src/AnoCore.Modules.Arcade/Casino/CasinoPresenter.cs').read_text()
        tick = presenter.split('public void Tick()',1)[1].split('private void SwitchGame',1)[0]
        for marker in ['TimeSpan.FromMilliseconds(2600 + i * 500)',
                       'TimeSpan.FromMilliseconds(3600)',
                       'TimeSpan.FromMilliseconds(4000)',
                       'state.LinesRevealed']:
            self.assertIn(marker, tick)
        self.assertNotIn('_wallet', tick)
        self.assertIn('WinningLines = [[0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]]', presenter)

        slots_css = (UI/'styles/custom_game/anocore/ano_casino_slots.css').read_text()
        coin_css = (UI/'styles/custom_game/anocore/ano_casino_coinflip.css').read_text()
        roulette_css = (UI/'styles/custom_game/anocore/ano_casino_roulette.css').read_text()
        self.assertIn("@keyframes 'slot-roll'", slots_css)
        self.assertIn("@keyframes 'coin-spin'", coin_css)
        self.assertNotIn("rotateZ(", roulette_css)
        self.assertIn(".casino-ball", roulette_css)

    def test_spitze_has_three_ladders_real_motion_and_collect_controls(self):
        root = ET.parse(UI/'layout/custom_game/anocore/ano_casino_rounds.xml').getroot()
        ids = {node.attrib['id'] for node in root.iter() if 'id' in node.attrib}
        for ladder in ['low', 'middle', 'high']:
            for level in range(1, 6):
                self.assertIn(f'casino_round_spitze_{ladder}{level}', ids)
        for element in ['spitze_symbol', 'spitze_pot', 'spitze_direct',
                        'spitze_games', 'spitze_progress', 'spin', 'ag', 'collect']:
            self.assertIn('casino_round_' + element, ids)
        content = (UI/'styles/custom_game/anocore/ano_casino_rounds.css').read_text()
        for name in ['spitze-roll-a', 'spitze-roll-b']:
            self.assertIn("@keyframes '" + name + "'", content)
            self.assertIn("animation-name:'" + name + "'", content)
        lobby = ET.parse(UI/'layout/custom_game/anocore/ano_casino_lobby.xml').getroot()
        card = next(node for node in lobby.iter('Button')
                    if node.attrib.get('id') == 'casino_lobby_risk')
        self.assertIn('ALLES SPITZE', ' '.join(node.attrib.get('text', '') for node in card.iter()))
        presenter = (ROOT/'src/AnoCore.Modules.Arcade/Casino/CasinoPresenter.cs').read_text()
        self.assertIn('roundGame = CasinoRoundGame.Spitze;', presenter)

    def test_round_games_have_all_server_buttons_and_bounded_book_grid(self):
        root = ET.parse(UI/'layout/custom_game/anocore/ano_casino_rounds.xml').getroot()
        ids = [p.attrib['id'] for p in root.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids), len(set(ids)))
        for name in ['root','title','account','bankroll','payout','detail','dealer','stake','status',
                     'play','minus','plus','hit','stand','double','split','risk','bank','free',
                     'retry','close','back','refresh','rules']:
            self.assertIn('casino_round_'+name, ids)
        self.assertEqual(15, len([name for name in ids if name.startswith('casino_round_symbol')]))
        self.assertEqual(5, len([p for p in root.iter('Panel') if p.attrib.get('class','').startswith('book-reel reel-')]))
        self.assertFalse(any(p.tag == 'scripts' for p in root.iter()))


if __name__ == '__main__':
    unittest.main()
