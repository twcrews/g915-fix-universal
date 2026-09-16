using G915Fix.Core.Permissions;
using G915Fix.MacOS.Interop;

namespace G915Fix.MacOS.Services;

internal sealed class MacPermissionService : IPermissionService
{
    public const string AccessibilityPermissionId = "macos.accessibility";
    private const string AccessibilitySettingsUri = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    // Constructing a property list lets CoreFoundation create the real CFBoolean
    // object required by AXIsProcessTrustedWithOptions; a hand-built dictionary
    // with an invalid boolean reference can crash inside CoreFoundation.
    private static readonly byte[] AccessibilityPromptOptions = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0"><dict><key>AXTrustedCheckOptionPrompt</key><true/></dict></plist>
        """u8.ToArray();
    private bool _accessibilityPromptRequested;

    public Task<IReadOnlyList<PermissionRequirement>> GetRequiredPermissionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PermissionRequirement>>([DescribeAccessibility()]);
    }

    public Task<PermissionRequirement?> GetPermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PermissionRequirement?>(permissionId == AccessibilityPermissionId
            ? DescribeAccessibility()
            : null);
    }

    public Task<PermissionRequestResult> RequestPermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermissionRequirement? permission = permissionId == AccessibilityPermissionId
            ? RequestAccessibility()
            : null;
        return Task.FromResult(new PermissionRequestResult(
            permission,
            permission is not null,
            permission?.Status == PermissionStatus.Granted ? null : AccessibilitySettingsUri,
            permission?.Message));
    }

    public Task<bool> HasInputFilteringPermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // This app uses a suppressing event tap, for which Accessibility is the
        // consent required by macOS. It does not use a listen-only event monitor.
        return Task.FromResult(MacNative.AXIsProcessTrusted());
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

    private PermissionRequirement RequestAccessibility()
    {
        if (MacNative.AXIsProcessTrusted())
        {
            return DescribeAccessibility();
        }

        if (_accessibilityPromptRequested)
        {
            _ = TryOpenSettings(AccessibilitySettingsUri);
            return DescribeAccessibility() with
            {
                Message = "System Settings was opened for Accessibility. Allow G915 Fix, then return to the app.",
                RequiredAction = PermissionAction.CompleteManualSetup
            };
        }

        _accessibilityPromptRequested = true;
        _ = RequestAccessibilityPrompt();
        return DescribeAccessibility() with
        {
            Message = "Respond to the macOS Accessibility prompt. If it does not appear, click this permission again to open System Settings.",
            RequiredAction = PermissionAction.Request
        };
    }

    private static bool RequestAccessibilityPrompt()
    {
        IntPtr data = MacNative.CFDataCreate(IntPtr.Zero, AccessibilityPromptOptions, AccessibilityPromptOptions.Length);
        if (data == IntPtr.Zero)
        {
            return false;
        }

        IntPtr options = IntPtr.Zero;
        IntPtr error = IntPtr.Zero;
        try
        {
            options = MacNative.CFPropertyListCreateWithData(IntPtr.Zero, data, 0, out _, out error);
            return options != IntPtr.Zero && MacNative.AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            if (error != IntPtr.Zero) MacNative.CFRelease(error);
            if (options != IntPtr.Zero) MacNative.CFRelease(options);
            MacNative.CFRelease(data);
        }
    }

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
