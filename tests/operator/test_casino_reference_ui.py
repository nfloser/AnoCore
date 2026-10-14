"""Native adaptation contracts: no browser runtime and no clipped or false cards."""
import pathlib
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
            self.assertIn(f"@keyframes '{name}' {{ 0% {{ transform: translateY(-640px); }} 100% {{ transform: translateY(0px); }} }}", css)

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
