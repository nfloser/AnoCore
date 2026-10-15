using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

var count = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 2_000_000;
var seed = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 20261015;
if (count is < 1000 or > 100_000_000) throw new ArgumentOutOfRangeException(nameof(count));
var profile = BiggestBoyProfiles.Validate(args.Length > 2 ? args[2] : "standard");
var random = new Random(seed);
double mean = 0, m2 = 0;
long hits = 0;
long bonusCount = 0, freeCount = 0, paidTotal = 0, returned = 0;
var expansionCounts = new long[10];
for (var i = 1; i <= count; i++)
{
    var spin = BiggestBoyExpandingRules.StartRound(100, random.Next, profile);
    if (spin.Payout > 0) hits++;
    paidTotal += spin.ChargedStake;
    if (spin.State.FreeSpinsRemaining > 0) { bonusCount++; expansionCounts[(int)spin.State.ExpandingSymbol!.Value]++; }
    var actualFreeSpins = 0;
    while (spin.State.FreeSpinsRemaining > 0)
    {
        spin = BiggestBoyExpandingRules.PlayFreeSpin(spin.State, random.Next);
        if (spin.ChargedStake != 0 || ++actualFreeSpins > 30) throw new InvalidOperationException("Bonus accounting invariant failed.");
        freeCount++;
    }
    if (spin.State.TotalPayout > BiggestBoyExpandingRules.MaximumRoundExposure(100)) throw new InvalidOperationException("Exposure exceeded.");
    returned += spin.State.TotalPayout;
    var value = spin.State.TotalPayout / 100.0;
    var delta = value - mean; mean += delta / i; m2 += delta * (value - mean);
}
var se = Math.Sqrt(m2 / (count - 1) / count);
Console.WriteLine(JsonSerializer.Serialize(new
{
    profile,
    paid_hit_frequency = hits / (double)count,
    version = BiggestBoyExpandingRules.Version,
    seed,
    paid_spins = count,
    paid_total = paidTotal,
    returned_total = returned,
    empirical_rtp = mean,
    standard_error = se,
    confidence95_low = mean - 1.96 * se,
    confidence95_high = mean + 1.96 * se,
    bonus_count = bonusCount,
    free_spins_played = freeCount,
    expanding_selections = expansionCounts,
}, new JsonSerializerOptions { WriteIndented = true }));
