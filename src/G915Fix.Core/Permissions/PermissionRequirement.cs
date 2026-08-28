namespace G915Fix.Core.Permissions;

public sealed record PermissionRequirement(
    string Id,
    string DisplayName,
    PermissionStatus Status,
    string? Message = null);
