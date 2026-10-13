using System.Text;
using AnoCore.Management.Host;
using AnoCore.Modules.Tournament;
using Microsoft.AspNetCore.Http;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchZyEndpointTests
{
    [TestMethod]
    public async Task IngressRejectsMissingAuthenticationBeforeReadingOrApplyingResults()
    {
        var repository = new FakeRepository();
        var context = Context("{}");
        await new TournamentMatchZyEndpoint(repository).HandleAsync(context, 42);
        Assert.AreEqual(401, context.Response.StatusCode);
        Assert.AreEqual(0, repository.Applied);
    }
    [TestMethod]
    public async Task IngressAcceptsAuthenticatedResultAndRejectsOversizedChunkedBody()
    {
        var repository = new FakeRepository();
        var context = Context("{}");
        context.Request.Headers.Authorization = "Bearer " + new string('a', 64);
        await new TournamentMatchZyEndpoint(repository).HandleAsync(context, 42);
        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.AreEqual(1, repository.Applied);
        context = Context(new string(' ', TournamentMatchZy.MaximumPayloadBytes + 1));
        context.Request.Headers.Authorization = "Bearer " + new string('a', 64);
        await new TournamentMatchZyEndpoint(repository).HandleAsync(context, 42);
        Assert.AreEqual(413, context.Response.StatusCode);
        Assert.AreEqual(1, repository.Applied);
    }
    private static DefaultHttpContext Context(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST"; context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }
    private sealed class FakeRepository : ITournamentMatchAssignmentRepository
    {
        public int Applied;
        public ValueTask<bool> AuthenticateAsync(long id, string token, CancellationToken cancellationToken = default) => ValueTask.FromResult(token == new string('a', 64));
        public ValueTask<bool> ApplyEventAsync(long id, string payload, CancellationToken cancellationToken = default) { Applied++; return ValueTask.FromResult(false); }
        public ValueTask<TournamentMatchClaim?> ClaimNextAsync(string server, Guid instance, TournamentMatchZyConfiguration config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> MarkDispatchedAsync(long id, string server, Guid instance, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<TournamentMatchAssignment>> LoadOpenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask CancelAsync(long id, string server, Guid instance, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
