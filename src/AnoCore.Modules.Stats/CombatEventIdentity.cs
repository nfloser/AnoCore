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
        var input = FormattableString.Invariant(
            $"{serverInstance}|{mapName}|{mapEpoch}|{tick}|{victimId.SteamId64}");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(digest.AsSpan(0, 16));
    }
}
