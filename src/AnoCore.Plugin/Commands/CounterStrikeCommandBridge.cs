using System.Text;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Commands;

public sealed class CounterStrikeCommandBridge : IDisposable
{
    private readonly object _gate = new();
    private readonly BasePlugin _plugin;
    private readonly IAnoCommandRegistry _registry;
    private readonly ILogger _logger;
    private readonly Action<string, CCSPlayerController?>? _afterDispatch;
    private readonly Dictionary<string, CommandInfo.CommandCallback> _bindings = new(StringComparer.Ordinal);
    private bool _disposed;
    private readonly CommandBindingSynchronizer _synchronizer;
    private readonly CommandRegistry? _observableRegistry;

    public CounterStrikeCommandBridge(
        BasePlugin plugin,
        IAnoCommandRegistry registry,
        ILogger logger,
        Action<string, CCSPlayerController?>? afterDispatch = null)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _afterDispatch = afterDispatch;
        _synchronizer = new CommandBindingSynchronizer(registry, Bind);
        _observableRegistry = registry as CommandRegistry;
        if (_observableRegistry is not null) _observableRegistry.Changed += QueueSynchronization;
    }

    public void Synchronize() => _synchronizer.Synchronize();

    private void QueueSynchronization()
        => Server.NextWorldUpdate(() =>
        {
            if (Volatile.Read(ref _disposed)) return;
            try { Synchronize(); }
            catch (Exception exception) { _logger.LogError(exception, "Native command synchronization failed."); }
        });

    public IDisposable Bind(CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        var logicalNames = new[] { descriptor.Name }.Concat(descriptor.Aliases).ToArray();
        var created = new List<(string NativeName, CommandInfo.CommandCallback Callback)>();

        lock (_gate)
        {
            foreach (var logicalName in logicalNames)
            {
                var nativeName = $"css_{logicalName}";
                if (_bindings.ContainsKey(nativeName))
                {
                    throw new InvalidOperationException($"CounterStrikeSharp command '{nativeName}' is already bound.");
                }
            }

            try
            {
                foreach (var logicalName in logicalNames)
                {
                    var nativeName = $"css_{logicalName}";
                    CommandInfo.CommandCallback callback = (player, info) => Dispatch(logicalName, player, info);
                    _plugin.AddCommand(nativeName, descriptor.Description, callback);
                    _bindings.Add(nativeName, callback);
                    created.Add((nativeName, callback));
                }
            }
            catch
            {
                foreach (var binding in created)
                {
                    _plugin.RemoveCommand(binding.NativeName, binding.Callback);
                    _bindings.Remove(binding.NativeName);
                }

                throw;
            }
        }

        return new BindingHandle(this, created);
    }

    public void Dispose()
    {
        if (_observableRegistry is not null) _observableRegistry.Changed -= QueueSynchronization;
        _synchronizer.Dispose();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var pair in _bindings.ToArray())
            {
                _plugin.RemoveCommand(pair.Key, pair.Value);
            }

            _bindings.Clear();
            _disposed = true;
        }
    }

    private void Dispatch(string logicalName, CCSPlayerController? player, CommandInfo info)
    {
        PlayerId? playerId = null;
        if (player is not null)
        {
            if (!player.IsValid || player.SteamID == 0)
            {
                return;
            }

            try
            {
                playerId = new PlayerId(player.SteamID);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
        }

        var callingContext = info.CallingContext;
        var input = BuildInvocation(logicalName, info);
        _ = DispatchAsync(logicalName, input, playerId, player, callingContext);
    }

    private async Task DispatchAsync(
        string logicalName,
        string input,
        PlayerId? playerId,
        CCSPlayerController? player,
        CommandCallingContext callingContext)
    {
        try
        {
            var result = await _registry.ExecuteAsync(input, playerId).ConfigureAwait(false);
            var message = result.Message;
            if (string.IsNullOrWhiteSpace(message) && _afterDispatch is null)
            {
                return;
            }

            Server.NextWorldUpdate(() =>
            {
                if (Volatile.Read(ref _disposed)
                    || (player is not null && (!player.IsValid || player.SteamID != playerId?.SteamId64)))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    Reply(player, callingContext, message);
                }

                try
                {
                    _afterDispatch?.Invoke(logicalName, player);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "AnoCore post-command presentation failed for {CommandName}.", logicalName);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AnoCore command dispatch failed for {CommandName}.", logicalName);
        }
    }

    private static string BuildInvocation(string logicalName, CommandInfo info)
    {
        var builder = new StringBuilder("!").Append(logicalName);
        for (var index = 1; index < info.ArgCount; index++)
        {
            builder.Append(' ').Append(Quote(info.GetArg(index)));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void Reply(CCSPlayerController? player, CommandCallingContext callingContext, string message)
    {
        if (player is null)
        {
            Server.PrintToConsole(message);
            return;
        }

        if (!player.IsValid)
        {
            return;
        }

        if (callingContext == CommandCallingContext.Console)
        {
            player.PrintToConsole(message);
        }
        else
        {
            player.PrintToChat(message);
        }
    }

    private void Remove(IReadOnlyCollection<(string NativeName, CommandInfo.CommandCallback Callback)> bindings)
    {
        lock (_gate)
        {
            foreach (var binding in bindings)
            {
                if (_bindings.TryGetValue(binding.NativeName, out var current) && current == binding.Callback)
                {
                    _plugin.RemoveCommand(binding.NativeName, binding.Callback);
                    _bindings.Remove(binding.NativeName);
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class BindingHandle : IDisposable
    {
        private CounterStrikeCommandBridge? _bridge;
        private readonly IReadOnlyCollection<(string NativeName, CommandInfo.CommandCallback Callback)> _bindings;

        public BindingHandle(
            CounterStrikeCommandBridge bridge,
            IReadOnlyCollection<(string NativeName, CommandInfo.CommandCallback Callback)> bindings)
        {
            _bridge = bridge;
            _bindings = bindings;
        }

        public void Dispose() => Interlocked.Exchange(ref _bridge, null)?.Remove(_bindings);
    }
}
