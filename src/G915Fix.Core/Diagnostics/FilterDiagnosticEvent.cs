using G915Fix.Core.Input;

namespace G915Fix.Core.Diagnostics;

/// <summary>A versioned, platform-neutral filter diagnostic event.</summary>
public sealed record FilterDiagnosticEvent(
    int SchemaVersion,
    DateTimeOffset Timestamp,
    FilterDiagnosticEventKind Kind,
    HidKeyboardUsage? Key = null,
    MouseButton? MouseButton = null,
    FilterDiagnosticAction? Action = null,
    string? Message = null)
{
    public const int CurrentSchemaVersion = 1;
}
