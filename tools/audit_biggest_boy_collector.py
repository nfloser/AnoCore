"""Exact pre-ceiling expectation for v3, not a claim of certified commercial RTP.

The published 5,000x round ceiling can only reduce this expectation. Window,
collector/money correlation and retrigger stage transitions are included exactly.
"""
import ast
from collections import Counter
from functools import lru_cache
from itertools import product
import json
from pathlib import Path
import re
from audit_biggest_boy import profile, base_spin_rtp

ROOT = Path(__file__).resolve().parents[1]


def audit():
    source = (ROOT / 'src/AnoCore.Modules.Arcade/Casino/BiggestBoyCollectorRules.cs').read_text()
    lines, pays = [ast.literal_eval(re.search(rf'{name}\s*=\s*(\[.*?\]);', source, re.S)[1])
                   for name in ('Lines', 'Pays')]
    strips = profile()[3]
    # The legacy exact evaluator divides each line by 25 and uses 5-row indexing.
    paid = base_spin_rtp([[reel * 5 + cell % 3 for reel, cell in enumerate(line)]
                          for line in lines], pays, strips) * 25 / 10
    windows = [[[strip[(stop + row) % 100] for row in range(3)]
                for stop in range(100)] for strip in strips]
    per_reel = [Counter((w.count(0), w.count(1), w.count(10)) for w in reel) for reel in windows]
    assert all(max(key[0] for key in reel) == 1 for reel in per_reel)
    joint = Counter({(0, 0, 0): 1.0})
    for reel in per_reel:
        nxt = Counter()
        for (w, m, s), chance in joint.items():
            for (rw, rm, rs), count in reel.items():
                nxt[w + rw, m + rm, s + rs] += chance * count / 100
        joint = nxt
    wilds = Counter()
    scatters = Counter()
    collect = 0.0
    mean_money = .8 + .18 * 2 + .02 * 5
    for (w, m, s), chance in joint.items():
        wilds[w] += chance
        scatters[s] += chance
        collect += chance * w * m * mean_money

    def multiplier(c):
        return 1 if c < 4 else 2 if c < 8 else 3 if c < 12 else 10

    @lru_cache(None)
    def bonus(remaining, collected):
        if remaining == 0:
            return 0.0, 0.0
        reward = paid + collect * multiplier(collected)
        spins = 1.0
        for count, chance in wilds.items():
            nxt = min(12, collected + count)
            extra = (nxt // 4 - collected // 4) * 10
            future, duration = bonus(remaining - 1 + extra, nxt)
            reward += chance * future
            spins += chance * duration
        return reward, spins

    trigger = sum(chance for count, chance in scatters.items() if count >= 3)
    total = paid
    expected_spins = 0.0
    for count, initial in [(3, 10), (4, 15), (5, 20)]:
        reward, spins = bonus(initial, 0)
        total += scatters[count] * reward
        expected_spins += scatters[count] * spins / trigger
    return dict(profile='5x3-collector-v3', paid_spin_rtp=paid,
                bonus_probability=trigger, expected_bonus_spins_before_ceiling=expected_spins,
                mean_collection_at_x1=collect, uncapped_rtp_upper_bound=total,
                note='Actual RTP is lower due to published 5000x round ceiling; not original Big Bass odds.')


if __name__ == '__main__':
    print(json.dumps(audit(), indent=2))
