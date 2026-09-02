namespace G915Fix.Core.Input;

/// <summary>
/// Details the input filter's access state for the currently focused application.
/// </summary>
/// <param name="Status">The detected access state.</param>
/// <param name="ProcessName">The focused process name, when available.</param>
/// <param name="ProcessId">The focused process identifier, when available.</param>
/// <param name="Message">Platform-specific diagnostic detail, when available.</param>
public sealed record ForegroundInputAccessResult(
    ForegroundInputAccessStatus Status,
    string? ProcessName = null,
    int? ProcessId = null,
    string? Message = null);
