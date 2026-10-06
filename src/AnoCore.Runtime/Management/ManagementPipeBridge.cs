using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace AnoCore.Runtime.Management;

public sealed class ManagementPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly ManagementHttpAdapter _adapter;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<Exception>? _onError;
    private Task? _runTask;
    private int _started;
    private int _disposed;

    public ManagementPipeServer(
        string pipeName,
        ManagementHttpAdapter adapter,
        Action<Exception>? onError = null)
    {
        if (!ManagementBridgeConfiguration.ValidPipeName(pipeName))
        {
            throw new ArgumentException("Invalid management pipe name.", nameof(pipeName));
        }

        _pipeName = pipeName.Trim();
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _onError = onError;
    }

    public string PipeName => _pipeName;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Management pipe server is already started.");
        }

        NamedPipeServerStream? first = null;
        try
        {
            first = CreateServer();
            _runTask = RunAsync(first, _lifetime.Token);
            first = null;
        }
        catch
        {
            first?.Dispose();
            Interlocked.Exchange(ref _started, 0);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        try
        {
            _runTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private NamedPipeServerStream CreateServer()
        => new(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task RunAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var request = await ManagementPipeProtocol.ReadAsync<ManagementHttpRequest>(
                        pipe, cancellationToken).ConfigureAwait(false);
                    var response = await _adapter.HandleAsync(
                        request, cancellationToken).ConfigureAwait(false);
                    await ManagementPipeProtocol.WriteAsync(
                        pipe, response, cancellationToken).ConfigureAwait(false);
                    await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    ReportError(exception);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                pipe = CreateServer();
            }
            catch (Exception exception)
            {
                ReportError(exception);
                return;
            }
        }
    }

    private void ReportError(Exception exception)
    {
        try
        {
            _onError?.Invoke(exception);
        }
        catch
        {
            // Error reporting must never take down the local bridge loop.
        }
    }
}

public sealed class ManagementPipeClient
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public ManagementPipeClient(
        string pipeName,
        TimeSpan? connectTimeout = null)
    {
        if (!ManagementBridgeConfiguration.ValidPipeName(pipeName))
        {
            throw new ArgumentException("Invalid management pipe name.", nameof(pipeName));
        }

        _pipeName = pipeName.Trim();
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        if (_connectTimeout <= TimeSpan.Zero || _connectTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }
    }

    public async ValueTask<ManagementHttpResponse> SendAsync(
        ManagementHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("AnoCore management pipe connection timed out.");
        }

        await ManagementPipeProtocol.WriteAsync(
            pipe, request, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ManagementPipeProtocol.ReadAsync<ManagementHttpResponse>(
            pipe, cancellationToken).ConfigureAwait(false);
    }
}

internal static class ManagementPipeProtocol
{
    public const int MaxFrameBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async ValueTask WriteAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length == 0 || payload.Length > MaxFrameBytes)
        {
            throw new InvalidDataException("Management pipe frame exceeds the supported size.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaxFrameBytes)
        {
            throw new InvalidDataException("Management pipe frame has an invalid size.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new InvalidDataException("Management pipe frame deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Management pipe frame contains invalid JSON.", exception);
        }
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(
                buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Management pipe closed before the frame completed.");
            }

            read += count;
        }
    }
}
