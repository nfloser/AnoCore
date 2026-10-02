using System.Text.RegularExpressions;
using AnoCore.Abstractions.Permissions;

namespace AnoCore.Modules.Admin;

public sealed record SelectableChatTag(string Id, string Text, string Permission);

public sealed class SelectableChatTagConfiguration
{
    private static readonly Regex SafeId = new(
        "^[a-z][a-z0-9_-]{0,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public List<SelectableChatTag> Tags { get; set; } = [];

    public static SelectableChatTagConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(
        SelectableChatTagConfiguration configuration)
    {
        if (configuration is null)
            return ["Chat tag configuration is required."];
        if (configuration.Tags is null || configuration.Tags.Count > 32)
            return ["Define no more than 32 selectable chat tags."];

        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in configuration.Tags)
        {
            if (tag is null || string.IsNullOrWhiteSpace(tag.Id)
                || !SafeId.IsMatch(tag.Id) || !ids.Add(tag.Id))
                errors.Add("Chat tag IDs must be unique lowercase identifiers.");
            if (tag is null || string.IsNullOrWhiteSpace(tag.Text)
                || tag.Text.Length > 24
                || tag.Text.Any(character => char.IsControl(character)
                    || character is '{' or '}'))
                errors.Add("Chat tag text must contain 1 to 24 printable characters without braces.");
            try
            {
                _ = new PermissionId(tag?.Permission ?? string.Empty);
            }
            catch (ArgumentException)
            {
                errors.Add("Chat tag permissions must use the ano.* namespace.");
            }
        }

        return errors;
    }
}
