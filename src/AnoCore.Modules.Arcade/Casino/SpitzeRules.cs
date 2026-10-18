namespace AnoCore.Modules.Arcade.Casino;

/// <summary>The five CS-inspired outcomes plus the empty reel gap.</summary>
public enum SpitzeSymbol { Loss, SafariMesh, NeonRevolution, Gold, Knife, Blank }
public enum SpitzeRoundStatus { Active, Collected, Completed }

/// <summary>Versioned durable single-reel state. All winnings are virtual €.</summary>
public sealed record SpitzeRoundState(
    string RulesVersion, long Revision, int Stake, int PaidSpins,
    int Low, int Middle, int High, long DirectWin, int ActionGamesRemaining,
    bool ActionGamesTriggered, SpitzeSymbol LastSymbol, SpitzeRoundStatus Status, long? FinalPayout);

/// <summary>
/// One reel / three five-level towers; randomness is drawn by the transaction caller.
/// The weights and 100-spin safety bound are AnoCasino values, not original Merkur odds.
/// </summary>
public static class SpitzeRules
{
    public const string Version = "anocasino-spitze-v1";
    public const int MaximumPaidSpins = 100;
    public const int BonusSpins = 7;
    public const int MaximumStake = CasinoStakeLimits.TechnicalMaximum;

    private static readonly int[] LowValues = [1, 2, 4, 7, 10];
    private static readonly int[] MiddleValues = [3, 8, 16, 29, 50];
    private static readonly int[] HighValues = [10, 40, 100, 200, 350];

    /// <summary>Pinned server-side distribution, with genuine empty stops.</summary>
    public static SpitzeSymbol FromRoll(int roll) => roll switch
    {
        < 0 or >= 100 => throw new ArgumentOutOfRangeException(nameof(roll)),
        < 16 => SpitzeSymbol.Loss,
        < 43 => SpitzeSymbol.SafariMesh,
        < 64 => SpitzeSymbol.NeonRevolution,
        < 74 => SpitzeSymbol.Gold,
        < 84 => SpitzeSymbol.Knife,
        _ => SpitzeSymbol.Blank,
    };

    public static long MaximumExposure(int stake)
    {
        ValidateStake(stake);
        // One potential 410x peak win per spin, plus one full cashpot.
        // Conservative upper bound for every payable outcome of one round.
        return checked((long)(MaximumPaidSpins + BonusSpins + 1) * 410 * stake);
    }

    public static long Cashpot(SpitzeRoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return checked((long)state.Stake * (Value(LowValues, state.Low)
            + Value(MiddleValues, state.Middle) + Value(HighValues, state.High)));
    }

    public static SpitzeRoundState Start(int stake, SpitzeSymbol firstSymbol)
    {
        ValidateStake(stake);
        var start = new SpitzeRoundState(Version, 0, stake, 0, 0, 0, 0, 0, 0,
            false, SpitzeSymbol.Blank, SpitzeRoundStatus.Active, null);
        return Advance(start, 0, firstSymbol, false);
    }

    public static SpitzeRoundState Spin(SpitzeRoundState state, long revision, SpitzeSymbol symbol)
    {
        Validate(state, revision);
        if (state.ActionGamesRemaining > 0)
            throw new InvalidOperationException("Action Games zuerst abschließen oder einsammeln.");
        if (state.PaidSpins >= MaximumPaidSpins)
            throw new InvalidOperationException("Rundenlimit erreicht.");
        return Advance(state, revision, symbol, false);
    }

    public static SpitzeRoundState FreeSpin(SpitzeRoundState state, long revision, SpitzeSymbol symbol)
    {
        Validate(state, revision);
        if (state.ActionGamesRemaining <= 0)
            throw new InvalidOperationException("Keine Action Games verfügbar.");
        return Advance(state, revision, symbol, true);
    }

    public static SpitzeRoundState Collect(SpitzeRoundState state, long revision)
    {
        Validate(state, revision);
        return state with
        {
            Revision = checked(state.Revision + 1),
            FinalPayout = checked(state.DirectWin + Cashpot(state)),
            Status = SpitzeRoundStatus.Collected,
        };
    }

    private static SpitzeRoundState Advance(SpitzeRoundState state, long revision, SpitzeSymbol symbol, bool free)
    {
        if (!Enum.IsDefined(symbol)) throw new ArgumentOutOfRangeException(nameof(symbol));
        var low = state.Low;
        var middle = state.Middle;
        var high = state.High;
        var direct = state.DirectWin;
        if (symbol == SpitzeSymbol.Loss)
        {
            low = middle = high = 0;
        }
        else
        {
            if (symbol is SpitzeSymbol.SafariMesh or SpitzeSymbol.Knife)
                Increase(ref low, LowValues, state.Stake, ref direct);
            if (symbol is SpitzeSymbol.NeonRevolution or SpitzeSymbol.Knife)
                Increase(ref middle, MiddleValues, state.Stake, ref direct);
            if (symbol is SpitzeSymbol.Gold or SpitzeSymbol.Knife)
                Increase(ref high, HighValues, state.Stake, ref direct);
        }

        var triggered = state.ActionGamesTriggered || low == 5 && middle == 5 && high == 5;
        var games = free ? state.ActionGamesRemaining - 1 : state.ActionGamesRemaining;
        if (!state.ActionGamesTriggered && triggered) games = BonusSpins;
        var spins = state.PaidSpins + (free ? 0 : 1);
        var complete = games == 0 && (spins == MaximumPaidSpins || free);
        var next = state with
        {
            Revision = checked(revision + 1),
            PaidSpins = spins,
            Low = low,
            Middle = middle,
            High = high,
            DirectWin = direct,
            ActionGamesRemaining = games,
            ActionGamesTriggered = triggered,
            LastSymbol = symbol,
            Status = complete ? SpitzeRoundStatus.Completed : SpitzeRoundStatus.Active,
        };
        return complete ? next with { FinalPayout = checked(next.DirectWin + Cashpot(next)) } : next;
    }

    private static void Increase(ref int level, int[] values, int stake, ref long direct)
    {
        if (level < values.Length) level++;
        if (level == values.Length)
            direct = checked(direct + (long)values[^1] * stake);
    }

    private static int Value(int[] values, int level) => level > 0 && level <= values.Length ? values[level - 1] : 0;

    private static void Validate(SpitzeRoundState state, long revision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateStake(state.Stake);
        if (state.RulesVersion != Version || state.Revision != revision || revision < 1
            || state.Status != SpitzeRoundStatus.Active || state.FinalPayout is not null
            || state.PaidSpins is < 1 or > MaximumPaidSpins
            || state.Low is < 0 or > 5 || state.Middle is < 0 or > 5 || state.High is < 0 or > 5
            || state.ActionGamesRemaining is < 0 or > BonusSpins
            || (!state.ActionGamesTriggered && state.ActionGamesRemaining != 0)
            || state.DirectWin < 0 || state.DirectWin > MaximumExposure(state.Stake)
            || !Enum.IsDefined(state.LastSymbol))
            throw new InvalidOperationException("Gespeicherte Alles-Spitze-Runde ist ungültig oder veraltet.");
    }

    private static void ValidateStake(int stake)
    {
        if (stake is < 50 or > MaximumStake || stake % 50 != 0)
            throw new ArgumentOutOfRangeException(nameof(stake));
    }
}
