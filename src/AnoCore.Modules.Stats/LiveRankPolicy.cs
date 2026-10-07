using System.Collections.Frozen;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class LiveRankConfiguration
{
    public bool WarmupPoints { get; set; }
    public int MinimumPlayers { get; set; } = 4;
    public bool IncludeBots { get; set; }
    public bool FreeForAll { get; set; }
    public int TeamKillPenalty { get; set; } = 2;
    public int SuicidePenalty { get; set; } = 1;
    public Dictionary<string, int> WeaponPoints { get; set; } = [];
    public decimal DistanceThresholdMeters { get; set; }
    public int DistanceBonus { get; set; }
    public bool DynamicMultipliers { get; set; }
    public decimal MinimumDynamicMultiplier { get; set; } = 0.25m;
    public decimal MaximumDynamicMultiplier { get; set; } = 4m;
    public decimal VipMultiplier { get; set; } = 1m;
    public string VipPermission { get; set; } = "ano.ranks.vip";
    public int PlaytimeIntervalSeconds { get; set; }
    public int StreakWindowSeconds { get; set; } = 30;
    public Dictionary<int, int> StreakPoints { get; set; } = [];

    public static IReadOnlyCollection<string> Validate(LiveRankConfiguration? value)
    {
        if (value is null) return ["Live rank policy is required."];
        var errors = new List<string>();
        if (value.PlaytimeIntervalSeconds is < 0 or > 86400 || value.PlaytimeIntervalSeconds is > 0 and < 10
            || value.MinimumPlayers is < 1 or > 64 || value.TeamKillPenalty is < 0 or > 1000
            || value.SuicidePenalty is < 0 or > 1000 || value.DistanceBonus is < 0 or > 1000
            || value.DistanceThresholdMeters is < 0 or > 10000 || value.StreakWindowSeconds is < 1 or > 600
            || value.VipMultiplier is < 1 or > 10 || value.MinimumDynamicMultiplier is <= 0 or > 4
            || value.MaximumDynamicMultiplier < value.MinimumDynamicMultiplier || value.MaximumDynamicMultiplier > 4)
            errors.Add("Live rank numeric policy is outside its supported bounds.");
        if (value.WeaponPoints is null || value.WeaponPoints.Count > 128 || value.WeaponPoints.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64
                || pair.Key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')
                || pair.Value is < -1000 or > 1000)
            || value.WeaponPoints.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.WeaponPoints.Count)
            errors.Add("Weapon rank points require at most 128 unique engine weapon keys and bounded values.");
        if (value.StreakPoints is null || value.StreakPoints.Any(pair => pair.Key is < 2 or > 64 || pair.Value is < 1 or > 1000))
            errors.Add("Killstreak thresholds require counts 2-64 and bonuses 1-1000.");
        try { _ = new PermissionId(value.VipPermission); }
        catch (ArgumentException) { errors.Add("The VIP permission must be a valid ano.* permission."); }
        return errors;
    }
}

public sealed record RankLiveContext(Guid EventId, DateTimeOffset OccurredAtUtc,
    bool IsWarmup, int HumanPlayers, string RoundKey);

public sealed record RankParticipant(PlayerSnapshot? Player, PlayerTeam Team, bool IsBot);

public sealed record RankDeathInput(RankLiveContext Context, RankParticipant Victim,
    RankParticipant? Attacker, PlayerSnapshot? Assister, string Weapon,
    IReadOnlyList<GameplayStatKind> Specials, bool FlashAssist, decimal DistanceMeters);

public sealed class LiveRankPolicy
{
    private readonly RankScoreWeights _weights;
    private readonly FrozenDictionary<string, int> _weapons;
    private readonly int _teamKillPenalty;
    private readonly int _suicidePenalty;
    private readonly decimal _distanceThreshold;
    private readonly int _distanceBonus;
    private readonly bool _dynamic;
    private readonly decimal _minimumDynamic;
    private readonly decimal _maximumDynamic;
    private readonly decimal _vipMultiplier;

    public LiveRankPolicy(RankConfiguration configuration)
    {
        var errors = RankConfiguration.Validate(configuration);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        _weights = configuration.ScoreWeights;
        var live = configuration.LivePolicy;
        WarmupPoints = live.WarmupPoints;
        MinimumPlayers = live.MinimumPlayers;
        IncludeBots = live.IncludeBots;
        FreeForAll = live.FreeForAll;
        PlaytimeInterval = TimeSpan.FromSeconds(live.PlaytimeIntervalSeconds);
        StreakWindow = TimeSpan.FromSeconds(live.StreakWindowSeconds);
        StreakPoints = live.StreakPoints.ToFrozenDictionary();
        VipPermission = new PermissionId(live.VipPermission);
        _vipMultiplier = live.VipMultiplier;
        _weapons = live.WeaponPoints.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _teamKillPenalty = live.TeamKillPenalty;
        _suicidePenalty = live.SuicidePenalty;
        _distanceThreshold = live.DistanceThresholdMeters;
        _distanceBonus = live.DistanceBonus;
        _dynamic = live.DynamicMultipliers;
        _minimumDynamic = live.MinimumDynamicMultiplier;
        _maximumDynamic = live.MaximumDynamicMultiplier;
    }

    public bool WarmupPoints { get; }
    public int MinimumPlayers { get; }
    public bool IncludeBots { get; }
    public bool FreeForAll { get; }
    public TimeSpan PlaytimeInterval { get; }
    public TimeSpan StreakWindow { get; }
    public IReadOnlyDictionary<int, int> StreakPoints { get; }
    public PermissionId VipPermission { get; }
    public bool UsesVip => _vipMultiplier != 1m;

