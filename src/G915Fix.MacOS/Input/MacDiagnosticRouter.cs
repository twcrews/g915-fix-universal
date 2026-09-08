using G915Fix.Core.Configuration;
using G915Fix.Core.Diagnostics;

namespace G915Fix.MacOS.Input;

/// <summary>Keeps disk I/O off the event tap while allowing live diagnostic reconfiguration.</summary>
internal sealed class MacDiagnosticRouter : IFilterDiagnosticSink, IDisposable
{
    private readonly object _sync = new();
    private IFilterDiagnosticSink? _sink;

    public void Configure(DiagnosticRuntimeOptions? options)
    {
        IFilterDiagnosticSink? replacement = options is { Enabled: true } && !string.IsNullOrWhiteSpace(options.LogPath)
            ? new JsonLinesFilterDiagnosticSink(options.LogPath)
            : null;
        IFilterDiagnosticSink? previous;
        lock (_sync)
        {
            previous = _sink;
            _sink = replacement;
        }
        if (previous is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public void Record(FilterDiagnosticEvent diagnosticEvent)
    {
        IFilterDiagnosticSink? sink;
        lock (_sync)
        {
            sink = _sink;
        }
        sink?.Record(diagnosticEvent);
    }

    public void Dispose() => Configure(null);
}
