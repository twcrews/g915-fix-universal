using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace G915Fix.Core.Diagnostics;

/// <summary>
/// Writes diagnostics off the input callback path. Recording is always
/// non-blocking; queue pressure or I/O failures never affect filtering.
/// </summary>
public sealed class JsonLinesFilterDiagnosticSink : IFilterDiagnosticSink, IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly Channel<FilterDiagnosticEvent> _channel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _writerTask;
    private long _droppedEvents;
    private string? _error;
    private int _disposed;

    public JsonLinesFilterDiagnosticSink(string path, int capacity = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));

        Path = System.IO.Path.GetFullPath(path);
        _channel = Channel.CreateBounded<FilterDiagnosticEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(WriteAsync);
    }

    public string Path { get; }

    public FilterDiagnosticSinkStatus Status => new(
        Volatile.Read(ref _error) is null,
        Interlocked.Read(ref _droppedEvents),
        Volatile.Read(ref _error));

    public void Record(FilterDiagnosticEvent diagnosticEvent)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!_channel.Writer.TryWrite(diagnosticEvent))
        {
            Interlocked.Increment(ref _droppedEvents);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await _writerTask.ConfigureAwait(false);
        }
        catch
        {
            // Writer failures are represented by Status and must not leak during shutdown.
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task WriteAsync()
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = new(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };
            await foreach (FilterDiagnosticEvent diagnosticEvent in _channel.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(diagnosticEvent, SerializerOptions)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            Interlocked.CompareExchange(ref _error, "Diagnostic writer shut down before all events were written.", null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            Interlocked.CompareExchange(ref _error, exception.Message, null);
            while (_channel.Reader.TryRead(out _))
            {
                Interlocked.Increment(ref _droppedEvents);
            }
        }
    }
}

public sealed record FilterDiagnosticSinkStatus(bool IsOperational, long DroppedEvents, string? Error);
