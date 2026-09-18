using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Targeting;

[Flags]
public enum TargetSelectorCapabilities
{
    None = 0,
    Self = 1 << 0,
    All = 1 << 1,
    Team = 1 << 2,
}

public enum TargetResolutionFailure
{
    None = 0,
    EmptySelector = 1,
    CallerRequired = 2,
    SelectorNotAllowed = 3,
    NotFound = 4,
    Ambiguous = 5,
}

public sealed record TargetResolutionResult(
    bool Accepted,
    TargetResolutionFailure Failure,
    IReadOnlyList<PlayerSnapshot> Targets)
{
    public static TargetResolutionResult Success(IEnumerable<PlayerSnapshot> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return new TargetResolutionResult(true, TargetResolutionFailure.None, targets.ToArray());
    }

    public static TargetResolutionResult Reject(TargetResolutionFailure failure)
    {
        if (failure == TargetResolutionFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A rejected target resolution requires a failure reason.");
        }

        return new TargetResolutionResult(false, failure, []);
    }
}

public interface IPlayerTargetResolver
{
    TargetResolutionResult Resolve(
        string selector,
        PlayerId? caller = null,
        TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.None);
}
