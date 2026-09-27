using System.Globalization;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ChatFormatConfiguration
{
    public string PublicTemplate { get; set; } =
        "{rank.tag} {player.name}: {message}";

    public string TeamTemplate { get; set; } =
        "(TEAM) {rank.tag} {player.name}: {message}";

    public static ChatFormatConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(
        ChatFormatConfiguration configuration)
    {
        if (configuration is null)
            return ["Chat format configuration is required."];

        var errors = new List<string>();
        ValidateTemplate(configuration.PublicTemplate, "Public", errors);
        ValidateTemplate(configuration.TeamTemplate, "Team", errors);
        return errors;
    }

    private static void ValidateTemplate(
        string? template, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template)
            || template.Length > 256
            || template.Any(char.IsControl))
        {
            errors.Add($"{name} chat template must contain 1 to 256 printable characters.");
            return;
        }

        if (Count(template, "{player.name}") != 1
            || Count(template, "{message}") != 1)
        {
            errors.Add($"{name} chat template must contain player.name and message exactly once.");
        }
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }
}

public sealed record ChatFormatRequest(
    PlayerId PlayerId,
    string DisplayName,
    string Message,
    bool IsTeamMessage);

public sealed class ChatMessageFormatter
{
    private const int MaximumNameLength = 48;
    private const int MaximumMessageLength = 256;
    private readonly ChatFormatConfiguration _configuration;
    private readonly IPlaceholderRegistry _placeholders;

    private ChatMessageFormatter(
        ChatFormatConfiguration configuration,
        IPlaceholderRegistry placeholders)
    {
        _configuration = configuration;
        _placeholders = placeholders;
    }

    public static async Task<ChatMessageFormatter> CreateAsync(
        IConfigStore configuration,
        IPlaceholderRegistry placeholders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(placeholders);
        var settings = await configuration.LoadAsync(
            "chat-format",
            () => ChatFormatConfiguration.Default,
            ChatFormatConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ChatMessageFormatter(settings, placeholders);
    }

    public async ValueTask<string> FormatAsync(
        ChatFormatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var template = request.IsTeamMessage
            ? _configuration.TeamTemplate
            : _configuration.PublicTemplate;
        var context = new PlaceholderContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["player"] = request.PlayerId,
            });
        var resolved = await _placeholders.ResolveAsync(
            template, context, cancellationToken).ConfigureAwait(false);

        var name = string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.PlayerId.SteamId64.ToString(CultureInfo.InvariantCulture)
            : Sanitize(request.DisplayName, MaximumNameLength);
        var message = Sanitize(request.Message ?? string.Empty, MaximumMessageLength);
        return resolved
            .Replace("{player.name}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{message}", message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value, int maximumLength)
    {
        var sanitized = string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character));
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
