"""Native adaptation contracts: no browser runtime and no clipped or false cards."""
import pathlib
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'
STYLE = UI / 'styles/custom_game/anocore'


class CasinoReferenceUiTests(unittest.TestCase):
    def test_blackjack_has_bounded_card_rows_and_click_children_are_inert(self):
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
        for row in ['dealer', 'hand0', 'hand1']:
            for index in range(21):
                panel = tree.find(f'.//*[@id="casino_round_{row}_card{index}"]')
                self.assertIsNotNone(panel)
                self.assertEqual('false', panel.attrib['hittest'])
        for button in tree.iter('Button'):
            for child in button.iter():
                if child is not button:
                    self.assertEqual('false', child.attrib.get('hittest'))

    def test_blackjack_style_extension_is_scoped_and_preserves_bounded_rows(self):
        css = (STYLE / 'ano_casino_rounds.css').read_text()
        extension = css.split('/* Blackjack table:', 1)[1].split('*/', 1)[1]
        selectors = re.findall(r'([^{}]+)\{[^{}]*\}', extension)
        self.assertTrue(selectors)
        for selector in selectors:
            self.assertTrue(selector.strip().startswith('.view-blackjack '), selector)
        self.assertIn('height:82px; min-height:82px', extension)
        self.assertIn('.view-blackjack .round-detail { visibility:visible;', extension)

    def test_book_loop_is_a_complete_double_strip_moving_downward(self):
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
        for reel in range(5):
            motion = tree.find(f'.//*[@id="casino_round_reel{reel}"]').find('./Panel')
            symbols = list(motion)
            self.assertEqual(16, len(symbols))
            self.assertEqual([p.attrib['class'].split()[1] for p in symbols[:8]],
                             [p.attrib['class'].split()[1] for p in symbols[8:]])
            for index, panel in enumerate(symbols):
                self.assertNotIn('style', panel.attrib)
                self.assertIn(f'book-row-{index}', panel.attrib['class'].split())
        css = (STYLE / 'ano_casino_rounds.css').read_text()
        for name in ['book-down-a', 'book-down-b']:
            self.assertIn(f"@keyframes '{name}' {{ 0% {{ transform: translateY(-640px); }}", css)

    def test_reel_loops_have_row_sized_steps_and_short_visible_holds(self):
        css = (STYLE / 'ano_casino_rounds.css').read_text()
        for prefix, rows, duration in [('book', 8, .96), ('collector', 11, 1.32)]:
            for variant in ['a', 'b']:
                chunk = css.split(f"@keyframes '{prefix}-down-{variant}' {{", 1)[1].split('@keyframes', 1)[0]
                frames = [(float(t), int(y)) for t, y in re.findall(
                    r'([\d.]+)%\s*\{\s*transform:\s*translateY\((-?\d+)px\);', chunk)]
                self.assertEqual(2 * rows + 1, len(frames))
                self.assertEqual((0.0, -80 * rows), frames[0])
                self.assertEqual((100.0, 0), frames[-1])
                for step in range(rows):
                    before, landed, held = frames[2 * step:2 * step + 3]
                    self.assertEqual(80, landed[1] - before[1])
                    self.assertEqual(landed[1], held[1])
                    self.assertAlmostEqual(20, (held[0] - landed[0]) * duration * 10, delta=.3)
        self.assertIn('.reels-armed', css)

    def test_settle_uses_result_strip_before_stop_and_alternates_names(self):
        for file, prefix in [('ano_casino_slots.css', 'slot'), ('ano_casino_rounds.css', 'book')]:
            css = (STYLE / file).read_text()
            for variant in ['a', 'b']:
                self.assertIn(f"animation-name: '{prefix}-settle-{variant}'", css)
            self.assertIn('settling', css)

    def test_all_casino_button_children_do_not_intercept_input(self):
        for view in ['lobby', 'slots', 'roulette', 'coinflip', 'rounds']:
            tree = ET.parse(UI / f'layout/custom_game/anocore/ano_casino_{view}.xml')
            for button in tree.iter('Button'):
                for child in button.iter():
                    if child is not button:
                        self.assertEqual('false', child.attrib.get('hittest'), (view, button.attrib['id']))


if __name__ == '__main__':
    unittest.main()
