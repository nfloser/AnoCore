namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Starting-stake policy. Accepted actions and bonuses retain their booked stake.</summary>
public sealed record CasinoStakeLimits
{
    public const int TechnicalMaximum = 1_000_000;
    public int Slots { get; init; } = 500;
    public int Coinflip { get; init; } = 500;
    public int Roulette { get; init; } = 500;
    public int Blackjack { get; init; } = 500;
    public int Risk { get; init; } = 500;
    public int Book { get; init; } = 10_000;

    private static readonly int[] BookSteps = [50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000];

    /// <summary>Convenient fruit-slot stakes, including an administrator's nonstandard ceiling.</summary>
    public int StepBookStake(int current, int direction)
    {
        Validate();
        var steps = BookSteps.Where(value => value < Book).Append(Book).ToArray();
        return direction > 0 ? steps.FirstOrDefault(value => value > current, Book)
            : direction < 0 ? steps.LastOrDefault(value => value < current, 50)
            : Math.Clamp(current, 50, Book);
    }

    public CasinoStakeLimits Validate()
    {
        foreach (var value in new[] { Slots, Coinflip, Roulette, Blackjack, Risk, Book })
            if (value is < 50 or > TechnicalMaximum || value % 50 != 0)
                throw new ArgumentException("CasinoMaximumStakes values must be 50..1000000 in steps of 50.");
        return this;
    }
    public int For(CasinoGame game) => game switch
    {
        CasinoGame.Slots => Slots,
        CasinoGame.Coinflip => Coinflip,
        CasinoGame.Roulette => Roulette,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
    public int For(CasinoRoundGame game) => game switch
    {
        CasinoRoundGame.Blackjack => Blackjack,
        CasinoRoundGame.Risk => Risk,
        CasinoRoundGame.Book => Book,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
    public void Check(CasinoRequest request)
    {
        CasinoRules.Validate(request);
        if (request.Stake > For(request.Game)) throw new CasinoRejectedException("Konfiguriertes Einsatzlimit überschritten.");
    }
}
