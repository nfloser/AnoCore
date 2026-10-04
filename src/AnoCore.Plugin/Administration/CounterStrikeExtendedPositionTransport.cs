using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace AnoCore.Plugin.Administration;

public sealed class CounterStrikeExtendedPositionTransport : IExtendedPositionTransport
{
    private readonly IPlayerRegistry _players;

    public CounterStrikeExtendedPositionTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask<PlayerWorldPosition> ReadPositionAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (_, pawn) =>
            {
                var origin = pawn.AbsOrigin
                    ?? throw new InvalidOperationException(
                        "The target pawn does not expose a world position.");
                return new PlayerWorldPosition(origin.X, origin.Y, origin.Z);
            },
            cancellationToken);

    public ValueTask RespawnAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, _) =>
            {
                controller.Respawn();
                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask TeleportAsync(
        PlayerSnapshot player,
        PlayerWorldPosition position,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (_, pawn) =>
            {
                pawn.Teleport(new Vector(position.X, position.Y, position.Z));
                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask SlapAsync(
        PlayerSnapshot player,
        int damage,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, pawn) =>
            {
                if (damage > 0 && pawn.Health - damage <= 0)
                {
                    pawn.CommitSuicide(explode: false, force: false);
                    return true;
                }

                if (damage > 0)
                {
                    pawn.Health -= damage;
                    Utilities.SetStateChanged(
                        pawn,
                        "CBaseEntity",
                        "m_iHealth");
                }

                try
                {
                    controller.ExecuteClientCommand(
                        $"play /sounds/player/damage{Random.Shared.Next(1, 4)}");
                }
                catch
                {
                    // Sound feedback is cosmetic; the authoritative slap still proceeds.
                }

                var velocity = pawn.AbsVelocity;
                var slapVelocity = new Vector(
                    velocity.X + RandomVelocityComponent(),
                    velocity.Y + RandomVelocityComponent(),
                    velocity.Z + Random.Shared.Next(100, 300));

                pawn.Teleport(position: null, angles: null, velocity: slapVelocity);
                return true;
            },
            cancellationToken).AsVoid();

    private ValueTask<T> RunOnServerThreadAsync<T>(
        PlayerSnapshot player,
        Func<CCSPlayerController, CCSPlayerPawn, T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Server.NextWorldUpdate(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (controller, pawn) = ResolveCurrentPlayer(player);
                completion.TrySetResult(action(controller, pawn));
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

    private (CCSPlayerController Controller, CCSPlayerPawn Pawn) ResolveCurrentPlayer(
        PlayerSnapshot expected)
    {
        if (!_players.TryGet(expected.Id, out var current)
            || current is null
            || !current.IsConnected
            || current.SessionId != expected.SessionId)
        {
            throw new InvalidOperationException(
                "The target player session changed before the engine action ran.");
        }

        var controller = Utilities.GetPlayers().FirstOrDefault(value =>
            value is { IsValid: true, IsBot: false, IsHLTV: false }
            && value.SteamID == expected.Id.SteamId64);

        var pawn = controller?.PlayerPawn.Value;
        if (controller is null
            || pawn is null
            || !pawn.IsValid)
        {
            throw new InvalidOperationException(
                "The target player no longer has a valid Counter-Strike pawn.");
        }

        return (controller, pawn);
    }

    private static float RandomVelocityComponent()
        => (Random.Shared.Next(50, 230))
           * (Random.Shared.Next(2) == 0 ? -1 : 1);
}
