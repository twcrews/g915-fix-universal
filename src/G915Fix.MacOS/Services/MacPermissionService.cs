using G915Fix.Core.Permissions;
using G915Fix.MacOS.Interop;

namespace G915Fix.MacOS.Services;

internal sealed class MacPermissionService : IPermissionService
{
    public const string AccessibilityPermissionId = "macos.accessibility";
    public const string InputMonitoringPermissionId = "macos.input-monitoring";
    private const string PrivacySettingsUri = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    public Task<IReadOnlyList<PermissionRequirement>> GetRequiredPermissionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PermissionRequirement>>(
        [
            DescribeAccessibility(),
            DescribeInputMonitoring()
        ]);
    }

    public Task<PermissionRequirement?> GetPermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PermissionRequirement?>(permissionId switch
        {
            AccessibilityPermissionId => DescribeAccessibility(),
            InputMonitoringPermissionId => DescribeInputMonitoring(),
            _ => null
        });
    }

    public Task<PermissionRequestResult> RequestPermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermissionRequirement? permission = permissionId switch
        {
            AccessibilityPermissionId => RequestAccessibility(),
            InputMonitoringPermissionId => RequestInputMonitoring(),
            _ => null
        };
        return Task.FromResult(new PermissionRequestResult(
            permission,
            permission is not null,
            permission?.Status == PermissionStatus.Granted ? null : PrivacySettingsUri,
            permission?.Message));
    }

    public Task<bool> HasInputFilteringPermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Suppressing events requires Accessibility. Input Monitoring is separately
        // preflighted because current macOS releases can deny keyboard observation
        // even when Accessibility has already been granted.
        return Task.FromResult(MacNative.AXIsProcessTrusted() && MacNative.CGPreflightListenEventAccess());
    }

    private static PermissionRequirement DescribeAccessibility()
    {
        bool granted = MacNative.AXIsProcessTrusted();
        return new PermissionRequirement(
            AccessibilityPermissionId,
            "Accessibility",
            granted ? PermissionStatus.Granted : PermissionStatus.Denied,
            granted ? "Accessibility access is granted." : "Allow G915 Fix to control your computer in Privacy & Security > Accessibility.",
            granted ? PermissionAction.None : PermissionAction.Request);
    }

    private static PermissionRequirement DescribeInputMonitoring()
    {
        bool granted = MacNative.CGPreflightListenEventAccess();
        return new PermissionRequirement(
            InputMonitoringPermissionId,
            "Input Monitoring",
            granted ? PermissionStatus.Granted : PermissionStatus.Denied,
            granted ? "Input Monitoring access is granted." : "Allow G915 Fix in Privacy & Security > Input Monitoring, then start filtering again.",
            granted ? PermissionAction.None : PermissionAction.Request);
    }

    private static PermissionRequirement RequestAccessibility()
    {
        // The documented Accessibility request API accepts a CFDictionary. Avalonia
        // does not ship Objective-C bindings, so open the exact System Settings pane
        // rather than fabricating an undocumented prompt dictionary.
        _ = TryOpenSettings();
        return DescribeAccessibility() with { RequiredAction = PermissionAction.CompleteManualSetup };
    }

    private static PermissionRequirement RequestInputMonitoring()
    {
        _ = MacNative.CGRequestListenEventAccess();
        _ = TryOpenSettings();
        return DescribeInputMonitoring() with { RequiredAction = PermissionAction.CompleteManualSetup };
    }

    private static bool TryOpenSettings()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", PrivacySettingsUri)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
