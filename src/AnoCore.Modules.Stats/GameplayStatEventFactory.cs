using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed record GameplayMatchParticipant(PlayerId PlayerId, PlayerTeam Team, int Score);

public static class GameplayStatEventFactory
{
    public static GameplayStatEvent? WeaponKill(string serverInstance, string mapName, long mapEpoch,
        int tick, DateTimeOffset occurredAtUtc, PlayerId attacker, PlayerId victim, string weapon)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(victim);
        if (attacker == victim || LiveRankPolicy.WeaponFamily(weapon) is not { } kind) return null;
        return Player(serverInstance, mapName, mapEpoch, tick, occurredAtUtc, attacker, kind,
            victim.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static IReadOnlyList<GameplayStatEvent> TeamObjective(string serverInstance, string mapName, long mapEpoch,
        int tick, DateTimeOffset occurredAtUtc, IEnumerable<PlayerSnapshot> players, GameplayStatKind kind,
        PlayerTeam team, PlayerId? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (!LiveRankPolicy.IsTeamObjective(kind) || team is not PlayerTeam.Terrorist and not PlayerTeam.CounterTerrorist)
            throw new ArgumentException("Team objective facts require a supported objective and playing team.");
        var snapshot = players.Take(65).ToArray();
        if (snapshot.Length > 64 || snapshot.Any(player => player is null)
            || snapshot.Select(player => player.Id).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Team objective facts require at most 64 unique players.");
        return Array.AsReadOnly(snapshot.Where(player => player.IsConnected && player.Team == team && player.Id != excluded)
            .OrderBy(player => player.Id.SteamId64)
            .Select(player => Player(serverInstance, mapName, mapEpoch, tick, occurredAtUtc, player.Id, kind,
                FormattableString.Invariant($"team:{(int)team}|excluded:{excluded?.SteamId64 ?? 0}"))).ToArray());
    }

    public static GameplayStatEvent Player(
        string serverInstance,
        string mapName,
        long mapEpoch,
        int tick,
        DateTimeOffset occurredAtUtc,
        PlayerId playerId,
        GameplayStatKind kind,
        string signature = "1")
    {
        var eventId = CombatEventIdentity.CreateDetail(
            serverInstance,
            mapName,
            mapEpoch,
            tick,
            $"stat_{(byte)kind}",
            playerId,
            null,
            signature);
        return new GameplayStatEvent(
            eventId, playerId, occurredAtUtc, mapName, kind);
    }

    public static IReadOnlyList<GameplayStatEvent> Round(
        string serverInstance,
        string mapName,
        long mapEpoch,
        int tick,
        DateTimeOffset occurredAtUtc,
        IEnumerable<PlayerSnapshot> players,
        PlayerTeam winner)
    {
        ArgumentNullException.ThrowIfNull(players);
        var result = new List<GameplayStatEvent>();
        foreach (var player in players
                     .Where(x => x.IsConnected
                         && x.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
                     .OrderBy(x => x.Id.SteamId64))
        {
            result.Add(Player(serverInstance, mapName, mapEpoch, tick, occurredAtUtc,
                player.Id, GameplayStatKind.RoundPlayed));
            result.Add(Player(serverInstance, mapName, mapEpoch, tick, occurredAtUtc,
                player.Id,
                player.Team == PlayerTeam.Terrorist
                    ? GameplayStatKind.RoundTerrorist
                    : GameplayStatKind.RoundCounterTerrorist));

            if (winner is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
            {
                result.Add(Player(serverInstance, mapName, mapEpoch, tick, occurredAtUtc,
                    player.Id,
                    player.Team == winner
                        ? GameplayStatKind.RoundWon
                        : GameplayStatKind.RoundLost));
            }
        }

        return result;
    }

    public static IReadOnlyList<GameplayStatEvent> Match(
        string serverInstance,
        string mapName,
        long mapEpoch,
        int tick,
        DateTimeOffset occurredAtUtc,
        IEnumerable<GameplayMatchParticipant> participants,
        bool freeForAll,
        PlayerTeam winningTeam)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var eligible = participants
            .Where(x => x.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
            .OrderBy(x => x.PlayerId.SteamId64)
            .ToArray();
        if (eligible.Length == 0) return [];

        PlayerId? ffaWinner = null;
        if (freeForAll)
        {
            var topScore = eligible.Max(x => x.Score);
            ffaWinner = eligible.First(x => x.Score == topScore).PlayerId;
        }
        else if (winningTeam is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist))
        {
            return [];
        }

        var result = new List<GameplayStatEvent>(eligible.Length);
        foreach (var participant in eligible)
        {
            var won = freeForAll
                ? participant.PlayerId == ffaWinner
                : participant.Team == winningTeam;
            result.Add(Player(
                serverInstance, mapName, mapEpoch, tick, occurredAtUtc,
                participant.PlayerId,
                won ? GameplayStatKind.MatchWon : GameplayStatKind.MatchLost,
                freeForAll
                    ? $"ffa|{participant.Score}"
                    : $"team|{(int)winningTeam}"));
        }

        return result;
    }
}
