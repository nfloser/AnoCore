namespace AnoCore.Modules.Tournament;

public sealed record TournamentPlayerPresence(ulong SteamId, Guid SessionId, string Name, Guid? MatchId, bool IsReady);
public sealed record TournamentServerPresence(string ServerId, Guid InstanceId,
    IReadOnlyList<TournamentPlayerPresence> Players, DateTimeOffset ExpiresAtUtc);

public interface ITournamentPresenceRepository
{
    ValueTask PublishAsync(string serverId, Guid instanceId, IReadOnlyCollection<TournamentPlayerPresence> players,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TournamentServerPresence>> LoadOnlineAsync(CancellationToken cancellationToken = default);
}

public sealed class TournamentServerIdentityConflictException(string serverId)
    : InvalidOperationException($"Tournament server ID '{serverId}' is already owned by a live instance. Configure unique server IDs.");

public sealed class TournamentServerConfiguration
{
    public bool Enabled { get; set; }
    public string ServerId { get; set; } = "";
    public static IReadOnlyCollection<string> Validate(TournamentServerConfiguration? value)
        => value is null ? ["Tournament server configuration required."]
            : value.Enabled && !TournamentPresenceValidation.ValidServerId(value.ServerId)
                ? ["Enabled tournament servers require a unique 1-48 character lowercase ServerId."] : [];
}

public static class TournamentPresenceValidation
{
    public const int HeartbeatSeconds = 10;
    public const int ExpirySeconds = 30;
    public static bool ValidServerId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 48
        && id[0] is not ('-' or '_') && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    public static void RequireValid(string serverId, Guid instanceId, IReadOnlyCollection<TournamentPlayerPresence> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (!ValidServerId(serverId) || instanceId == Guid.Empty || players.Count > 64)
            throw new ArgumentException("Presence needs a valid server ID, instance and at most 64 players.");
        var ids = new HashSet<ulong>();
        var sessions = new HashSet<Guid>();
        foreach (var player in players)
            if (player is null || player.SteamId == 0 || !ids.Add(player.SteamId)
                || player.SessionId == Guid.Empty || !sessions.Add(player.SessionId)
                || string.IsNullOrWhiteSpace(player.Name) || player.Name.Length > 128 || player.Name.Any(char.IsControl)
                || player.MatchId == Guid.Empty || (player.IsReady && player.MatchId is null))
                throw new ArgumentException("Invalid or duplicate presence identity, name or ready state.", nameof(players));
    }
}
