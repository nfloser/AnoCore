"""Exact v4 RTP from committed cyclic reels, Book paylines and expanded grids."""
import ast
from collections import Counter
from functools import lru_cache
from itertools import product
import json
from math import comb, prod
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def profile():
    source = (ROOT / 'src/AnoCore.Modules.Arcade/Casino/BiggestBoyExpandingRules.cs').read_text()
    def array(name, text=source):
        return ast.literal_eval(re.search(rf'{name}\s*=\s*(\[.*?\]);', text, re.S)[1])
    lines = array('Lines', (ROOT / 'src/AnoCore.Modules.Arcade/Casino/BonusSlotRules.cs').read_text())
    pays, counts = array('Pays'), array('Counts')
    normals = [symbol for symbol, count in enumerate(counts) for _ in range(count)]
    strips = []
    for reel in range(5):
        strip = [0] * 100
        strip[0] = strip[50] = 10
        strip[25] = 11
        index = 0
        for position in range(100):
            if position not in (0, 25, 50):
                strip[position] = normals[(index * 37 + reel * 19) % len(normals)]
                index += 1
        strips.append(strip)
    return lines, pays, strips


def audit():
    lines, pays, strips = profile()
    awards = {}
    for symbol in range(10):
        for first in product((symbol, 11), repeat=3):
            for suffix in product(range(12), repeat=2):
                cells = first + suffix
                best = 0
                for candidate in range(10):
                    count = 0
                    for actual in cells:
                        if actual not in (candidate, 11): break
                        count += 1
                    if count >= 3: best = max(best, pays[candidate][count - 3])
                awards[cells] = best / 10

    def expectation(selected=None):
        dist = {}
        for reel, strip in enumerate(strips):
            windows = [[strip[(stop + row) % 100] for row in range(3)] for stop in range(100)]
            if selected is not None:
                windows = [[selected] * 3 if selected in w else w for w in windows]
            for row in range(3):
                c = Counter(w[row] for w in windows)
                dist[reel, row] = tuple(c[s] / 100 for s in range(12))
        @lru_cache(None)
        def line_ev(ds):
            return sum(value * prod(ds[r][cells[r]] for r in range(5)) for cells, value in awards.items())
        return sum(line_ev(tuple(dist[r, cell % 3] for r, cell in enumerate(line))) for line in lines)

    # Two isolated scatter stops per reel produce six of 100 scatter windows.
    assert all(sum(10 in [strip[(stop + row) % 100] for row in range(3)] for stop in range(100)) == 6 for strip in strips)
    scatter_p = {c: comb(5, c) * .06**c * .94**(5-c) for c in range(6)}
    trigger = sum(scatter_p[c] for c in (3, 4, 5))
    @lru_cache(None)
    def duration(remaining, awarded):
        if not remaining: return 0.0
        added = min(5, 30 - awarded)
        return 1 + (1-trigger)*duration(remaining-1, awarded) + trigger*duration(remaining-1+added, awarded+added)
    expected = sum(scatter_p[c]/trigger * duration(n, n) for c,n in [(3,10),(4,15),(5,20)])
    paid = expectation()
    free = [expectation(s) for s in range(10)]
    total = paid + trigger * expected * sum(free) / 10
    return dict(version='anocore-biggest-boy-5x3-expanding-v4', paid_rtp=paid,
                bonus_probability=trigger, expected_free_spins=expected,
                free_spin_rtp_by_symbol=free, total_rtp=total, paylines=lines, paytable=pays,
                maximum_round_multiplier=max(row[2] for row in pays)*31)


if __name__ == '__main__':
    print(json.dumps(audit(), indent=2))
