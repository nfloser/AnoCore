namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Immutable stop distributions; physical reels and paytable stay identical.</summary>
public static class BiggestBoyProfiles
{
    public const string Standard = "standard";
    public const string Frequent = "frequent";
    public const string Generous = "generous";
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { Standard, Frequent, Generous });

    public static string Validate(string profile) => Names.Contains(profile)
        ? profile : throw new ArgumentException("BiggestBoyProfile must be standard, frequent or generous.", nameof(profile));

    public static int StopWeight(string profile, int reel, int stop)
    {
        Validate(profile);
        if (reel is < 0 or >= 5 || stop is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(stop));
        var strip = BiggestBoyExpandingRules.ReelStrips[reel];
        return profile switch
        {
            Frequent => strip[stop] is >= BiggestBoySymbol.G and <= BiggestBoySymbol.T ? 2 : 1,
            Generous => Enumerable.Range(0, 3).Any(row => strip[(stop + row) % 100] is BiggestBoySymbol.Book or BiggestBoySymbol.Scatter) ? 11 : 10,
            _ => 1,
        };
    }

    private static readonly Lazy<IReadOnlyDictionary<string, int[][]>> Tickets = new(() =>
        Names.ToDictionary(name => name, name => Enumerable.Range(0, 5).Select(reel =>
            Enumerable.Range(0, 100).SelectMany(stop => Enumerable.Repeat(stop, StopWeight(name, reel, stop))).ToArray()).ToArray()));

    public static int SelectStop(string profile, int reel, Func<int, int> random)
    {
        Validate(profile);
        ArgumentNullException.ThrowIfNull(random);
        if (reel is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(reel));
        var tickets = Tickets.Value[profile][reel];
        var ticket = random(tickets.Length);
        if (ticket < 0 || ticket >= tickets.Length) throw new ArgumentException("Invalid random draw.", nameof(random));
        return tickets[ticket];
    }
}
