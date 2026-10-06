using System.Collections.ObjectModel;
using System.Globalization;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed class GameplayXpConfiguration
{
    public bool Enabled { get; set; } = true;
    public DateTimeOffset EarnFromUtc { get; set; } = InitialStart();
    public int CheckpointSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
    public int KillXp { get; set; } = 10;
    public int AssistXp { get; set; } = 5;
    public decimal WeekendMultiplier { get; set; } = 1m;
    public Dictionary<GameplayStatKind, int> GameplayXp { get; set; } = new()
    {
        [GameplayStatKind.HeadshotKill] = 5,
        [GameplayStatKind.BombPlanted] = 20,
        [GameplayStatKind.BombDefused] = 30,
        [GameplayStatKind.HostageRescued] = 30,
        [GameplayStatKind.Mvp] = 10,
        [GameplayStatKind.RoundWon] = 10,
    };

    public GameplayXpPolicy Snapshot() => GameplayXpPolicy.Create(this);

    public static IReadOnlyCollection<string> Validate(GameplayXpConfiguration configuration)
    {
        try { ArgumentNullException.ThrowIfNull(configuration); _ = configuration.Snapshot(); return []; }
        catch (ArgumentException exception) { return [exception.Message]; }
    }

    private static DateTimeOffset InitialStart()
    {
        var utc = DateTimeOffset.UtcNow;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
}

public sealed class GameplayXpPolicy
{
    private GameplayXpPolicy(GameplayXpConfiguration configuration, IReadOnlyDictionary<GameplayStatKind, int> weights)
    {
        EarnFromUtc = configuration.EarnFromUtc;
        CheckpointSeconds = configuration.CheckpointSeconds;
        BatchSize = configuration.BatchSize;
        KillXp = configuration.KillXp;
        AssistXp = configuration.AssistXp;
        WeekendMultiplier = configuration.WeekendMultiplier;
        GameplayXp = weights;
    }

    public DateTimeOffset EarnFromUtc { get; }
    public int CheckpointSeconds { get; }
    public int BatchSize { get; }
    public int KillXp { get; }
    public int AssistXp { get; }
    public decimal WeekendMultiplier { get; }
    public IReadOnlyDictionary<GameplayStatKind, int> GameplayXp { get; }

    public XpBoostResolution ResolveGameplayBoost(ProgressionDefinitionSnapshot definitions, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var scheduled = definitions.ResolveBoost(at, ProgressionXpSource.Gameplay);
        var day = new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero);
        if (WeekendMultiplier == 1m || day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            return scheduled;
        var start = day.DayOfWeek == DayOfWeek.Sunday ? day.AddDays(-1) : day;
        var id = "gameplay.weekend." + start.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return WeekendMultiplier > scheduled.Multiplier
            || WeekendMultiplier == scheduled.Multiplier && string.CompareOrdinal(id, scheduled.BoostId) < 0
            ? new XpBoostResolution(id, WeekendMultiplier)
            : scheduled;
    }

    internal static GameplayXpPolicy Create(GameplayXpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.EarnFromUtc.Offset != TimeSpan.Zero
            || configuration.EarnFromUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0
            || configuration.CheckpointSeconds is < 10 or > 600 || configuration.BatchSize is < 1 or > 100
            || configuration.KillXp is < 0 or > 1000 || configuration.AssistXp is < 0 or > 1000
            || configuration.WeekendMultiplier is < 1m or > ProgressionDefinitionSnapshot.MaxBoostMultiplier
            || configuration.GameplayXp is null)
            throw new ArgumentException("Gameplay XP requires a microsecond UTC start, bounded checkpoint/batch, 0-1000 XP weights and a 1-10 weekend multiplier.");
        var weights = new Dictionary<GameplayStatKind, int>();
        foreach (var (kind, value) in configuration.GameplayXp)
        {
            if (!Enum.IsDefined(kind) || value is < 0 or > 1000)
                throw new ArgumentException("Gameplay XP weights require known statistics and 0-1000 XP.");
            if (value > 0) weights.Add(kind, value);
        }
        return new GameplayXpPolicy(configuration, new ReadOnlyDictionary<GameplayStatKind, int>(weights));
    }
}

public interface IGameplayXpRepository
{
    ValueTask<IReadOnlyList<ProgressionGrantRecord>> ReconcileAsync(PlayerId playerId, GameplayXpPolicy policy,
        ProgressionDefinitionSnapshot definitions, DateTimeOffset at, CancellationToken cancellationToken = default);
}
