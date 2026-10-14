"""Exact RTP audit for the committed Biggest Boy 5x5 v2 profile.

Reads paylines, base pays, expansion pays and strip counts from C#.
No Monte Carlo and no external dependencies are used.
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

    lines = array('Lines')
    pays = array('Pays')
    expansion_pays = array('ExpansionPays')
    counts = array('counts')
    normals = [symbol for symbol, count in enumerate(counts, 1) for _ in range(count)]
    strips = []
    for reel in range(5):
        strip = [0] * 100
        strip[0] = strip[50] = 10
        strip[5] = 0
        index = 0
        for position in range(100):
            if position not in (0, 5, 50):
                strip[position] = normals[(index * 37 + reel * 19) % 97]
                index += 1
        strips.append(strip)
    return lines, pays, expansion_pays, strips


def base_spin_rtp(lines, pays, strips):
    distributions = {}
    for reel, strip in enumerate(strips):
        windows = [[strip[(stop + row) % 100] for row in range(5)] for stop in range(100)]
        for row in range(5):
            counts = Counter(window[row] for window in windows)
            distributions[reel, row] = tuple(counts[symbol] / 100 for symbol in range(11))

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
        awards[cells] = best / 25

    @lru_cache(None)
    def expectation(dist):
        return sum(value * prod(dist[reel][cells[reel]] for reel in range(5))
                   for cells, value in awards.items())

    return sum(expectation(tuple(distributions[reel, cell % 5]
                                 for reel, cell in enumerate(line)))
               for line in lines)


def expansion_topup(selected, lines, expansion_pays, strips):
    total = 0.0
    for line in lines:
        probabilities = []
        for reel, cell in enumerate(line):
            row = cell % 5
            hits = 0
            strip = strips[reel]
            for stop in range(100):
                window = [strip[(stop + offset) % 100] for offset in range(5)]
                value = window[row]
                if selected in window and value != 10:
                    value = selected
                if value in (0, selected):
                    hits += 1
            probabilities.append(hits / 100)

        first_three = prod(probabilities[:3])
        exact_three = first_three * (1 - probabilities[3])
        exact_four = first_three * probabilities[3] * (1 - probabilities[4])
        exact_five = prod(probabilities)
        pay = expansion_pays[selected]
        total += (exact_three * pay[0] + exact_four * pay[1] + exact_five * pay[2]) / 25
    return total


def audit():
    lines, pays, expansion_pays, strips = profile()
    scatter_windows = [
        sum(10 in [strip[(stop + row) % 100] for row in range(5)] for stop in range(100))
        for strip in strips
    ]
    assert scatter_windows == [10] * 5

    scatter_probabilities = {
        count: comb(5, count) * .1 ** count * .9 ** (5 - count)
        for count in range(6)
    }
    bonus_probability = sum(scatter_probabilities[count] for count in (3, 4, 5))

    @lru_cache(None)
    def expected_spins(remaining, awarded):
        if remaining == 0:
            return 0.0
        added = min(5, 30 - awarded)
        return (1
                + (1 - bonus_probability) * expected_spins(remaining - 1, awarded)
                + bonus_probability * expected_spins(remaining - 1 + added, awarded + added))

    starts = {3: 10, 4: 15, 5: 20}
    expected_bonus_spins = sum(
        scatter_probabilities[count] / bonus_probability
        * expected_spins(starts[count], starts[count])
        for count in starts
    )

    paid = base_spin_rtp(lines, pays, strips)
    expansion = {
        symbol: expansion_topup(symbol, lines, expansion_pays, strips)
        for symbol in range(1, 10)
    }
    free = {symbol: paid + expansion[symbol] for symbol in expansion}
    total = {
        symbol: paid + bonus_probability * expected_bonus_spins * free[symbol]
        for symbol in free
    }

    return {
        'bonus_probability': bonus_probability,
        'mean_paid_spins_per_bonus': 1 / bonus_probability,
        'expected_bonus_spins': expected_bonus_spins,
        'paid_spin_rtp': paid,
        'expansion_topup_by_symbol': expansion,
        'free_spin_rtp_by_symbol': free,
        'total_rtp_by_symbol': total,
        'minimum_total_rtp': min(total.values()),
        'maximum_total_rtp': max(total.values()),
        'beer_total_rtp': total[1],
        'maximum_round_multiplier': (max(row[2] for row in pays) + max(row[2] for row in expansion_pays)) * 31,
        'paytable': pays,
        'expansion_paytable': expansion_pays,
    }


if __name__ == '__main__':
    print(json.dumps(audit(), indent=2))
