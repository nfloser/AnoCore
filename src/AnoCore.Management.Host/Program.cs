using System.Threading.RateLimiting;
using AnoCore.Management.Host;
using Microsoft.AspNetCore.RateLimiting;
using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

var builder = WebApplication.CreateBuilder(args);

var sidecarOptions = new ManagementSidecarOptions();
builder.Configuration.GetSection("Management").Bind(sidecarOptions);
var configurationErrors = ManagementSidecarOptions.Validate(sidecarOptions);
if (configurationErrors.Count != 0)
{
    throw new InvalidOperationException(
        "Invalid management sidecar configuration: "
        + string.Join(" ", configurationErrors));
}

var matchZyEnabled = builder.Configuration.GetValue<bool>("TournamentMatchZy:Enabled");
var matchZyConnection = matchZyEnabled ? Environment.GetEnvironmentVariable("ANOCORE_MYSQL") : null;
if (matchZyEnabled && string.IsNullOrWhiteSpace(matchZyConnection))
    throw new InvalidOperationException("MatchZy ingress requires ANOCORE_MYSQL for the same shared tournament database.");
if (matchZyEnabled)
    await TournamentPersistenceBootstrap.EnsureReadyAsync(new MySqlDatabase(matchZyConnection!)).ConfigureAwait(false);
if (matchZyEnabled)
    builder.Services.AddSingleton(new TournamentMatchZyEndpoint(new MySqlTournamentAssignmentRepository(new MySqlDatabase(matchZyConnection!))));

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = matchZyEnabled ? TournamentMatchZy.MaximumPayloadBytes : sidecarOptions.MaximumRequestBodyBytes;
    options.Limits.MaxRequestHeaderCount = 16;
    options.Limits.MaxRequestHeadersTotalSize = 8 * 1024;
    options.Limits.MaxRequestLineSize = 512;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton(sidecarOptions);
builder.Services.AddSingleton<IManagementPipeTransport, NamedPipeManagementTransport>();
builder.Services.AddSingleton<ManagementProxyEndpoint>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                client,
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = sidecarOptions.RequestsPerMinutePerClient,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    Window = TimeSpan.FromMinutes(1),
                });
        });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next().ConfigureAwait(false);
});
app.UseRateLimiter();

app.Run(async context =>
{
    if (matchZyEnabled && context.Request.Path.Value is { } path && path.StartsWith("/tournament/matchzy/", StringComparison.Ordinal)
        && long.TryParse(path["/tournament/matchzy/".Length..], out var assignmentId) && assignmentId > 0)
    {
        await context.RequestServices.GetRequiredService<TournamentMatchZyEndpoint>().HandleAsync(context, assignmentId).ConfigureAwait(false);
        return;
    }
    var endpoint = context.RequestServices.GetRequiredService<ManagementProxyEndpoint>();
    await endpoint.HandleAsync(context).ConfigureAwait(false);
});

app.Run();
