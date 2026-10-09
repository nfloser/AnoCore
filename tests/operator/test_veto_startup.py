"""Guard native composition closures whose startup locals transfer ownership.

The engine-free vote tests cover authorization and stale sessions. This check
covers the native composition boundary they cannot instantiate without CS2.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class VetoStartupTests(unittest.TestCase):
    def test_lifetime_callback_does_not_capture_cleared_startup_owners(self):
        source = (ROOT / "src/AnoCore.Plugin/AnoCorePlugin.cs").read_text()
        composition = source.split("var votes = new SessionBoundVetoVoteService(", 1)[1]
        callback = composition.split("() =>", 1)[1].split(");", 1)[0]
        transfer = source.split("_pendingRuntime = created;", 1)[1]
        transfer = transfer.split("Server.NextWorldUpdate", 1)[0]
        cleared = set(re.findall(r"\b(\w+)\s*=\s*null\s*;", transfer))
        captured = set(re.findall(r"\b\w+\b", callback))
        self.assertFalse(cleared & captured,
                         f"Veto lifetime captures cleared startup owners: {cleared & captured}")


if __name__ == "__main__":
    unittest.main()
