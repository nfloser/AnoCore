using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace AnoCore.Modules.Arcade.Casino;

public enum CasinoGame { Slots, Coinflip, Roulette }
public sealed record CasinoOutcome(CasinoGame Game, int[] Symbols, int Number, bool Won, long Payout, string Summary);
public sealed record RouletteBet(int Choice, int Stake);
public sealed record CasinoRequest(CasinoGame Game, int Stake, int Choice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RouletteBet[]? Bets = null);

/// <summary>Original versioned rules. Payouts include the returned stake.</summary>
public static class CasinoRules
{
    public const string Version = "anocasino-v1";
    public static readonly string[] Symbols = ["cherry", "lemon", "plum", "orange", "grapes", "watermelon", "bell", "seven"];
    private static readonly int[] Multipliers = [12, 22, 36, 55, 100, 150, 300, 1000];
    private static readonly int[] Weights = [30, 25, 20, 15, 10, 7, 4, 1];
    public static decimal SlotsExpectedReturn => Weights.Select((weight, index) =>
        (decimal)weight * weight * weight * Multipliers[index] / (112m * 112m * 112m)).Sum();
    private static readonly int[][] Lines = [[0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]];
    private static readonly HashSet<int> Reds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];
    public static long MaximumPayout(CasinoRequest request)
    {
        Validate(request);
        if (request.Bets is not null)
            return Enumerable.Range(0, 37).Max(number => EvaluateNumber(request, number).Payout);
        return (long)request.Stake * (request.Game == CasinoGame.Slots ? Multipliers.Max()
            : request.Game == CasinoGame.Coinflip ? 2 : request.Choice <= 36 ? 36 : request.Choice >= 43 ? 3 : 2);
    }

    public static void Validate(CasinoRequest request)
    {
        if (!Enum.IsDefined(request.Game) || request.Stake is < 50 or > CasinoStakeLimits.TechnicalMaximum || request.Stake % 50 != 0)
            throw new ArgumentException("Einsatz muss 50–1.000.000 AP in 50er-Schritten sein.");
        if (request.Game == CasinoGame.Coinflip && request.Choice is not (0 or 1)
            || request.Game == CasinoGame.Roulette && request.Choice is < 0 or > 48)
            throw new ArgumentException("Ungültige Auswahl.");
        if (request.Bets is { } bets
            && (request.Game != CasinoGame.Roulette || bets.Length is < 1 or > 49
                || bets.Any(bet => bet is null || bet.Choice is < 0 or > 48
                    || bet.Stake is < 50 or > CasinoStakeLimits.TechnicalMaximum || bet.Stake % 50 != 0)
                || bets.Select(bet => bet.Choice).Distinct().Count() != bets.Length
                || bets.Sum(bet => (long)bet.Stake) != request.Stake))
            throw new ArgumentException("Ungültige Roulette-Einsätze oder Gesamtsumme.");
    }
    public static CasinoOutcome Draw(CasinoRequest request, Func<int, int>? random = null)
    {
        Validate(request);
        random ??= RandomNumberGenerator.GetInt32;
        if (request.Game == CasinoGame.Slots)
        {
            var symbols = Enumerable.Range(0, 9).Select(_ =>
            {
                var pick = random(Weights.Sum());
                for (var i = 0; i < Weights.Length; i++) { pick -= Weights[i]; if (pick < 0) return i; }
                throw new ArgumentException("Invalid random source.");
            }).ToArray();
            return EvaluateSlots(request.Stake, symbols);
        }
        var number = random(request.Game == CasinoGame.Coinflip ? 2 : 37);
        return EvaluateNumber(request, number);
    }
    public static CasinoOutcome EvaluateSlots(int stake, int[] symbols)
    {
        Validate(new(CasinoGame.Slots, stake, 0));
        if (symbols.Length != 9 || symbols.Any(x => x < 0 || x >= Symbols.Length)) throw new ArgumentException("Invalid reel grid.");
        var payout = Lines.Where(line => symbols[line[0]] == symbols[line[1]] && symbols[line[1]] == symbols[line[2]])
            .Sum(line => (long)stake * Multipliers[symbols[line[0]]] / Lines.Length);
        return new(CasinoGame.Slots, (int[])symbols.Clone(), -1, payout > 0, payout,
            payout > 0 ? $"Gewinn: {payout:N0} AP" : "Kein Gewinn");
    }
    // Roulette choices: 0–36 straight number, 37 red, 38 black, 39 even.
    public static CasinoOutcome EvaluateNumber(CasinoRequest request, int number)
    {
        Validate(request);
        if (request.Game == CasinoGame.Slots || number < 0 || number >= (request.Game == CasinoGame.Coinflip ? 2 : 37))
            throw new ArgumentException("Invalid outcome.");
        if (request.Bets is { } bets)
        {
            var payoutTotal = bets.Sum(bet => EvaluateNumber(new(CasinoGame.Roulette, bet.Stake, bet.Choice), number).Payout);
            var display = EvaluateNumber(new(CasinoGame.Roulette, request.Stake, request.Choice), number);
            return display with { Won = payoutTotal > 0, Payout = payoutTotal, Summary = $"{number} · Rückzahlung: {payoutTotal:N0} €" };
        }
        var won = request.Game == CasinoGame.Coinflip ? number == request.Choice : request.Choice switch
        {
            <= 36 => number == request.Choice,
            37 => Reds.Contains(number),
            38 => number != 0 && !Reds.Contains(number),
            39 => number != 0 && number % 2 == 0,
            40 => number % 2 == 1,
            41 => number is >= 1 and <= 18,
            42 => number is >= 19 and <= 36,
            >= 43 and <= 45 => number > 0 && (number - 1) / 12 == request.Choice - 43,
            >= 46 and <= 48 => number > 0 && (number - 1) % 3 == request.Choice - 46,
            _ => false,
        };
        var payout = won ? (long)request.Stake * (request.Game == CasinoGame.Roulette && request.Choice <= 36 ? 36 : request.Choice >= 43 ? 3 : 2) : 0;
        var label = request.Game == CasinoGame.Coinflip ? (number == 0 ? "Kopf" : "Zahl")
            : $"{number} · {(number == 0 ? "Grün" : Reds.Contains(number) ? "Rot" : "Schwarz")}";
        return new(request.Game, [], number, won, payout, $"{label} · Gewinn: {payout:N0} AP");
    }
}
