namespace G915Fix.Core.Permissions;

public interface IPermissionService
{
    Task<IReadOnlyList<PermissionRequirement>> GetRequiredPermissionsAsync(CancellationToken cancellationToken = default);

    Task RequestPermissionAsync(string permissionId, CancellationToken cancellationToken = default);
}
