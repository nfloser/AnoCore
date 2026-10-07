using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public enum GameplayStatKind : byte
{
    GrenadeThrown = 1,
    BombPlanted = 2,
    BombDefused = 3,
    HostageRescued = 4,
    HostageKilled = 5,
    Mvp = 6,
    RoundPlayed = 7,
    RoundWon = 8,
    RoundLost = 9,
    RoundTerrorist = 10,
    RoundCounterTerrorist = 11,
    MatchWon = 12,
    MatchLost = 13,
    FirstBlood = 14,
    HeadshotKill = 15,
    NoScopeKill = 16,
    PenetratedKill = 17,
    ThroughSmokeKill = 18,
    FlashedKill = 19,
    DominatedKill = 20,
    RevengeKill = 21,
    FlashAssist = 22,
    PlaytimeInterval = 23,
    BombDropped = 24,
    BombPickedUp = 25,
    BombExploded = 26,
    BombDefusedOthers = 27,
    HostageHurt = 28,
    HostagesRescuedAll = 29,
    GrenadeKill = 30,
    InfernoKill = 31,
    ImpactKill = 32,
    KnifeKill = 33,
    TaserKill = 34,
}

public sealed record GameplayStatEvent
{
    public GameplayStatEvent(Guid eventId, PlayerId playerId, DateTimeOffset occurredAtUtc,
        string mapName, GameplayStatKind kind, int amount = 1)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A gameplay statistic event id is required.", nameof(eventId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (amount is < 1 or > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(amount));

        EventId = eventId;
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        var utc = occurredAtUtc.ToUniversalTime();
        OccurredAtUtc = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        MapName = GameplayStatValidation.NormalizeMap(mapName, nameof(mapName));
        Kind = kind;
        Amount = amount;
    }

    public Guid EventId { get; }
    public PlayerId PlayerId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string MapName { get; }
    public GameplayStatKind Kind { get; }
    public int Amount { get; }
}

public sealed record GameplayStatFilter
{
    public GameplayStatFilter(string? mapName = null)
        => MapName = string.IsNullOrWhiteSpace(mapName)
            ? null
            : GameplayStatValidation.NormalizeMap(mapName, nameof(mapName));

    public string? MapName { get; }
}

public sealed record GameplayStatTotal(GameplayStatKind Kind, long Count);

public interface IGameplayStatRepository
{
    ValueTask RecordAsync(GameplayStatEvent statistic,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(PlayerId playerId,
        GameplayStatFilter? filter = null,
        CancellationToken cancellationToken = default);
}

internal static class GameplayStatValidation
{
    public static string NormalizeMap(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A map name is required.", paramName);

        var normalized = value.Trim();
        if (normalized.Length > 128)
            throw new ArgumentException("Map names must be at most 128 characters.", paramName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Map names cannot contain control characters.", paramName);
        return normalized;
    }
}
