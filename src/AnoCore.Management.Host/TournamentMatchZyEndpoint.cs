using System.Text;
using System.Text.Json;
using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Management.Host;

/// <summary>Optional ingress is independent from operator management authentication.</summary>
public sealed class TournamentMatchZyEndpoint(ITournamentMatchAssignmentRepository repository)
{
    public async Task HandleAsync(HttpContext context, long id)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = 405; return; }
        if (!context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
        { context.Response.StatusCode = 415; return; }
        var auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || !await repository.AuthenticateAsync(id, auth[7..], context.RequestAborted).ConfigureAwait(false))
        { context.Response.StatusCode = 401; return; }
        if (context.Request.ContentLength > TournamentMatchZy.MaximumPayloadBytes) { context.Response.StatusCode = 413; return; }
        try
        {
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(bytes, context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > TournamentMatchZy.MaximumPayloadBytes) { context.Response.StatusCode = 413; return; }
                buffer.Write(bytes, 0, count);
            }
            var payload = new UTF8Encoding(false, true).GetString(buffer.ToArray());
            var complete = await repository.ApplyEventAsync(id, payload, context.RequestAborted).ConfigureAwait(false);
            await context.Response.WriteAsJsonAsync(new { accepted = true, complete }, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException or DecoderFallbackException)
        { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { accepted = false }, context.RequestAborted).ConfigureAwait(false); }
    }
}
