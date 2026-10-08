namespace AnoCore.Modules.Stats;

/// <summary>
/// Keeps a validated native GameRules reference while reading its current warmup state.
/// The host must call this only on the game thread and reset it at map boundaries/unload.
/// Missing references are retried on the next tick rather than on every combat event.
/// </summary>
public sealed class WarmupStateCache<T> where T : class
{
    private readonly Func<T?> _resolve;
    private readonly Func<T, bool> _isValid;
    private readonly Func<T, bool?> _read;
    private T? _entity;
    private int? _missingTick;

    public WarmupStateCache(Func<T?> resolve, Func<T, bool> isValid, Func<T, bool?> read)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _isValid = isValid ?? throw new ArgumentNullException(nameof(isValid));
        _read = read ?? throw new ArgumentNullException(nameof(read));
    }

    public bool? Read(int tick)
    {
        try
        {
            if (_entity is not null && !_isValid(_entity)) _entity = null;
            if (_entity is null)
            {
                if (_missingTick == tick) return null;
                _missingTick = tick;
                var candidate = _resolve();
                if (candidate is null || !_isValid(candidate)) return null;
                _entity = candidate;
            }

            var warmup = _read(_entity);
            if (warmup is null)
            {
                _entity = null;
                _missingTick = tick;
            }
            else _missingTick = null;
            return warmup;
        }
        catch
        {
            _entity = null;
            _missingTick = tick;
            throw;
        }
    }

    public void Reset()
    {
        _entity = null;
        _missingTick = null;
    }
}
