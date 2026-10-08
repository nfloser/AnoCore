namespace AnoCore.Modules.Stats;

public sealed class CombatRecordingConfiguration
{
    public bool RecordWeaponFire { get; set; } = true;
    public bool RecordDamage { get; set; } = true;
    public bool BatchWrites { get; set; } = true;
    public int Capacity { get; set; } = 8192;
    public int BatchSize { get; set; } = 256;
    public double FlushIntervalSeconds { get; set; } = 1;
    public int ShutdownTimeoutSeconds { get; set; } = 5;

    public static IReadOnlyCollection<string> Validate(CombatRecordingConfiguration configuration)
    {
        if (configuration is null) return ["Combat recording configuration is required."];
        var errors = new List<string>();
        if (configuration.BatchSize is < 1 or > 256) errors.Add("BatchSize must be between 1 and 256.");
        if (configuration.Capacity < configuration.BatchSize || configuration.Capacity is < 1 or > 65536)
            errors.Add("Capacity must cover BatchSize and be between 1 and 65536.");
        if (!double.IsFinite(configuration.FlushIntervalSeconds)
            || configuration.FlushIntervalSeconds is < 0.1 or > 30)
            errors.Add("FlushIntervalSeconds must be between 0.1 and 30.");
        if (configuration.ShutdownTimeoutSeconds is < 1 or > 30)
            errors.Add("ShutdownTimeoutSeconds must be between 1 and 30.");
        return errors;
    }
}
