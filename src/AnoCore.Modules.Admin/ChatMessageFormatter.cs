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

public sealed class PreparedChatFormat
{
    private readonly string _publicTemplate;
    private readonly string _teamTemplate;

    internal PreparedChatFormat(string publicTemplate, string teamTemplate)
    {
        _publicTemplate = publicTemplate;
        _teamTemplate = teamTemplate;
    }

    public string Format(string? message, bool isTeamMessage)
    {
        var sanitized = ChatMessageFormatter.Sanitize(
            message ?? string.Empty, ChatMessageFormatter.MaximumMessageLength);
        return (isTeamMessage ? _teamTemplate : _publicTemplate)
            .Replace("{message}", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ChatMessageFormatter
{
    internal const int MaximumMessageLength = 256;
    private const int MaximumNameLength = 48;
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

    public async ValueTask<PreparedChatFormat> PrepareAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        cancellationToken.ThrowIfCancellationRequested();
        var context = new PlaceholderContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["player"] = player.Id,
                ["session"] = player.SessionId,
            });
        var publicTemplate = await PrepareTemplateAsync(
            _configuration.PublicTemplate, player, context, cancellationToken)
            .ConfigureAwait(false);
        var teamTemplate = await PrepareTemplateAsync(
            _configuration.TeamTemplate, player, context, cancellationToken)
            .ConfigureAwait(false);
        return new PreparedChatFormat(publicTemplate, teamTemplate);
    }

    public async ValueTask<string> FormatAsync(
        ChatFormatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var player = new PlayerSnapshot(
            request.PlayerId,
            PlayerSessionId.New(),
            request.DisplayName,
            isConnected: true,
            isAlive: false,
            PlayerTeam.None,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var prepared = await PrepareAsync(player, cancellationToken).ConfigureAwait(false);
        return prepared.Format(request.Message, request.IsTeamMessage);
    }

    private async ValueTask<string> PrepareTemplateAsync(
        string template,
        PlayerSnapshot player,
        PlaceholderContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await _placeholders.ResolveAsync(
            template, context, cancellationToken).ConfigureAwait(false);
        var name = string.IsNullOrWhiteSpace(player.Name)
            ? player.Id.SteamId64.ToString(CultureInfo.InvariantCulture)
            : Sanitize(player.Name, MaximumNameLength);
        return resolved.Replace(
            "{player.name}", name, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Sanitize(string value, int maximumLength)
    {
        var sanitized = string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character));
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
