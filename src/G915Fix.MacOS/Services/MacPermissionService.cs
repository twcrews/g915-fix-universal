using G915Fix.Core.Permissions;
using G915Fix.MacOS.Interop;

namespace G915Fix.MacOS.Services;

internal sealed class MacPermissionService : IPermissionService
{
    public const string AccessibilityPermissionId = "macos.accessibility";
    public const string InputMonitoringPermissionId = "macos.input-monitoring";
    private const string AccessibilitySettingsUri = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private const string InputMonitoringSettingsUri = "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent";

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
            permission?.Status == PermissionStatus.Granted ? null : GetSettingsUri(permissionId),
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
        _ = RequestAccessibilityPrompt();
        // TCC can suppress a prompt after a previous denial. Opening the exact pane
        // is the reliable fallback and also gives the user a visible way to finish.
        _ = TryOpenSettings(AccessibilitySettingsUri);
        return DescribeAccessibility() with { RequiredAction = PermissionAction.CompleteManualSetup };
    }

    private static PermissionRequirement RequestInputMonitoring()
    {
        _ = MacNative.CGRequestListenEventAccess();
        // CGRequestListenEventAccess prompts where macOS permits it. It does not
        // report whether TCC suppressed a previously rejected prompt, so provide
        // the nearest System Settings view as the fallback path.
        _ = TryOpenSettings(InputMonitoringSettingsUri);
        return DescribeInputMonitoring() with { RequiredAction = PermissionAction.CompleteManualSetup };
    }

    private static bool RequestAccessibilityPrompt()
    {
        IntPtr key = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXTrustedCheckOptionPrompt", MacNative.Utf8StringEncoding);
        if (key == IntPtr.Zero)
        {
            return false;
        }

        IntPtr options = IntPtr.Zero;
        try
        {
            options = MacNative.CFDictionaryCreate(
                IntPtr.Zero,
                [key],
                [MacNative.GetBooleanTrue()],
                1,
                IntPtr.Zero,
                IntPtr.Zero);
            return options != IntPtr.Zero && MacNative.AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            if (options != IntPtr.Zero) MacNative.CFRelease(options);
            MacNative.CFRelease(key);
        }
    }

    private static string GetSettingsUri(string permissionId) => permissionId == InputMonitoringPermissionId
        ? InputMonitoringSettingsUri
        : AccessibilitySettingsUri;

    private static bool TryOpenSettings(string settingsUri)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", settingsUri)
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
