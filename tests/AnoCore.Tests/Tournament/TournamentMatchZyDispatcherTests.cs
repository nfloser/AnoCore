using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchZyDispatcherTests
{
    [TestMethod]
    public async Task DispatchPersistsMarkerBeforeNativeLoadAndNeverReloadsOpenClaim()
    {
        var steps = new List<string>();
        var repository = new Repository(steps);
        var transport = new Transport(steps);
        var dispatcher = new TournamentMatchZyDispatcher(repository, transport, new() { Enabled = true, ResultBaseUrl = "https://example.com" }, "server", Guid.NewGuid());
        await dispatcher.PollAsync();
        await dispatcher.PollAsync();
        CollectionAssert.AreEqual(new[] { "prepare", "marker", "load" }, steps);
        await dispatcher.CancelAsync(42);
        CollectionAssert.AreEqual(new[] { "prepare", "marker", "load", "cancel", "end" }, steps);
    }
    private sealed class Transport(List<string> steps) : ITournamentMatchZyTransport
    {
        public ValueTask PrepareAsync(long id, string json, CancellationToken token) { steps.Add("prepare"); return ValueTask.CompletedTask; }
        public ValueTask LoadPreparedAsync(CancellationToken token) { steps.Add("load"); return ValueTask.CompletedTask; }
        public ValueTask EndAsync(CancellationToken token) { steps.Add("end"); return ValueTask.CompletedTask; }
    }
    private sealed class Repository(List<string> steps) : ITournamentMatchAssignmentRepository
    {
        private bool _claimed;
        public ValueTask<TournamentMatchClaim?> ClaimNextAsync(string server, Guid instance, TournamentMatchZyConfiguration config, CancellationToken token = default)
        {
            if (_claimed) return ValueTask.FromResult<TournamentMatchClaim?>(null); _claimed = true;
            var definition = new TournamentEventDefinition
            {
                Id = "event",
                Name = "Event",
                Teams = Enumerable.Range(1, 3).Select(i => new TournamentEventTeamDefinition
                { Id = $"team-{i}", Name = $"Team {i}", Tag = $"T{i}", InitialSeed = i, CaptainSteamId = (ulong)(i * 10), Starters = Enumerable.Range(0, 5).Select(j => (ulong)(i * 10 + j)).ToList() }).ToList()
            };
            return ValueTask.FromResult<TournamentMatchClaim?>(new(new(42, "event", "fixture", false, "team-1", "team-2", server, instance, 1, ["de_mirage"], "pending"), definition, new string('a', 64)));
        }
        public ValueTask<bool> MarkDispatchedAsync(long id, string server, Guid instance, CancellationToken token = default) { steps.Add("marker"); return ValueTask.FromResult(true); }
        public ValueTask CancelAsync(long id, string server, Guid instance, CancellationToken token = default) { steps.Add("cancel"); return ValueTask.CompletedTask; }
        public ValueTask<IReadOnlyList<TournamentMatchAssignment>> LoadOpenAsync(CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<bool> AuthenticateAsync(long id, string secret, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<bool> ApplyEventAsync(long id, string payload, CancellationToken token = default) => throw new NotSupportedException();
    }
}
