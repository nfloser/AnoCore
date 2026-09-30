namespace AnoCore.Abstractions.Modules;

/// <summary>
/// Version of the stable AnoCore module-facing API contract.
/// </summary>
public static class AnoCoreApi
{
    /// <summary>
    /// Oldest module API level supported by this runtime generation.
    /// </summary>
    public const int MinimumSupportedLevel = 1;

    /// <summary>
    /// Newest module API level exposed by this runtime generation.
    /// </summary>
    public const int CurrentLevel = 1;
}
