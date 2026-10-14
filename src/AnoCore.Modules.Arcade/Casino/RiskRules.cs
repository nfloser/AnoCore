namespace AnoCore.Modules.Arcade.Casino;

/// <summary>The terminal disposition of a Risk round.</summary>
public enum RiskRoundStatus
{
    Active,
    Banked,
    Lost,
}

/// <summary>
/// Serializable immutable snapshot of a Risk round. Revisions increase once per accepted action;
/// callers should persist the returned state with an optimistic compare against the prior revision.
/// </summary>
public sealed record RiskRoundState(
    string RulesVersion,
    long Revision,
    long HeldPayout,
    int Gambles,
    RiskRoundStatus Status,
    long? FinalPayout);

/// <summary>
/// Pure rules for anocasino-risk-v1. Randomness is supplied by the authoritative caller as one
/// already-drawn fair bit; this class never draws randomness or performs persistence.
/// </summary>
public static class RiskRules
{
    public const string Version = "anocasino-risk-v1";
    public const int WinOddsPercent = 50;
    public const int MaximumGambles = 5;
    public const long MaximumHeldExposure = 1_000_000;

    /// <summary>Create a round around the already-held payout supplied by the integrating game.</summary>
    public static RiskRoundState Start(long heldPayout)
    {
        if (heldPayout is < 1 or > MaximumHeldExposure)
            throw new ArgumentOutOfRangeException(nameof(heldPayout), $"Held payout must be between 1 and {MaximumHeldExposure} AP.");

        return new(Version, 0, heldPayout, 0, RiskRoundStatus.Active, null);
    }

    /// <summary>Accept a server-authoritative coin result: win doubles the held amount; loss pays zero.</summary>
    public static RiskRoundState Double(RiskRoundState state, long expectedRevision, bool won)
    {
        ValidateAction(state, expectedRevision);
        if (state.Gambles >= MaximumGambles)
            throw new InvalidOperationException("The maximum number of gambles has been reached; only Bank remains legal.");
        if (state.HeldPayout > MaximumHeldExposure / 2)
            throw new InvalidOperationException("Doubling would exceed the maximum held exposure.");

        var nextRevision = checked(state.Revision + 1);
        if (!won)
            return state with { Revision = nextRevision, HeldPayout = 0, Gambles = state.Gambles + 1,
                Status = RiskRoundStatus.Lost, FinalPayout = 0 };

        return state with { Revision = nextRevision, HeldPayout = checked(state.HeldPayout * 2), Gambles = state.Gambles + 1 };
    }

    /// <summary>Bank the exact currently held payout and make the round terminal.</summary>
    public static RiskRoundState Bank(RiskRoundState state, long expectedRevision)
    {
        ValidateAction(state, expectedRevision);
        return state with { Revision = checked(state.Revision + 1), Status = RiskRoundStatus.Banked,
            FinalPayout = state.HeldPayout };
    }

    private static void ValidateAction(RiskRoundState state, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.RulesVersion != Version)
            throw new InvalidOperationException("The state belongs to a different Risk rules version.");
        if (state.Revision != expectedRevision)
            throw new InvalidOperationException("The Risk state revision is stale.");
        if (state.Status != RiskRoundStatus.Active)
            throw new InvalidOperationException("The Risk round is terminal; actions cannot be replayed.");
        if (state.Revision < 0 || state.Gambles is < 0 or > MaximumGambles
            || state.HeldPayout is < 1 or > MaximumHeldExposure || state.FinalPayout is not null)
            throw new InvalidOperationException("The Risk state is invalid.");
    }
}
