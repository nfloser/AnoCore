using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace AnoCore.Plugin.Administration;

public sealed class CounterStrikeExtendedPlayerStateTransport : IExtendedPlayerStateTransport
{
    private readonly IPlayerRegistry _players;

    public CounterStrikeExtendedPlayerStateTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask<ExtendedPlayerStateBaseline> CaptureAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateFacet facet,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (_, pawn) => facet switch
            {
                ExtendedPlayerStateFacet.Movement => new ExtendedPlayerStateBaseline(
                    facet,
                    MoveType: (byte)pawn.MoveType,
                    ActualMoveType: (byte)pawn.ActualMoveType),
                ExtendedPlayerStateFacet.Speed => new ExtendedPlayerStateBaseline(
                    facet,
                    Primary: pawn.VelocityModifier),
                ExtendedPlayerStateFacet.Blindness => new ExtendedPlayerStateBaseline(
                    facet,
                    Primary: pawn.FlashDuration,
                    Secondary: pawn.FlashMaxAlpha,
                    Tertiary: pawn.BlindStartTime,
                    Quaternary: pawn.BlindUntilTime),
                ExtendedPlayerStateFacet.Damage => new ExtendedPlayerStateBaseline(
                    facet,
                    Flag: pawn.TakesDamage),
                _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, null),
            },
            cancellationToken);

    public ValueTask ApplyAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateMutation mutation,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, pawn) =>
            {
                switch (mutation.Operation)
                {
                    case ExtendedPlayerStateOperation.SetHealth:
                        pawn.Health = mutation.Value!.Value;
                        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                        break;

                    case ExtendedPlayerStateOperation.SetArmor:
                        pawn.ArmorValue = mutation.Value!.Value;
                        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                        break;

                    case ExtendedPlayerStateOperation.Freeze:
                        SetMoveType(pawn, MoveType_t.MOVETYPE_NONE);
                        break;

                    case ExtendedPlayerStateOperation.Noclip:
                        SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
                        break;

                    case ExtendedPlayerStateOperation.Slay:
                        controller.CommitSuicide(explode: false, force: true);
                        break;

                    case ExtendedPlayerStateOperation.SetSpeed:
                        pawn.VelocityModifier = mutation.Value!.Value / 100f;
                        break;

                    case ExtendedPlayerStateOperation.Blind:
                        ApplyBlind(pawn, mutation.Value!.Value);
                        break;

                    case ExtendedPlayerStateOperation.God:
                        pawn.TakesDamage = false;
                        break;

                    case ExtendedPlayerStateOperation.Unfreeze:
                    case ExtendedPlayerStateOperation.Walk:
                    case ExtendedPlayerStateOperation.ResetSpeed:
                    case ExtendedPlayerStateOperation.Unblind:
                    case ExtendedPlayerStateOperation.Ungod:
                        throw new InvalidOperationException(
                            "Restore operations must use the captured AnoCore baseline.");

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(mutation), mutation.Operation, null);
                }

                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask RestoreAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateBaseline baseline,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (_, pawn) =>
            {
                switch (baseline.Facet)
                {
                    case ExtendedPlayerStateFacet.Movement:
                        pawn.MoveType = (MoveType_t)baseline.MoveType;
                        pawn.ActualMoveType = (MoveType_t)baseline.ActualMoveType;
                        break;

                    case ExtendedPlayerStateFacet.Speed:
                        pawn.VelocityModifier = baseline.Primary;
                        break;

                    case ExtendedPlayerStateFacet.Blindness:
                        pawn.FlashDuration = baseline.Primary;
                        pawn.FlashMaxAlpha = baseline.Secondary;
                        pawn.BlindStartTime = baseline.Tertiary;
                        pawn.BlindUntilTime = baseline.Quaternary;
                        Utilities.SetStateChanged(
                            pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
                        Utilities.SetStateChanged(
                            pawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
                        break;

                    case ExtendedPlayerStateFacet.Damage:
                        pawn.TakesDamage = baseline.Flag;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(baseline), baseline.Facet, null);
                }

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

    private static void SetMoveType(CCSPlayerPawn pawn, MoveType_t moveType)
    {
        pawn.MoveType = moveType;
        pawn.ActualMoveType = moveType;
    }

    private static void ApplyBlind(CCSPlayerPawn pawn, int alpha)
    {
        var now = Server.CurrentTime;
        pawn.BlindStartTime = now;
        pawn.BlindUntilTime = now + 3600f;
        pawn.FlashDuration = 3600f;
        pawn.FlashMaxAlpha = alpha;

        Utilities.SetStateChanged(
            pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
        Utilities.SetStateChanged(
            pawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
    }
}

internal static class ExtendedPlayerStateValueTaskExtensions
{
    public static async ValueTask AsVoid<T>(this ValueTask<T> task)
        => _ = await task.ConfigureAwait(false);
}
