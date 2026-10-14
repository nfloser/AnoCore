"""Exact line expectations and capped bonus length for the committed original profile.

Reads Lines/Pays/counts from C#; no Monte Carlo or external dependencies.
Cross-line dependence does not affect the expectation of their summed payouts.
"""
import ast
from collections import Counter
from functools import lru_cache
from itertools import product
from math import comb, prod
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]


def profile():
    source = (ROOT / 'src/AnoCore.Modules.Arcade/Casino/BiggestBoyRules.cs').read_text()
    def array(name):
        return ast.literal_eval(re.search(rf'{name}\s*=\s*(\[.*?\]);', source, re.S)[1])
    lines, pays, counts = array('Lines'), array('Pays'), array('counts')
    normals = [symbol for symbol, count in enumerate(counts, 1) for _ in range(count)]
    strips = []
    for reel in range(5):
        strip = [0] * 100
        strip[0] = strip[50] = 10
        index = 0
        for position in range(100):
            if position not in (0, 5, 50):
                strip[position] = normals[(index * 37 + reel * 19) % 97]
                index += 1
        strips.append(strip)
    return lines, pays, strips


def audit():
    lines, pays, strips = profile()
    distributions = {}
    for free in (False, True):
        for reel, strip in enumerate(strips):
            windows = [[strip[(stop + row) % 100] for row in range(5)] for stop in range(100)]
            if free:
                windows = [[0 if symbol != 10 else 10 for symbol in window] if 0 in window else window for window in windows]
            for row in range(5):
                counts = Counter(window[row] for window in windows)
                distributions[free, reel, row] = tuple(counts[s] / 100 for s in range(11))
    # Only these prefixes can win; all other combinations pay zero.
    winners = set()
    for symbol in range(10):
        for prefix in product((0, symbol), repeat=3):
            winners.update(prefix + suffix for suffix in product(range(11), repeat=2))
    awards = {}
    for cells in winners:
        best = 0
        for symbol in range(10):
            count = 0
            for actual in cells:
                if actual not in (0, symbol):
                    break
                count += 1
            if count >= 3:
                best = max(best, pays[symbol][count - 3])
        awards[cells] = best / 10
    @lru_cache(None)
    def expectation(dist):
        return sum(value * prod(dist[r][cells[r]] for r in range(5)) for cells, value in awards.items())
    def spin_mean(free):
        return sum(expectation(tuple(distributions[free, reel, cell % 5] for reel, cell in enumerate(line))) for line in lines)
    scatter_windows = [sum(10 in [strip[(stop + row) % 100] for row in range(5)] for stop in range(100)) for strip in strips]
    assert scatter_windows == [10] * 5
    p = sum(comb(5, k) * .1 ** k * .9 ** (5 - k) for k in range(3, 6))
    @lru_cache(None)
    def expected_spins(remaining, awarded):
        if remaining == 0:
            return 0.0
        added = min(5, 30 - awarded, 20 - (remaining - 1))
        return 1 + (1 - p) * expected_spins(remaining - 1, awarded) + p * expected_spins(remaining - 1 + added, awarded + added)
    paid, free = spin_mean(False), spin_mean(True)
    count = expected_spins(10, 10)
    return dict(bonus_probability=p, mean_paid_spins_per_bonus=1/p,
                expected_bonus_spins=count, paid_spin_rtp=paid, free_spin_payout_per_locked_stake=free,
                total_rtp=paid + p * count * free, maximum_round_multiplier=max(row[2] for row in pays) * 31,
                paytable=pays)


if __name__ == '__main__':
    print(json.dumps(audit(), indent=2))
