using System.Globalization;
using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

var count = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 100_000;
var seed = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 20261015;
if (count is < 1000 or > 10_000_000) throw new ArgumentOutOfRangeException(nameof(count));
foreach (var biggest in new[] { true, false })
{
    var random = new Random(seed);
    var settings = new SlotOutcomeSettings(18, biggest ? 8 : 5, 1);
    long total = 0, wins = 0, bonuses = 0, free = 0, maximum = 0;
    double mean = 0, m2 = 0;
    for (var i = 1; i <= count; i++)
    {
        long payout;
        if (biggest)
        {
            var spin = ControlledSlotRules.Biggest(100, random.Next, settings);
            if (spin.Payout > 100) wins++;
            if (spin.State.FreeSpinsRemaining > 0) bonuses++;
            while (spin.State.FreeSpinsRemaining > 0)
            {
                spin = ControlledSlotRules.Biggest(100, random.Next, settings, spin.State);
                if (spin.ChargedStake != 0) throw new InvalidOperationException("Free spin charged.");
                free++;
            }
            payout = spin.State.TotalPayout;
        }
        else
        {
            var spin = ControlledSlotRules.Book(100, random.Next, settings);
            if (spin.Payout > 100) wins++;
            if (spin.State.FreeSpinsRemaining > 0) bonuses++;
            while (spin.State.FreeSpinsRemaining > 0)
            {
                spin = ControlledSlotRules.Book(100, random.Next, settings, spin.State);
                if (spin.ChargedStake != 0) throw new InvalidOperationException("Free spin charged.");
                free++;
            }
            payout = spin.State.TotalPayout;
        }
        total += payout; maximum = Math.Max(maximum, payout);
        var value = payout / 100.0;
        var delta = value - mean; mean += delta / i; m2 += delta * (value - mean);
    }
    var se = Math.Sqrt(m2 / (count - 1) / count);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        game = biggest ? "BiggestBoy" : "Book", version = biggest ? ControlledSlotRules.BiggestVersion : ControlledSlotRules.BookVersion,
        settings, count, seed, total_staked = count * 100L, total_returned = total,
        empirical_return = mean, confidence95_low = mean - 1.96 * se, confidence95_high = mean + 1.96 * se,
        paid_net_win_frequency = wins / (double)count, bonus_frequency = bonuses / (double)count,
        free_spins = free, largest_round_multiple = maximum / 100.0
    }));
}
