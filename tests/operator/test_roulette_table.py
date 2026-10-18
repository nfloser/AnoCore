"""Readable native Roulette board, bound labels and reproducible coordinates."""
from pathlib import Path
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT/'ui/AnoCore'


class RouletteTableTests(unittest.TestCase):
    def test_all_bets_and_dynamic_labels_remain_reachable(self):
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_roulette.xml')
        ids = [p.attrib['id'] for p in tree.iter() if 'id' in p.attrib]
        self.assertEqual(len(ids), len(set(ids)))
        for bet in range(49):
            self.assertIn('casino_roulette_' + ('r' if bet <= 36 else 'bet') + str(bet), ids)
        dynamic = [p for p in tree.iter('Label') if 'id' in p.attrib and p.attrib['id'] != 'casino_roulette_pointer']
        self.assertEqual(9, len(dynamic))
        for label in dynamic:
            self.assertEqual('{s:text}', label.attrib['text'])
        for button in tree.iter('Button'):
            for child in list(button):
                self.assertEqual('false', child.attrib['hittest'])
        self.assertFalse(any('style' in p.attrib for p in tree.iter()))

    def test_generator_reproduces_committed_native_resources(self):
        paths = [UI/'layout/custom_game/anocore/ano_casino_roulette.xml', UI/'styles/custom_game/anocore/ano_casino_roulette.css']
        before = [p.read_bytes() for p in paths]
        subprocess.run([sys.executable, str(ROOT/'tools/generate_roulette_ui.py')], check=True)
        self.assertEqual(before, [p.read_bytes() for p in paths])

    def test_board_rows_follow_classic_three_column_number_groups(self):
        tree = ET.parse(UI/'layout/custom_game/anocore/ano_casino_roulette.xml')
        rows = tree.findall('.//*[@class="number-row"]')
        self.assertEqual(3, len(rows))
        for first, row in zip([3, 2, 1], rows):
            self.assertEqual([f'casino_roulette_r{n}' for n in range(first, 37, 3)] + [f'casino_roulette_bet{45+first}'],
                             [p.attrib['id'] for p in row])


if __name__ == '__main__':
    unittest.main()
