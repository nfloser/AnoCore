using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public static class ExtendedAdministrationDisconnect
{
    public static async ValueTask DisconnectAsync(
        IPlayerRegistry registry,
        ExtendedPlayerStateService? state,
        ExtendedPositionService? positions,
        PlayerSnapshot current,
        DateTimeOffset disconnectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(current);

        try
        {
            // Registry invalidation happens before asynchronous event delivery or admin cleanup.
            await registry.DisconnectAsync(
                current.Id, current.SessionId, disconnectedAtUtc).ConfigureAwait(false);
        }
        finally
        {
            // Release only this session's bookkeeping; the pawn may already be gone.
            if (positions is not null)
            {
                await positions.ForgetSessionAsync(current.SessionId).ConfigureAwait(false);
            }

            if (state is not null)
            {
                await state.ForgetSessionAsync(current.SessionId).ConfigureAwait(false);
            }
        }
    }
}
