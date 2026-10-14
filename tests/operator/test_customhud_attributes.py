"""CustomHud rejects inline style even when resource compilation succeeds."""
import pathlib
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]

class CustomHudAttributesTests(unittest.TestCase):
    def test_all_native_layouts_reject_inline_styles(self):
        for path in (ROOT / 'ui/AnoCore/layout').rglob('*.xml'):
            for element in ET.parse(path).getroot().iter():
                self.assertNotIn('style', element.attrib, (path.name, element.attrib))
