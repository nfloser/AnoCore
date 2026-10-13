namespace AnoCore.Modules.Tournament;

/// <summary>Fixed, server-local JSON import document. Database remains authoritative.</summary>
public sealed class TournamentEventImportConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public TournamentEventDefinition Event { get; set; } = new();

    public static string Fingerprint(TournamentEventImportConfiguration value) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)));

    public static IReadOnlyCollection<string> Validate(TournamentEventImportConfiguration? value)
    {
        if (value is null) return ["Tournament event import configuration is required."];
        if (value.SchemaVersion != 1) return ["Unsupported tournament schema version; expected 1."];
        return value.Enabled ? TournamentEventDefinition.Validate(value.Event) : [];
    }
}
