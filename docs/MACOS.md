# macOS local testing

`G915Fix.MacOS` is an unsigned, non-sandboxed Avalonia menu-bar application for
macOS 13 (Ventura) or later. It uses a CoreGraphics event tap to suppress input,
so it cannot filter while macOS Secure Input is active (for example, many password
fields). It does not currently implement automatic game-profile switching.

## Prerequisites

- macOS on Apple Silicon or Intel
- .NET SDK 10
- Xcode Command Line Tools (`actool`) when creating a local `.app` bundle

## Build and run

Run all tests first:

```bash
dotnet test G915Fix.slnx
```

For a quick debugger-oriented launch, run the project directly:

```bash
dotnet run --project src/G915Fix.MacOS/G915Fix.MacOS.csproj
```

For the recommended permission-testing route, create an application bundle and
move it to a stable location before granting TCC permissions:

```bash
scripts/package-macos.sh osx-arm64
# Use osx-x64 on an Intel Mac.
cp -R "artifacts/macos/osx-arm64/G915 Fix.app" /Applications/
open "/Applications/G915 Fix.app"
```

The bundle compiles the source-of-truth `res/app-icon.icon/` Icon Composer
asset directly with `actool`; it does not construct an icon from the asset's
individual files. The menu-bar icon uses `res/app-icon-black.png` in light
appearance and `res/app-icon-white.png` in dark appearance.

## Grant input permissions

1. On launch, **Permissions Required** opens automatically until both
   **Accessibility** and **Input Monitoring** are allowed. It is also available
   at any time from **All settings...** in the menu-bar menu, then the main
   window's **Open permissions** button.
2. Click each permission button marked **Not allowed**. macOS displays its
   consent prompt when it can. If a prompt was previously rejected or does not
   appear, click that permission again to open the matching Privacy & Security
   pane as a fallback.
3. Return to the window and verify both buttons read **Allowed**. Quit and reopen
   the app if macOS asks for it, then click **Initialize**. Filtering starts
   automatically.
4. Confirm the status reads **Active** before testing a keyboard or mouse.

The app stores `config.json` and JSON profiles in
`~/Library/Application Support/G915Fix/`, and diagnostic JSON Lines events in
`~/Library/Logs/G915Fix/filter-diagnostics.jsonl` when diagnostics are enabled.
Use **Open diagnostic heatmap** in the Application section to generate a
self-contained HTML file alongside the log and open it in the default browser.
It opens an empty report when no diagnostics have been recorded yet; enable
Diagnostics before filtering to populate it. The menu-bar menu also provides
**Filter keyboard**, **Filter mouse**, **Profile auto-switch**, **Track events**,
**Update games list...**, and **Event heatmap...** for the corresponding common
settings and actions; use **All settings...** for the complete settings window.

Closing the settings window hides it and leaves the menu-bar app running. Use
**Quit G915 Fix** from the menu-bar menu to stop the runtime and exit. The
**Start at login** toggle creates only the app-owned
`~/Library/LaunchAgents/com.twcrews.g915fix.plist`; test it only after placing
the bundle somewhere permanent, such as `/Applications`. Enabling it takes effect
at the next login and does not relaunch the app in the current session.

## Manual smoke checks

- Test ordinary typing, a known bouncing key, modifiers, arrows, keypad keys,
  and optional left/right/middle/X1/X2 mouse debounce.
- Test both `BlockRepress` and `BlockRelease`; changing a configuration while a
  key is held must not create a delayed key-up afterward.
- Revoke either TCC permission and verify **Initialize** reports `PermissionRequired`
  rather than claiming the filter is active.
- Enable autostart and verify the running app is not relaunched; after signing out
  and back in, verify exactly one G915 Fix instance starts.
- Test a password field and confirm the app fails open rather than blocking input.
- Test the bundle independently on both `osx-arm64` and `osx-x64` before release.

The local bundle is unsigned and should not be distributed. A release build needs
a stable signing identity, hardened-runtime validation, and notarization so TCC
permissions remain associated with the released application identity.
