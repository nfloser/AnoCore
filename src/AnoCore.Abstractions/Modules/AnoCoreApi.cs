namespace AnoCore.Abstractions.Modules;

/// <summary>
/// Version of the stable AnoCore module-facing API contract.
/// </summary>
public static class AnoCoreApi
{
    /// <summary>
    /// API level implied by the original module descriptor constructor. This value never changes.
    /// </summary>
    public const int BaselineLevel = 1;

    /// <summary>
    /// Oldest module API level supported by this runtime generation.
    /// </summary>
    public const int MinimumSupportedLevel = BaselineLevel;

    /// <summary>
    /// Newest module API level exposed by this runtime generation.
    /// </summary>
    public const int CurrentLevel = 2;
}
