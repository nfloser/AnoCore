namespace AnoCore.Modules.Tournament;

/// <summary>Fixed, server-local JSON import document. Database remains authoritative.</summary>
public sealed class TournamentEventImportConfiguration
{
    public bool Enabled { get; set; }
    public TournamentEventDefinition Event { get; set; } = new();

    public static IReadOnlyCollection<string> Validate(TournamentEventImportConfiguration? value)
    {
        if (value is null) return ["Tournament event import configuration is required."];
        return value.Enabled ? TournamentEventDefinition.Validate(value.Event) : [];
    }
}
