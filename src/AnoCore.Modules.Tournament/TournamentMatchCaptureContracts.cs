namespace AnoCore.Modules.Tournament;

public sealed record TournamentDemoSession
{
    public TournamentDemoSession(Guid matchId, int mapIndex, string mapName, long startedRevision)
    {
        if (matchId == Guid.Empty) throw new ArgumentException("A match id is required.", nameof(matchId));
        if (mapIndex < 0) throw new ArgumentOutOfRangeException(nameof(mapIndex));
        if (startedRevision < 1) throw new ArgumentOutOfRangeException(nameof(startedRevision));
        MatchId = matchId;
        MapIndex = mapIndex;
        MapName = NormalizeMap(mapName);
        StartedRevision = startedRevision;
        Id = $"ano_{matchId:N}_m{mapIndex + 1:D2}";
    }

    public Guid MatchId { get; }
    public int MapIndex { get; }
    public string MapName { get; }
    public long StartedRevision { get; }
    public string Id { get; }

    internal static string NormalizeMap(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            throw new ArgumentException("A map name is required.", nameof(mapName));
        var value = mapName.Trim();
        if (value.Length > 128 || value.Any(char.IsControl))
            throw new ArgumentException("The map name is invalid.", nameof(mapName));
        return value;
    }
}

public sealed record TournamentRoundBackup
{
    public TournamentRoundBackup(Guid matchId, int mapIndex, string mapName,
        int roundNumber, long revision)
    {
        if (matchId == Guid.Empty) throw new ArgumentException("A match id is required.", nameof(matchId));
        if (mapIndex < 0) throw new ArgumentOutOfRangeException(nameof(mapIndex));
        if (roundNumber < 0) throw new ArgumentOutOfRangeException(nameof(roundNumber));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        MatchId = matchId;
        MapIndex = mapIndex;
        MapName = TournamentDemoSession.NormalizeMap(mapName);
        RoundNumber = roundNumber;
        Revision = revision;
        Id = $"{matchId:N}:m{mapIndex + 1}:r{roundNumber}:v{revision}";
        NativeName = $"ano_{matchId:N}_m{mapIndex + 1:D2}_r{roundNumber:D3}_v{revision}";
    }

    public Guid MatchId { get; }
    public int MapIndex { get; }
    public string MapName { get; }
    public int RoundNumber { get; }
    public long Revision { get; }
    public string Id { get; }
    public string NativeName { get; }
}

public interface ITournamentMatchCaptureTransport
{
    ValueTask StartDemoAsync(TournamentDemoSession demo,
        CancellationToken cancellationToken = default);

    ValueTask StopDemoAsync(TournamentDemoSession demo,
        CancellationToken cancellationToken = default);

    ValueTask CaptureBackupAsync(TournamentRoundBackup backup,
        CancellationToken cancellationToken = default);

    ValueTask RestoreBackupAsync(TournamentRoundBackup backup,
        CancellationToken cancellationToken = default);
}
