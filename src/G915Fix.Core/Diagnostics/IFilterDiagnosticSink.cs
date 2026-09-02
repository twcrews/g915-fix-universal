namespace G915Fix.Core.Diagnostics;

/// <summary>
/// Receives filter diagnostics synchronously. Implementations must be fast and
/// must not allow recording failures to affect input filtering.
/// </summary>
public interface IFilterDiagnosticSink
{
    void Record(FilterDiagnosticEvent diagnosticEvent);
}
