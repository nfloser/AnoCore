"""Native Blackjack bounds, input and asset packaging contracts."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT / 'ui/AnoCore'


class BlackjackTableTests(unittest.TestCase):
    def test_four_hands_all_controls_and_inert_click_children(self):
        tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_blackjack.xml')
        ids = [p.attrib['id'] for p in tree.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids), len(set(ids)))
        for hand in ['dealer', 'hand0', 'hand1', 'hand2', 'hand3']:
            for card in range(21):
                self.assertIn(f'casino_round_{hand}_card{card}', ids)
        for name in ['hit', 'stand', 'double', 'split', 'surrender', 'insurance', 'decline',
                     'evenmoney', 'play', 'retry', 'back', 'close', 'refresh', 'rules']:
            self.assertIn('casino_round_' + name, ids)
        for button in tree.iter('Button'):
            for child in button.iter():
                if child is not button:
                    self.assertEqual('false', child.attrib.get('hittest'))
        self.assertFalse(any('style' in p.attrib for p in tree.iter()))
        self.assertFalse(list(tree.iter('scripts')))
        rules = tree.find('.//*[@class="rules-sheet"]')
        self.assertEqual('true', rules.attrib['hittest'])
        self.assertEqual(11, len(rules.findall('./Label[@class="rules-line"]')))

    def test_card_fans_fit_each_split_hand_and_keep_short_hands_centered(self):
        css = (UI / 'styles/custom_game/anocore/ano_casino_blackjack.css').read_text()
        for count in range(1, 9):
            self.assertIn(f'.card-row.cards-{count} {{ width:{(count-1)*72+62}px;', css)
        for index in range(21):
            self.assertIn(f'.compact .card-index-{index} {{ position:{index*26}px', css)
        self.assertLessEqual(20 * 26 + 62, 600)
        self.assertLessEqual(7 * 72 + 62, 600)
        self.assertIn('.compact .playing-card { text-align:left;', css)
        self.assertIn("@keyframes 'blackjack-deal-a'", css)
        self.assertIn('height:1000px;', css)
        self.assertIn('.table { width:100%; height:456px; min-height:456px;', css)

    def test_both_native_build_paths_compile_dedicated_view(self):
        for build in ['build.ps1', 'build-casino.ps1']:
            self.assertIn('"ano_casino_blackjack"', (UI / build).read_text())
        presenter = (ROOT / 'src/AnoCore.Modules.Arcade/Casino/CasinoRoundPresenter.cs').read_text()
        self.assertIn('panorama/layout/custom_game/anocore/ano_casino_blackjack.xml', presenter)
        self.assertIn('_hud.HideAll(BlackjackHudId)', presenter)
        self.assertIn('_hud.Hide(player, BlackjackHudId)', presenter)


if __name__ == '__main__':
    unittest.main()
