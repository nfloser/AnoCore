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
