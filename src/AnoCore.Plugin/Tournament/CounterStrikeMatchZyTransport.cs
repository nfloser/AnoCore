using AnoCore.Modules.Tournament;
using CounterStrikeSharp.API;

namespace AnoCore.Plugin.Tournament;

/// <summary>Only a fixed local configuration path reaches the server console.</summary>
public sealed class CounterStrikeMatchZyTransport : ITournamentMatchZyTransport
{
    private const string RelativePath = "cfg/anocore/tournament-matchzy.json";
    public async ValueTask PrepareAsync(long id, string json, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Server.GameDirectory, "csgo", "cfg", "anocore", "tournament-matchzy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public ValueTask EndAsync(CancellationToken cancellationToken) => ExecuteAsync("css_endmatch", cancellationToken);
    public ValueTask LoadPreparedAsync(CancellationToken cancellationToken) => ExecuteAsync("matchzy_loadmatch " + RelativePath, cancellationToken);
    private static async ValueTask ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Server.NextWorldUpdate(() =>
        {
            if (cancellationToken.IsCancellationRequested) { completed.TrySetCanceled(cancellationToken); return; }
            try { Server.ExecuteCommand(command); completed.TrySetResult(); }
            catch (Exception exception) { completed.TrySetException(exception); }
        });
        await completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
