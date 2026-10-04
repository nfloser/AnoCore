using System.Security.Cryptography;
using System.Text;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Stats;

public static class CombatEventIdentity
{
    public static Guid Create(string serverInstance, string mapName, long mapEpoch,
        int tick, PlayerId victimId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(victimId);
        return Hash(FormattableString.Invariant(
            $"{serverInstance}|{mapName}|{mapEpoch}|{tick}|death|{victimId.SteamId64}"));
    }

    public static Guid CreateDetail(string serverInstance, string mapName, long mapEpoch,
        int tick, string eventType, PlayerId primaryId, PlayerId? secondaryId,
        string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(primaryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        if (eventType.Length > 32)
            throw new ArgumentException("Combat event type is too long.", nameof(eventType));
        if (signature.Length > 256)
            throw new ArgumentException("Combat event signature is too long.", nameof(signature));

        return Hash(FormattableString.Invariant(
            $"{serverInstance}|{mapName}|{mapEpoch}|{tick}|{eventType}|{primaryId.SteamId64}|"
            + $"{secondaryId?.SteamId64 ?? 0UL}|{signature}"));
    }

    private static Guid Hash(string input)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(digest.AsSpan(0, 16));
    }
}
