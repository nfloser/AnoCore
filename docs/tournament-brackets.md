# Static single-elimination bracket generation

Tracking: [#385](https://github.com/nfloser/AnoCore/issues/385), a first
subset of [#379](https://github.com/nfloser/AnoCore/issues/379).

The engine-independent `TournamentSingleElimination` generator accepts
**three through ten uniquely seeded teams** and returns deterministic
bracket rounds and winner-of-match references. This is an additional planning
building block and does **not** start matches or update Panorama yet.

## Placement and byes

The bracket expands to the next power of two (4/8/16 slots).
Seed placement distributes strongest seeds over opposite halves;
an empty opponent slot is a **bye**, not a fictitious match victory.

For example, with **five teams** the eight opening seed slots are:

```text
Quarter A:  #1 -- bye          -> semifinal A
            #4 -- #5          -> semifinal A

Quarter B:  #2 -- bye          -> semifinal B
            #3 -- bye          -> semifinal B

Grand final: winner of semifinal A vs winner of semifinal B
```

This produces four actual contests, as required for five entrants.
Three teams produce a preliminary #2/#3 match and #1 receives a bye into
the final. Ten teams use a 16-slot bracket with six opening byes and
nine actual contests. No team is eliminated by an unplayed round.

## Contracts and safety

- `TournamentBracketEntrant`: stable team ID and one-based seed.
- `TournamentBracketSlot`: a known team ID, a prior winner reference, or
  an empty opening slot.
- `TournamentBracketMatch`: uniquely identified match with home/away sources.
- `TournamentBracketPlan`: power-of-two opening slots plus ordered rounds.

Input order never affects bracket output; seeds must be unique and
consecutive 1..N. Every dependency references a **prior** round.
Team display names are not used as persistent match keys.

This generator explicitly implements **static single elimination**.
Dynamic reseeding, double elimination, Swiss stages, advancement from
completed group standings and championship resets are separate work
in #379. It is not a substitute for a match execution or persistence
engine; those must reject stale bracket results and support transactional
advancement.

## Verification

MSTest covers every count 3–10, unique seeded slots and match IDs,
seed #1/#2 separation, appropriate opening byes, total contest count
`N - 1`, dependency ordering, reproducibility and invalid seeds.
Live CS2/DatHost and Workshop UI acceptance comes with #381.

## Confirmed-result progression

`TournamentBracketProgression.Calculate(entrants, results)` rebuilds the generated
single-elimination graph in round order. Each match reports resolved home/away
team IDs, its confirmed winner and whether it is ready to play. The final winner
is the champion; byes advance known teams without inventing results.

Results use stable match IDs, winner team IDs and winner/loser map totals (decisive,
nonnegative, at most three winner maps for BO1/BO3/BO5). Unknown or duplicate
matches, nonparticipant winners and results with unresolved dependencies are
rejected. Input order is irrelevant; changing an upstream winner invalidates
incompatible downstream results rather than silently transferring them.

This is a domain snapshot for future persistence, presenter and match-capture
integration. It does not yet persist playoff results or start a live CS2 match.

## Shared playoff results and operator commands

The shared event store now retains playoff results beside group results.
Migration 23 initializes existing events with an empty playoff result list.
Both writes use the event revision: concurrent group/playoff edits reject stale
writes, and a group import preserves and revalidates recorded playoff results.

```text
css_anotournamenteventbracket
css_anotournamenteventplayoffresult r1-m1 team-2 2 0
```

The first command shows stable match IDs, resolved participants, waiting/ready
pairings and the champion. The second confirms winner/loser map totals, requires
`ano.tournament.manage`, and records request/completion audit entries. It rejects
duplicates, premature finals and unrelated winners. Completion-audit failure
reports that the result was saved; do not submit it again.

Playoff seeds currently come from `InitialSeed`, then stable team IDs for unseeded
teams. Automatic group qualification and format-specific BO limits are separate
stage policy; this command does not assert group completion or start a CS2 match.

## Groups automatically qualifying for playoffs

Optionally set `PlayoffQualifiersPerGroup` in `tournament-event.json` (for example,
`2`). Leaving it absent preserves the existing direct seeded knockout plan.
Each group must contain enough teams and the total qualified field must be 2-10.
A three-team group can therefore qualify two finalists without changing the
3-10 registered-team event limit.

While any scheduled group fixture lacks a confirmed result, the playoff view
waits and playoff results are rejected. Once every fixture is confirmed, the
existing standings/tie-break rules select each group's top teams. Seeds are
assigned by finishing position, then stable group ID, without a separate manual
promotion command. A two-team final is one real contest with no fictitious bye.

After any playoff result is recorded, group/rule edits that would change the
qualified field or its seeds are rejected. Display-name edits remain possible.
This is static seeding; cross-group rematch avoidance and round reseeding are
not yet configurable policies.
