using System.Globalization;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ChatFormatConfiguration
{
    public string PublicTemplate { get; set; } =
        "{rank.tag} {player.name}: {message}";

    public string TeamTemplate { get; set; } =
        "(TEAM) {rank.tag} {player.name}: {message}";

    public string RankColor { get; set; } = "None";

    public string NameColor { get; set; } = "None";

    public string MessageColor { get; set; } = "None";

    // Exact external command tokens only. Their owning plugin retains dispatch and authorization.
    public List<string> PassthroughCommands { get; set; } =
        [".map", ".prac", ".ready", ".unready", ".pause", ".unpause", ".stay", ".switch"];

    public bool IsPassthroughCommand(string input)
    {
        if (input.Length < 2 || input[0] != '.') return false;
        return PassthroughCommands.Any(command => input.StartsWith(command, StringComparison.OrdinalIgnoreCase)
            && (input.Length == command.Length || char.IsWhiteSpace(input[command.Length])));
    }

    public static ChatFormatConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(
        ChatFormatConfiguration configuration)
    {
        if (configuration is null)
            return ["Chat format configuration is required."];

        var errors = new List<string>();
        ValidateTemplate(configuration.PublicTemplate, "Public", errors);
        ValidateTemplate(configuration.TeamTemplate, "Team", errors);
        ValidateColor(configuration.RankColor, "Rank", errors);
        ValidateColor(configuration.NameColor, "Name", errors);
        ValidateColor(configuration.MessageColor, "Message", errors);
        if (configuration.PassthroughCommands is null || configuration.PassthroughCommands.Count > 64)
            errors.Add("PassthroughCommands must contain at most 64 exact dot-command tokens.");
        else
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in configuration.PassthroughCommands)
            {
                if (string.IsNullOrEmpty(command) || command.Length is < 2 or > 33
                    || command[0] != '.' || !char.IsAsciiLetter(command[1])
                    || command.Skip(2).Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')
                    || !names.Add(command))
                    errors.Add("PassthroughCommands entries must be unique .name tokens (1..32 ASCII letters/digits/underscores, starting with a letter).");
            }
        }
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

    private static void ValidateColor(
        string? color,
        string name,
        ICollection<string> errors)
    {
        if (!ChatColorPalette.IsSupported(color))
            errors.Add($"{name} chat color is not supported.");
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

    internal object? Policy { get; }
    internal object? TagPolicy { get; }

    internal PreparedChatFormat(string publicTemplate, string teamTemplate, object? policy = null, object? tagPolicy = null)
    {
        Policy = policy;
        TagPolicy = tagPolicy;
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

public sealed class ChatMessageFormatter : IDisposable
{
    internal const int MaximumMessageLength = 256;
    private const int MaximumNameLength = 48;
    private readonly ChatFormatConfiguration _configuration;
    private readonly IPlaceholderRegistry _placeholders;
    private IConfigReloadRegistration<ChatFormatConfiguration>? _reload;
    private object? _observedPolicy;
    private object? _observedTagPolicy;
    public Func<object?>? TagPolicyIdentity { get; set; }
    private ChatFormatConfiguration Current => _reload?.Current ?? _configuration;
    public bool IsPassthroughCommand(string input) => Current.IsPassthroughCommand(input);
    public bool IsCurrent(PreparedChatFormat prepared)
        => ReferenceEquals(prepared.Policy, Current) && ReferenceEquals(prepared.TagPolicy, TagPolicyIdentity?.Invoke());
    public bool ObserveReload()
    {
        var policy = Current;
        var tags = TagPolicyIdentity?.Invoke();
        var changed = !ReferenceEquals(policy, _observedPolicy) || !ReferenceEquals(tags, _observedTagPolicy);
        _observedPolicy = policy;
        _observedTagPolicy = tags;
        return changed;
    }
    public void Dispose() => _reload?.Dispose();

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
        CancellationToken cancellationToken = default,
        IConfigReloadRegistry? reloads = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(placeholders);
        var settings = await configuration.LoadAsync(
            "chat-format",
            () => ChatFormatConfiguration.Default,
            ChatFormatConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var formatter = new ChatMessageFormatter(settings, placeholders);
        formatter._reload = reloads?.Register(new ModuleId("ano.chat.format"), "chat-format", settings,
            token => configuration.LoadAsync("chat-format", () => ChatFormatConfiguration.Default,
                ChatFormatConfiguration.Validate, token), ChatFormatConfiguration.Validate);
        return formatter;
    }

    public async ValueTask<PreparedChatFormat> PrepareAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        cancellationToken.ThrowIfCancellationRequested();
        var policy = Current;
        var tagPolicy = TagPolicyIdentity?.Invoke();
        var context = new PlaceholderContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["player"] = player.Id,
                ["session"] = player.SessionId,
            });
        var publicTemplate = await PrepareTemplateAsync(
            policy.PublicTemplate, policy, player, context, cancellationToken)
            .ConfigureAwait(false);
        var teamTemplate = await PrepareTemplateAsync(
            policy.TeamTemplate, policy, player, context, cancellationToken)
            .ConfigureAwait(false);
        return new PreparedChatFormat(publicTemplate, teamTemplate, policy, tagPolicy);
    }

    public async ValueTask<string> FormatAsync(
        ChatFormatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var player = new PlayerSnapshot(
            request.PlayerId,
            PlayerSessionId.New(),
            string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.PlayerId.SteamId64.ToString(CultureInfo.InvariantCulture)
                : request.DisplayName,
            isConnected: true,
            isAlive: false,
            PlayerTeam.Unknown,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var prepared = await PrepareAsync(player, cancellationToken).ConfigureAwait(false);
        return prepared.Format(request.Message, request.IsTeamMessage);
    }

    private async ValueTask<string> PrepareTemplateAsync(
        string template,
        ChatFormatConfiguration policy,
        PlayerSnapshot player,
        PlaceholderContext context,
        CancellationToken cancellationToken)
    {
        var rankColor = ChatColorPalette.Resolve(
            policy.RankColor, player.Team);
        var effectiveTemplate = _placeholders.Contains("chat.tag")
            ? template.Replace(
                "{rank.tag}", "{chat.tag}", StringComparison.OrdinalIgnoreCase)
            : template;
        var decorated = Decorate(
            effectiveTemplate,
            rankColor,
            ChatColorPalette.Resolve(policy.NameColor, player.Team),
            ChatColorPalette.Resolve(policy.MessageColor, player.Team));
        var resolved = await _placeholders.ResolveAsync(
            decorated, context, cancellationToken).ConfigureAwait(false);
        if (rankColor is not null)
        {
            resolved = resolved.Replace(
                $"{rankColor}{ChatColorPalette.Default}",
                string.Empty,
                StringComparison.Ordinal);
        }
        var name = string.IsNullOrWhiteSpace(player.Name)
            ? player.Id.SteamId64.ToString(CultureInfo.InvariantCulture)
            : Sanitize(player.Name, MaximumNameLength);
        return resolved.Replace(
            "{player.name}", name, StringComparison.OrdinalIgnoreCase);
    }

    private static string Decorate(
        string template,
        char? rankColor,
        char? nameColor,
        char? messageColor)
        => DecorateToken(
            DecorateToken(
                DecorateToken(
                    DecorateToken(template, "{chat.tag}", rankColor),
                    "{rank.tag}", rankColor),
                "{player.name}", nameColor),
            "{message}", messageColor);

    private static string DecorateToken(
        string template,
        string token,
        char? color)
        => color is null
            ? template
            : template.Replace(
                token,
                $"{color}{token}{ChatColorPalette.Default}",
                StringComparison.OrdinalIgnoreCase);

    internal static string Sanitize(string value, int maximumLength)
    {
        var sanitized = string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character));
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
