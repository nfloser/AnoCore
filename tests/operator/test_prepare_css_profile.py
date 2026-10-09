import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / 'tools/prepare_css_profile.py'
spec = importlib.util.spec_from_file_location('css_profile', SCRIPT)
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)


class CssProfileTests(unittest.TestCase):
    def inputs(self, path):
        tags = path / 'tags.json'
        tags.write_text('// https://example.test\n' + json.dumps({
            'all': {'NameTag': '{GREEN}[ANOMEME] ', 'ChatColor': '', 'NameColor': ''},
            '#css/host': {'NameTag': '{MAGENTA}[HOST] '},
            '#css/dev': {'NameTag': '{YELLOW}[DEV] '},
            '76561198000000001': {'NameTag': '{RED}[FOUNDER] '}}) + '\n// tail')
        groups = path / 'admin_groups.json'
        groups.write_text(json.dumps({'#css/host': {'flags': ['@css/map'], 'immunity': 50, 'extra': {'x': 1}},
                                     '#css/dev': {'flags': ['@anocore/team', '@css/rcon'], 'immunity': 75},
                                     '#css/normal': {'flags': [], 'immunity': 25}}))
        return tags, groups

    def test_exact_colors_private_override_and_group_preservation(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            tags, groups = self.inputs(path)
            before = groups.read_bytes()
            output = path / 'prepared'
            profile.prepare(tags, groups, output)
            roles = json.loads((output / 'role-chat-tags.json').read_text())
            self.assertEqual({'Text': '[ANOMEME]', 'Color': 'Green'}, roles['DefaultTag'])
            self.assertEqual({'Text': '[FOUNDER]', 'Color': 'Red'}, roles['PlayerOverrides']['76561198000000001'])
            host = next(x for x in roles['Groups'] if x['Group'] == '#css/host')
            self.assertEqual('Purple', host['Tag']['Color'])
            merged = json.loads((output / 'admin_groups.json').read_text())
            self.assertEqual({'flags': [], 'immunity': 25}, merged['#css/normal'])
            self.assertEqual({'x': 1}, merged['#css/host']['extra'])
            self.assertEqual(50, merged['#css/host']['immunity'])
            self.assertEqual(['@css/map', '@anocore/team', '@anocore/veto'], merged['#css/host']['flags'])
            self.assertEqual(1, merged['#css/dev']['flags'].count('@anocore/team'))
            self.assertEqual(before, groups.read_bytes())
            chat = json.loads((output / 'chat-format.json').read_text())
            self.assertEqual('{player.name}: {message}', chat['PublicTemplate'])
            self.assertIn('.map', chat['PassthroughCommands'])
            self.assertEqual('None', chat['MessageColor'])

    def test_unsupported_data_fails_before_output_and_never_overwrites(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            tags, groups = self.inputs(path)
            tags.write_text('{"all":{"NameTag":"{FAKE}[TAG]"}}')
            with self.assertRaises(ValueError):
                profile.prepare(tags, groups, path / 'out')
            self.assertFalse((path / 'out').exists())
            tags, groups = self.inputs(path)
            output = path / 'out'
            output.mkdir()
            sentinel = output / 'keep'
            sentinel.write_text('original')
            with self.assertRaises(ValueError):
                profile.prepare(tags, groups, output)
            self.assertEqual('original', sentinel.read_text())

    def test_duplicate_keys_permissions_and_unsupported_name_colors_are_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            tags, groups = self.inputs(path)
            for invalid in ['{"all":{},"all":{}}', '{"@css/root":{"NameTag":"[ROOT]"}}',
                            '{"all":{"NameTag":"[TAG]","NameColor":"Red"}}',
                            '{"all":{"NameTag":"bad\\u0001"}}', '{"0":{"NameTag":"[BAD]"}}']:
                tags.write_text(invalid)
                with self.assertRaises(ValueError):
                    profile.prepare(tags, groups, path / 'out')
                self.assertFalse((path / 'out').exists())

    def test_missing_operator_groups_and_malformed_flags_are_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            tags, groups = self.inputs(path)
            for invalid in [{}, {'#css/host': {'flags': '@css/root'}, '#css/dev': {'flags': []}}]:
                groups.write_text(json.dumps(invalid))
                with self.assertRaises(ValueError):
                    profile.prepare(tags, groups, path / 'out')
