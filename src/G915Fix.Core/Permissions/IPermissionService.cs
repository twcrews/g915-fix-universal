namespace G915Fix.Core.Permissions;

/// <summary>Reports and guides user-consent requirements for platform capabilities.</summary>
public interface IPermissionService
{
    Task<IReadOnlyList<PermissionRequirement>> GetRequiredPermissionsAsync(CancellationToken cancellationToken = default);

    Task<PermissionRequirement?> GetPermissionAsync(string permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts any platform-supported consent flow. A result may require the user
    /// to finish the action manually in operating-system settings.
    /// </summary>
    Task<PermissionRequestResult> RequestPermissionAsync(string permissionId, CancellationToken cancellationToken = default);
}

public sealed record PermissionRequestResult(
    PermissionRequirement? Permission,
    bool RequestInitiated,
    string? SettingsUri = null,
    string? Message = null);
