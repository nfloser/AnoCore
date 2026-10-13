namespace AnoCore.Modules.Tournament;

/// <summary>An entrant has a persistent team ID and one-based tournament seed.</summary>
public sealed record TournamentBracketEntrant(string TeamId, int Seed);

/// <summary>A bracket slot is either a known team, a winner dependency, or an opening bye.</summary>
public sealed record TournamentBracketSlot(string? TeamId, string? FromMatchId)
{
    public bool HasValue => TeamId is not null || FromMatchId is not null;
}

public sealed record TournamentBracketMatch(
    string Id, int Round, TournamentBracketSlot Home, TournamentBracketSlot Away);

public sealed record TournamentBracketRound(
    int Number, IReadOnlyList<TournamentBracketMatch> Matches);

public sealed record TournamentBracketPlan(
    int SlotCount,
    IReadOnlyList<TournamentBracketSlot> OpeningSlots,
    IReadOnlyList<TournamentBracketRound> Rounds);

/// <summary>
/// Generates static single-elimination match dependencies without pretending
/// that a bye is an actually played match. Match execution is handled elsewhere.
/// </summary>
public static class TournamentSingleElimination
{
    public static TournamentBracketPlan Generate(
        IReadOnlyCollection<TournamentBracketEntrant> entrants)
    {
        ArgumentNullException.ThrowIfNull(entrants);
        if (entrants.Count is < 3 or > 10)
            throw new ArgumentOutOfRangeException(nameof(entrants),
                "Seeded tournaments require 3-10 teams.");

        var input = entrants.ToArray();
        if (input.Any(entry => entry is null))
            throw new ArgumentException("Bracket entrant cannot be null.", nameof(entrants));
        var ordered = input.OrderBy(entry => entry.Seed).ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seeds = new HashSet<int>();
        foreach (var entrant in ordered)
        {
            if (!ValidId(entrant.TeamId) || !ids.Add(entrant.TeamId)
                || !seeds.Add(entrant.Seed))
                throw new ArgumentException("Bracket entrants need distinct non-empty team IDs and seeds.",
                    nameof(entrants));
        }
        if (!ordered.Select(entry => entry.Seed).SequenceEqual(Enumerable.Range(1, ordered.Length)))
            throw new ArgumentException("Bracket seeds must be consecutive, starting at 1.", nameof(entrants));

        var capacity = 1;
        while (capacity < ordered.Length)
            capacity *= 2;

        // Seed placement protects #1 and #2 in opposite halves, then
        // pairs strong/weak seeds within each section of the bracket.
        var positions = new List<int> { 1 };
        while (positions.Count < capacity)
        {
            var total = positions.Count * 2 + 1;
            positions = positions.SelectMany(seed => new[] { seed, total - seed }).ToList();
        }

        var opening = positions.Select(seed => new TournamentBracketSlot(
            seed <= ordered.Length ? ordered[seed - 1].TeamId : null, null)).ToList();
        var current = opening.ToList();
        var rounds = new List<TournamentBracketRound>();

        for (var round = 1; current.Count > 1; round++)
        {
            var matches = new List<TournamentBracketMatch>();
            var advanced = new List<TournamentBracketSlot>();
            for (var index = 0; index < current.Count; index += 2)
            {
                var home = current[index];
                var away = current[index + 1];
                if (!home.HasValue && !away.HasValue)
                    throw new InvalidOperationException("Both sides of a bracket pairing are empty.");
                if (!home.HasValue || !away.HasValue)
                {
                    // The higher seed advances because there is no opponent.
                    advanced.Add(home.HasValue ? home : away);
                    continue;
                }

                var id = $"r{round}-m{matches.Count + 1}";
                matches.Add(new TournamentBracketMatch(id, round, home, away));
                advanced.Add(new TournamentBracketSlot(null, id));
            }
            rounds.Add(new TournamentBracketRound(round, matches.AsReadOnly()));
            current = advanced;
        }

        if (current.Count != 1 || current[0].FromMatchId is null)
            throw new InvalidOperationException("Bracket does not have a final match winner.");
        return new TournamentBracketPlan(capacity, opening.AsReadOnly(), rounds.AsReadOnly());
    }

    private static bool ValidId(string? value)
        => !string.IsNullOrEmpty(value) && value.Length <= 48
            && value.All(character => (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9') || character is '-' or '_')
            && value[0] is not ('-' or '_');
}
