namespace AnoCore.Modules.Admin;

/// <summary>Prepares trusted, already formatted text for native CS2 chat output.</summary>
public static class NativeChatText
{
    public static string Prepare(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Native chat needs a printable prefix before an initial color control.
        // K4-Zenith uses the same leading-space convention in its chat formatter.
        return text.StartsWith(' ') ? text : string.Concat(" ", text);
    }
}
