"""Offline resource contracts; native animation remains an in-game gate."""
import pathlib
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'


class ArcadeAssetsTests(unittest.TestCase):
    def test_native_layout_has_bounded_reels_and_controls(self):
        root = ET.parse(UI / 'layout/custom_game/anocore/ano_arcade.xml').getroot()
        panels = [e for e in root if e.tag == 'Panel']
        self.assertEqual(len(panels), 1)
        self.assertNotIn('id', panels[0].attrib)
        ids = [e.attrib['id'] for e in root.iter() if 'id' in e.attrib]
        self.assertEqual(len(ids), len(set(ids)))
        for name in ('account', 'bank', 'stake', 'win', 'status', 'close',
                     'load', 'return', 'minus', 'plus', 'spin', 'refresh',
                     'slots', 'coinflip', 'roulette', 'cases', 'home'):
            self.assertIn('ano_arcade_' + name, ids)
        for reel in range(3):
            self.assertIn(f'ano_arcade_reel_{reel}', ids)
        self.assertFalse(any(e.tag == 'scripts' for e in root.iter()))

    def test_animation_restarts_use_distinct_names_and_no_server_frame_loop(self):
        css = (UI / 'styles/custom_game/anocore/ano_arcade.css').read_text()
        for name in ('arcade-spin-a', 'arcade-spin-b'):
            self.assertIn("@keyframes '" + name + "'", css)
        self.assertIn('overflow: clip;', css)
        source = (ROOT / 'src/AnoCore.Modules.Arcade/ArcadeProbePresenter.cs').read_text()
        self.assertNotIn('Task.Delay', source)
        self.assertIn('TimeSpan.FromSeconds(4)', source)
        self.assertIn('Permission = new("ano.casino.test")', source)

    def test_reel_stop_offsets_match_visible_middle_symbols(self):
        root = ET.parse(UI / 'layout/custom_game/anocore/ano_arcade.xml').getroot()
        for reel in range(3):
            strip = next(e for e in root.iter() if e.attrib.get('id') == f'ano_arcade_reel_{reel}')
            symbols = [e.attrib['class'].split()[-1] for e in strip]
            self.assertEqual(symbols, ['symbol-lemon', 'symbol-cherry', 'symbol-plum'] * 4)
            self.assertEqual(symbols[1], 'symbol-cherry')
            self.assertEqual(symbols[reel + 1], ['symbol-cherry', 'symbol-plum', 'symbol-lemon'][reel])
        # Text refresh must not reapply classes or visibility to animated panels.
        source = (ROOT / 'src/AnoCore.Modules.Arcade/ArcadeProbePresenter.cs').read_text()
        refresh = source.split('if (context.ButtonId == "ano_arcade_refresh")')[1].split('if (state.StartedAt')[0]
        self.assertNotIn('Render(state)', refresh)
        self.assertIn('Text(state, "status", state.Status)', refresh)

    def test_separate_casino_package_contains_only_casino_resources(self):
        build = (UI / 'build-casino.ps1').read_text()
        self.assertIn('"anocasino_ui"', build)
        for resource in ('ano_arcade.xml', 'ano_arcade.css', 'cherry', 'lemon', 'plum'):
            self.assertIn(resource, build)
        for unrelated in ('ano_veto.xml', 'ano_tournament.xml', 'menu.xml', 'PreviewSource'):
            self.assertNotIn(unrelated, build)
        self.assertIn('if ($LASTEXITCODE -ne 0)', build)
        self.assertIn('Compiled resource missing', build)

    def test_build_includes_layout_and_replaceable_textures(self):
        build = (UI / 'build.ps1').read_text()
        self.assertIn('"ano_arcade"', build)
        for symbol in ('cherry', 'lemon', 'plum'):
            texture = UI / f'styles/custom_game/anocore/arcade/{symbol}.vtex'
            self.assertIn(f'arcade/{symbol}.png', texture.read_text())
            png = texture.with_suffix('.png')
            self.assertTrue(png.is_file())


if __name__ == '__main__':
    unittest.main()
