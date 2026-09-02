using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using G915Fix.Core.Diagnostics;

namespace G915Fix.Heatmap;

/// <summary>Writes versioned diagnostics as UTF-8 JSON Lines.</summary>
public sealed class JsonLinesDiagnosticSink : IFilterDiagnosticSink, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public JsonLinesDiagnosticSink(string path, bool append = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _writer = new StreamWriter(fullPath, append, new UTF8Encoding(false));
    }

    public void Record(FilterDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(JsonSerializer.Serialize(diagnosticEvent, JsonOptions));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}

/// <summary>Reads valid versioned diagnostics from a JSON Lines log.</summary>
public static class JsonLinesDiagnosticLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async IAsyncEnumerable<FilterDiagnosticEvent> ReadAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            FilterDiagnosticEvent? diagnosticEvent;
            try
            {
                diagnosticEvent = JsonSerializer.Deserialize<FilterDiagnosticEvent>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (diagnosticEvent is not null)
            {
                yield return diagnosticEvent;
            }
        }
    }
}
