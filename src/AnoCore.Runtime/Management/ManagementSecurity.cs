using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed record ManagementRateLimitOptions(
    int ReadRequestsPerMinute = 120,
    int PrivilegedRequestsPerMinute = 30,
    int MaximumTrackedKeys = 1024)
{
    public ManagementRateLimitOptions Validate()
    {
        if (ReadRequestsPerMinute is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(ReadRequestsPerMinute));
        if (PrivilegedRequestsPerMinute is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(PrivilegedRequestsPerMinute));
        if (MaximumTrackedKeys is < 16 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumTrackedKeys));
        return this;
    }
}

public sealed class ManagementRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly object _gate = new();
    private readonly ManagementRateLimitOptions _options;
    private readonly Dictionary<(string TokenId, ManagementOperationClass Class), Entry> _entries = [];

    public ManagementRateLimiter(ManagementRateLimitOptions? options = null)
        => _options = (options ?? new ManagementRateLimitOptions()).Validate();

    public bool TryAcquire(
        ManagementPrincipal principal,
        ManagementOperationClass operationClass,
        DateTimeOffset now,
        out TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!Enum.IsDefined(operationClass))
            throw new ArgumentOutOfRangeException(nameof(operationClass));

        var utc = now.ToUniversalTime();
        var key = (principal.TokenId, operationClass);
        lock (_gate)
        {
            Prune(utc);
            if (!_entries.TryGetValue(key, out var entry)
                || utc - entry.WindowStart >= Window)
            {
                if (!_entries.ContainsKey(key)
                    && _entries.Count >= _options.MaximumTrackedKeys)
                {
                    retryAfter = Window;
                    return false;
                }

                _entries[key] = new Entry(utc, 1);
                retryAfter = TimeSpan.Zero;
                return true;
            }

            var limit = operationClass == ManagementOperationClass.Read
                ? _options.ReadRequestsPerMinute
                : _options.PrivilegedRequestsPerMinute;
            if (entry.Count >= limit)
            {
                retryAfter = Window - (utc - entry.WindowStart);
                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
                return false;
            }

            _entries[key] = entry with { Count = entry.Count + 1 };
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _entries
            .Where(pair => now - pair.Value.WindowStart >= Window)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _entries.Remove(key);
        }
    }

    private sealed record Entry(DateTimeOffset WindowStart, int Count);
}

public sealed record ManagementAuditEvent(
    string Phase,
    string TokenId,
    ManagementCapabilityId Capability,
    string CorrelationId,
    string? ResultCode,
    DateTimeOffset OccurredAtUtc);

public sealed class ManagementCapabilityRegistry : IManagementCapabilityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<ManagementCapabilityId, Registration> _registrations = [];
    private readonly ManagementRateLimiter _rateLimiter;
    private readonly TimeProvider _time;
    private readonly Func<ManagementAuditEvent, CancellationToken, ValueTask>? _audit;

    public ManagementCapabilityRegistry(
        ManagementRateLimiter? rateLimiter = null,
        TimeProvider? timeProvider = null,
        Func<ManagementAuditEvent, CancellationToken, ValueTask>? audit = null)
    {
        _rateLimiter = rateLimiter ?? new ManagementRateLimiter();
        _time = timeProvider ?? TimeProvider.System;
        _audit = audit;
    }

    public IDisposable Register(
        AnoCore.Abstractions.Modules.ModuleId owner,
        ManagementCapabilityDescriptor descriptor,
        Func<ManagementRequestContext, ManagementOperationRequest,
            CancellationToken, ValueTask<ManagementOperationResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        descriptor.Validate();

        lock (_gate)
        {
            if (_registrations.ContainsKey(descriptor.Id))
                throw new InvalidOperationException(
                    $"Management capability '{descriptor.Id}' is already registered.");
            var registration = new Registration(owner, descriptor, handler);
            _registrations.Add(descriptor.Id, registration);
            return new Handle(this, descriptor.Id, registration);
        }
    }

    public async ValueTask<ManagementOperationResult> ExecuteAsync(
        ManagementRequestContext context,
        ManagementOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Registration registration;
        lock (_gate)
        {
            if (!_registrations.TryGetValue(request.Capability, out registration!))
                return ManagementOperationResult.Fail(
                    "not_found", "Management capability was not found.");
        }

        if (!context.Principal.Has(registration.Descriptor.RequiredScope))
            return ManagementOperationResult.Fail(
                "forbidden", "Management capability scope is not granted.");

        var now = _time.GetUtcNow();
        if (!_rateLimiter.TryAcquire(
                context.Principal,
                registration.Descriptor.OperationClass,
                now,
                out var retryAfter))
        {
            return ManagementOperationResult.Fail(
                "rate_limited",
                $"Management request limit exceeded; retry after {Math.Ceiling(retryAfter.TotalSeconds)} second(s).");
        }

        if (_audit is not null)
        {
            await _audit(
                new ManagementAuditEvent(
                    "requested",
                    context.Principal.TokenId,
                    registration.Descriptor.Id,
                    context.CorrelationId,
                    null,
                    now),
                cancellationToken).ConfigureAwait(false);
        }

        ManagementOperationResult result;
        try
        {
            result = await registration.Handler(
                context, request, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Management capability returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = ManagementOperationResult.Fail(
                "handler_failed", "Management capability failed.");
        }

        if (_audit is not null)
        {
            await _audit(
                new ManagementAuditEvent(
                    result.Success ? "completed" : "failed",
                    context.Principal.TokenId,
                    registration.Descriptor.Id,
                    context.CorrelationId,
                    result.Code,
                    _time.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public IReadOnlyList<ManagementCapabilityDescriptor> GetCapabilities()
    {
        lock (_gate)
        {
            return _registrations.Values
                .Select(value => value.Descriptor)
                .OrderBy(value => value.Id.Value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void Unregister(
        ManagementCapabilityId id,
        Registration expected)
    {
        lock (_gate)
        {
            if (_registrations.TryGetValue(id, out var current)
                && ReferenceEquals(current, expected))
            {
                _registrations.Remove(id);
            }
        }
    }

    private sealed record Registration(
        AnoCore.Abstractions.Modules.ModuleId Owner,
        ManagementCapabilityDescriptor Descriptor,
        Func<ManagementRequestContext, ManagementOperationRequest,
            CancellationToken, ValueTask<ManagementOperationResult>> Handler);

    private sealed class Handle(
        ManagementCapabilityRegistry owner,
        ManagementCapabilityId id,
        Registration registration) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unregister(id, registration);
        }
    }
}
