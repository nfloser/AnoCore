using System.Data.Common;
using System.Globalization;

namespace AnoCore.Modules.Progression.Persistence;

public sealed partial class MySqlAchievementUnlockRepository
{
    private static AchievementTierUnlockCandidate NormalizeAndValidate(
        AchievementTierUnlockCandidate candidate)
    {
        ValidateAchievementId(candidate.AchievementId);
        if (candidate.Tier is < 1 or > AchievementDefinition.MaxTiers
            || candidate.DefinitionVersion < 1
            || candidate.RewardXp < 0
            || candidate.AwardedXp < 0
            || !string.Equals(
                candidate.GrantId,
                AchievementUnlockService.GrantId(
                    candidate.AchievementId, candidate.Tier),
                StringComparison.Ordinal)
            || candidate.BoostMultiplier is < 1m
                or > ProgressionDefinitionSnapshot.MaxBoostMultiplier
            || candidate.BoostId is not null
                && !PrintableBounded(candidate.BoostId, 64)
            || candidate.BoostId is null && candidate.BoostMultiplier != 1m)
        {
            throw new ArgumentException(
                "Achievement unlock candidate is invalid.", nameof(candidate));
        }

        var expectedAward = checked((long)decimal.Truncate(
            candidate.RewardXp * candidate.BoostMultiplier));
        if (expectedAward != candidate.AwardedXp)
            throw new ArgumentException(
                "Achievement reward XP does not match reward and multiplier.",
                nameof(candidate));

        return candidate with
        {
            OccurredAtUtc = NormalizeUtc(candidate.OccurredAtUtc),
        };
    }

    private static void ValidateStoredPair(
        AchievementUnlockRecord unlock,
        ProgressionGrantRecord grant)
    {
        if (grant.Source != ProgressionXpSource.AchievementReward
            || !string.Equals(grant.GrantId, unlock.GrantId, StringComparison.Ordinal)
            || grant.BaseXp != unlock.RewardXp)
        {
            throw new InvalidOperationException(
                "Stored achievement unlock and reward grant disagree.");
        }
    }

    private static void ValidateAchievementId(string? achievementId)
    {
        if (string.IsNullOrWhiteSpace(achievementId)
            || achievementId.Length > 64
            || achievementId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Achievement ID is invalid.", nameof(achievementId));
        }
    }

    private static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static long ReadNonnegativeInt64(object value, string field)
    {
        try
        {
            var converted = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (converted < 0)
                throw new InvalidOperationException(
                    $"Stored progression field '{field}' cannot be negative.");
            return converted;
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                $"Stored progression field '{field}' exceeds supported XP range.",
                exception);
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
