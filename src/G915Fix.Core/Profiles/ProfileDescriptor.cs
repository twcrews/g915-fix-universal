namespace G915Fix.Core.Profiles;

public sealed record ProfileDescriptor(
    string Name,
    string Path,
    bool IsDefault = false);
