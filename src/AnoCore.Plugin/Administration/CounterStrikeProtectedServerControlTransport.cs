using System.Net;
using System.Security.Cryptography;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace AnoCore.Plugin.Administration;

public sealed class CounterStrikeProtectedServerControlTransport
    : IProtectedServerControlTransport
{
    private readonly IPlayerRegistry _players;
    private readonly byte[] _fingerprintKey;

    public CounterStrikeProtectedServerControlTransport(IPlayerRegistry players)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _fingerprintKey = RandomNumberGenerator.GetBytes(32);
    }

    public ValueTask<bool> SetCVarAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            () =>
            {
                var conVar = ConVar.Find(name);
                if (conVar is null)
                {
                    return false;
                }

                conVar.StringValue = value;
                return true;
            },
            cancellationToken);

    public async ValueTask ExecuteServerCommandAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        _ = await RunOnServerThreadAsync(
            () =>
            {
                Server.ExecuteCommand(
                    arguments.Length == 0
                        ? command
                        : $"{command} {arguments}");
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<SameIpPlayerGroup>> GetSameIpGroupsAsync(
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            BuildSameIpGroups,
            cancellationToken);

    private IReadOnlyList<SameIpPlayerGroup> BuildSameIpGroups()
    {
        Dictionary<string, NetworkGroupBuilder> groups =
            new(StringComparer.Ordinal);

        foreach (var controller in Utilities.GetPlayers())
        {
            if (controller is not { IsValid: true, IsBot: false, IsHLTV: false }
                || controller.SteamID == 0
                || !TryNormalizeAddress(controller.IpAddress, out var address))
            {
                continue;
            }

            var playerId = new PlayerId(controller.SteamID);
            if (!_players.TryGet(playerId, out var current)
                || current is null
                || !current.IsConnected)
            {
                continue;
            }

            var addressKey = Convert.ToHexString(address.GetAddressBytes());
            if (!groups.TryGetValue(addressKey, out var group))
            {
                group = new NetworkGroupBuilder(
                    CreateFingerprint(address));
                groups.Add(addressKey, group);
            }

            group.Players.Add(
                new SameIpPlayer(current.Id, current.Name));
        }

        return groups.Values
            .Where(group => group.Players.Count > 1)
            .OrderBy(group => group.Fingerprint, StringComparer.Ordinal)
            .Select(group => new SameIpPlayerGroup(
                group.Fingerprint,
                group.Players
                    .OrderBy(player => player.Id.SteamId64)
                    .ToArray()))
            .ToArray();
    }

    private string CreateFingerprint(IPAddress address)
    {
        using var hmac = new HMACSHA256(_fingerprintKey);
        var digest = hmac.ComputeHash(address.GetAddressBytes());
        return $"network-{Convert.ToHexString(digest.AsSpan(0, 6)).ToLowerInvariant()}";
    }

    private static bool TryNormalizeAddress(
        string? rawAddress,
        out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            return false;
        }

        var value = rawAddress.Trim();
        if (IPAddress.TryParse(value.Trim('[', ']'), out var direct))
        {
            address = NormalizeAddress(direct);
            return true;
        }

        if (IPEndPoint.TryParse(value, out var endpoint))
        {
            address = NormalizeAddress(endpoint.Address);
            return true;
        }

        return false;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
        => address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;

    private static ValueTask<T> RunOnServerThreadAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Server.NextWorldUpdate(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return new ValueTask<T>(completion.Task);
    }

    private sealed class NetworkGroupBuilder(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;

        public List<SameIpPlayer> Players { get; } = [];
    }
}
