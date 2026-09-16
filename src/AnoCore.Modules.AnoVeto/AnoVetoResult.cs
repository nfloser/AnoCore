using AnoCore.Abstractions.Maps;

namespace AnoCore.Modules.AnoVeto;

public enum AnoVetoFailure
{
    None = 0,
    Forbidden = 1,
    NotEnoughMaps = 2,
    AlreadyActive = 3,
    NotActive = 4,
    NotEligible = 5,
    AlreadyVoted = 6,
    InvalidMap = 7,
    VoteRejected = 8,
}

public enum AnoVetoOutcome
{
    None = 0,
    MapSelected = 1,
    QuorumNotMet = 2,
    TieWithoutWinner = 3,
    Cancelled = 4,
}

public sealed record AnoVetoOperationResult(
    bool Accepted,
    AnoVetoFailure Failure,
    AnoVetoOutcome Outcome,
    IReadOnlyList<MapDefinition> Maps,
    MapDefinition? Winner = null)
{
    public static AnoVetoOperationResult Reject(AnoVetoFailure failure)
        => new(false, failure, AnoVetoOutcome.None, []);

    public static AnoVetoOperationResult Success(
        IReadOnlyList<MapDefinition>? maps = null,
        AnoVetoOutcome outcome = AnoVetoOutcome.None,
        MapDefinition? winner = null)
        => new(true, AnoVetoFailure.None, outcome, maps ?? [], winner);
}
