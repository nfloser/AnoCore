"""Verify the self-contained, old-server-inspired AnoMeme chat profile."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROFILE = ROOT / "examples/server-profiles/anomeme"


class LegacyChatProfileTests(unittest.TestCase):
    def test_chat_profile_preserves_uncolored_name_and_message_slots(self):
        data = json.loads((PROFILE / "chat-format.json").read_text(encoding="utf-8"))
        self.assertEqual("{player.name}: {message}", data["PublicTemplate"])
        self.assertEqual("{player.name}: {message}", data["TeamTemplate"])
        self.assertEqual("None", data["NameColor"])
        self.assertEqual("None", data["MessageColor"])
        self.assertEqual("None", data["RankColor"])
        self.assertIn(".ready", data["PassthroughCommands"])
        self.assertIn(".map", data["PassthroughCommands"])

    def test_standard_action_aliases_survive_the_color_profile(self):
        data = json.loads((PROFILE / "chat-format.json").read_text(encoding="utf-8"))
        commands = data["PassthroughCommands"]
        self.assertLessEqual(len(commands), 64)
        self.assertEqual(len(commands), len({c.lower() for c in commands}))
        for command in [".r", ".ready", ".ur", ".notready", ".unready", ".p", ".pause",
                        ".up", ".unpause", ".swap", ".switch", ".map", ".prac", ".stay"]:
            self.assertIn(command, commands)

    def test_role_colors_still_match_old_server_groups(self):
        data = json.loads((PROFILE / "role-chat-tags.json").read_text(encoding="utf-8"))
        group_colors = {entry["Group"]: entry["Tag"]["Color"] for entry in data["Groups"]}
        self.assertEqual({
            "#css/admin": "LightRed",
            "#css/dev": "Yellow",
            "#css/host": "Purple",
            "#css/og": "DarkBlue",
            "#css/normal": "Green",
        }, group_colors)
        self.assertEqual("Green", data["DefaultTag"]["Color"])

    def test_channel_labels_are_inserted_before_role_and_name(self):
        src = (ROOT / "src/AnoCore.Modules.Admin/NativeChatRouter.cs").read_text(encoding="utf-8")
        self.assertIn('isTeamMessage ? "[TEAM] " : "[ALL] "', src)
        self.assertIn("rolePrefix, formatted", src)


if __name__ == "__main__":
    unittest.main()
