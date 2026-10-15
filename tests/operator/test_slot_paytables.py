"""Generated rule panels must match the server's versioned paytables."""
from pathlib import Path
import sys
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from generate_biggest_boy_ui import balanced_pays


class SlotPaytableTests(unittest.TestCase):
    def test_both_new_tables_match_server_factors(self):
        root = ET.parse(ROOT / 'ui/AnoCore/layout/custom_game/anocore/ano_casino_biggest.xml')
        for cabinet, source, expected_count in [
            ('bb-biggest-rules', 'BiggestBoyBalancedRules.cs', 10),
            ('bb-book-rules', 'BookBalancedRules.cs', 7),
        ]:
            panel = root.find(f'.//*[@class="bb-rules {cabinet}"]')
            group = panel.find('./Panel[@class="bb-balanced-paytable"]')
            self.assertEqual(expected_count, len(group))
            for label, values in zip(group, balanced_pays(source)):
                self.assertTrue(label.attrib['text'].endswith(f'{values[0]}× / {values[1]}× / {values[2]}×'))
            self.assertIsNotNone(panel.find('./Panel[@class="bb-legacy-paytable"]'))


if __name__ == '__main__':
    unittest.main()
