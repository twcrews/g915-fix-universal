namespace G915Fix.Core.Games;

public sealed record GameProcess(
    string ExecutableName,
    int? ProcessId = null);