    public bool Allowed(RankLiveContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.EventId == Guid.Empty || context.HumanPlayers is < 0 or > 64
            || string.IsNullOrWhiteSpace(context.RoundKey) || context.RoundKey.Length > 192
            || context.RoundKey.Any(char.IsControl))
            throw new ArgumentException("Rank live context is invalid.", nameof(context));
        return (!context.IsWarmup || WarmupPoints) && context.HumanPlayers >= MinimumPlayers;
    }

    public bool ValidKill(RankDeathInput input)
        => input.Attacker is not null && input.Attacker.Player?.Id != input.Victim.Player?.Id
            && (!input.Victim.IsBot && !input.Attacker.IsBot || IncludeBots)
            && (FreeForAll || input.Attacker.Team != input.Victim.Team);

    public IReadOnlyList<RankPointAward> Death(RankDeathInput input, long victimScore, long attackerScore,
        IEnumerable<PlayerId> vipPlayers, int streakCount)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Victim);
        ArgumentNullException.ThrowIfNull(vipPlayers);
        if (victimScore < 0 || attackerScore < 0 || streakCount is < 1 or > 65
            || input.DistanceMeters is < 0 or > 10000 || string.IsNullOrWhiteSpace(input.Weapon)
            || input.Weapon.Length > 64 || input.Weapon.Any(char.IsControl)
            || input.Specials is null || input.Specials.Count > 8
            || input.Specials.Distinct().Count() != input.Specials.Count
            || input.Specials.Any(kind => !IsKillSpecial(kind)))
            throw new ArgumentException("Rank death inputs are invalid.", nameof(input));
        ValidateParticipant(input.Victim);
        if (input.Attacker is not null) ValidateParticipant(input.Attacker);
        if (!Allowed(input.Context) || !Playing(input.Victim.Team)
            || input.Attacker is not null && !Playing(input.Attacker.Team)
            || !IncludeBots && (input.Victim.IsBot || input.Attacker?.IsBot == true)) return [];
        var vips = vipPlayers.Take(65).ToHashSet();
        if (vips.Count > 64) throw new ArgumentException("VIP player set is unbounded.", nameof(vipPlayers));
        var awards = new Dictionary<PlayerId, long>();
        var victim = input.Victim.Player?.Id;
        var attacker = input.Attacker?.Player?.Id;
        var suicide = input.Attacker is null || attacker is not null && attacker == victim;
        if (suicide)
        {
            if (victim is not null) awards[victim] = -_suicidePenalty;
            return Results();
        }
        if (victim is not null)
            awards[victim] = Scale(-_weights.DeathPenalty, Dynamic(attackerScore, victimScore), false);
        var teamkill = !FreeForAll && input.Attacker!.Team == input.Victim.Team;
        if (attacker is not null)
        {
            if (teamkill) awards[attacker] = -_teamKillPenalty;
            else
            {
                long points = _weights.KillPoints + _weapons.GetValueOrDefault(input.Weapon);
                foreach (var kind in input.Specials) points = checked(points + _weights.GameplayPoints.GetValueOrDefault(kind));
                if (_distanceThreshold > 0 && input.DistanceMeters >= _distanceThreshold) points += _distanceBonus;
                points += StreakPoints.GetValueOrDefault(streakCount);
                awards[attacker] = Scale(points, Dynamic(victimScore, attackerScore), vips.Contains(attacker));
            }
        }
        if (!teamkill && input.Assister is { } assister && assister.Id != victim && assister.Id != attacker
            && Playing(assister.Team) && (FreeForAll || assister.Team == input.Attacker!.Team))
        {
            var points = _weights.AssistPoints + (input.FlashAssist ? _weights.GameplayPoints.GetValueOrDefault(GameplayStatKind.FlashAssist) : 0);
            awards[assister.Id] = Scale(points, 1m, vips.Contains(assister.Id));
        }
        return Results();

        IReadOnlyList<RankPointAward> Results() => awards.OrderBy(pair => pair.Key.SteamId64)
            .Select(pair => new RankPointAward(pair.Key, pair.Value)).ToList().AsReadOnly();
    }

    public long Gameplay(GameplayStatKind kind, bool vip)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (IsKillSpecial(kind) || kind == GameplayStatKind.FlashAssist
            || FreeForAll && kind is GameplayStatKind.RoundWon or GameplayStatKind.RoundLost) return 0;
        return Scale(_weights.GameplayPoints.GetValueOrDefault(kind), 1m, vip);
    }

    public static bool IsKillSpecial(GameplayStatKind kind)
        => kind is GameplayStatKind.FirstBlood or GameplayStatKind.HeadshotKill or GameplayStatKind.NoScopeKill
            or GameplayStatKind.PenetratedKill or GameplayStatKind.ThroughSmokeKill or GameplayStatKind.FlashedKill
            or GameplayStatKind.DominatedKill or GameplayStatKind.RevengeKill;

    private decimal Dynamic(long numerator, long denominator) => !_dynamic ? 1m
        : Math.Clamp((decimal)Math.Max(1, numerator) / Math.Max(1, denominator), _minimumDynamic, _maximumDynamic);

    private long Scale(long points, decimal dynamic, bool vip)
        => checked((long)decimal.Truncate(points * dynamic * (points > 0 && vip ? _vipMultiplier : 1m)));

    private static bool Playing(PlayerTeam team) => team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;

    private static void ValidateParticipant(RankParticipant participant)
    {
        if (!Enum.IsDefined(participant.Team) || participant.IsBot == (participant.Player is not null))
            throw new ArgumentException("Rank participants require a human snapshot or an anonymous bot.", nameof(participant));
    }
}
