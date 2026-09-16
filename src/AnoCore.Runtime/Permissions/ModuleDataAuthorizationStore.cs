using System.Text.Json;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Permissions;

public sealed class ModuleDataAuthorizationStore : IAuthorizationStore
{
    private static readonly ModuleId Module = new("authorization");
    private const string StateKey = "state";

    private readonly IModuleDataStore _store;
    private readonly JsonSerializerOptions _jsonOptions;

    public ModuleDataAuthorizationStore(
        IModuleDataStore store,
        JsonSerializerOptions? jsonOptions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
    }

    public async ValueTask<AuthorizationState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _store.GetAsync(Module, StateKey, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AuthorizationState>(json, _jsonOptions)
                ?? throw new InvalidDataException("The persisted authorization state deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The persisted authorization state contains invalid JSON.", exception);
        }
    }

    public async ValueTask SaveAsync(
        AuthorizationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await _store.SetAsync(Module, StateKey, json, cancellationToken).ConfigureAwait(false);
    }
}
