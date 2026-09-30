namespace AnoCore.Abstractions.Modules;

public sealed record ModuleDescriptor
{
    public ModuleDescriptor(ModuleId id, string name, string version, string description)
        : this(id, name, version, description, AnoCoreApi.BaselineLevel)
    {
    }

    public ModuleDescriptor(
        ModuleId id,
        string name,
        string version,
        string description,
        int minimumApiLevel)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A module name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A module version is required.", nameof(version));
        }

        if (minimumApiLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumApiLevel),
                "A module API level must be greater than zero.");
        }

        Id = id;
        Name = name.Trim();
        Version = version.Trim();
        Description = description?.Trim() ?? string.Empty;
        MinimumApiLevel = minimumApiLevel;
    }

    public ModuleId Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string Description { get; }

    /// <summary>
    /// Minimum AnoCore module API level required before this module may initialize.
    /// </summary>
    public int MinimumApiLevel { get; }
}
