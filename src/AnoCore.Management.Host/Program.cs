using System.Threading.RateLimiting;
using AnoCore.Management.Host;
using Microsoft.AspNetCore.RateLimiting;

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

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize =
        ManagementSidecarOptions.HardMaximumRequestBodyBytes;
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
    var endpoint = context.RequestServices.GetRequiredService<ManagementProxyEndpoint>();
    await endpoint.HandleAsync(context).ConfigureAwait(false);
});

app.Run();
