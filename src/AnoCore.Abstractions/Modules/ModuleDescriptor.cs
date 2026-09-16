namespace AnoCore.Abstractions.Modules;

public sealed record ModuleDescriptor
{
    public ModuleDescriptor(ModuleId id, string name, string version, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A module name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A module version is required.", nameof(version));
        }

        Id = id;
        Name = name.Trim();
        Version = version.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    public ModuleId Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string Description { get; }
}
