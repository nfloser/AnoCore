namespace AnoCore.Modules.Tournament;

public interface ITournamentMatchZyTransport
{
    ValueTask PrepareAsync(long id, string json, CancellationToken cancellationToken);
    ValueTask LoadPreparedAsync(CancellationToken cancellationToken);
    ValueTask EndAsync(CancellationToken cancellationToken);
}

/// <summary>Claim → durable file → dispatch marker → one native load. Restarts never blindly reload a live match.</summary>
public sealed class TournamentMatchZyDispatcher(ITournamentMatchAssignmentRepository repository,
    ITournamentMatchZyTransport transport, TournamentMatchZyConfiguration configuration, string serverId, Guid instanceId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async ValueTask CancelAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.CancelAsync(assignmentId, serverId, instanceId, cancellationToken).ConfigureAwait(false);
            await transport.EndAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async ValueTask PollAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var claim = await repository.ClaimNextAsync(serverId, instanceId, configuration, cancellationToken).ConfigureAwait(false);
            if (claim is null) return;
            var url = configuration.ResultBaseUrl.TrimEnd('/') + "/tournament/matchzy/" + claim.Assignment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var json = TournamentMatchZy.CreateConfiguration(claim.Definition, claim.Assignment, url, claim.Token);
            await transport.PrepareAsync(claim.Assignment.Id, json, cancellationToken).ConfigureAwait(false);
            if (!await repository.MarkDispatchedAsync(claim.Assignment.Id, serverId, instanceId, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Assignment lost ownership before dispatch.");
            await transport.LoadPreparedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
