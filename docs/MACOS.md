# macOS local testing

`G915Fix.MacOS` is an unsigned, non-sandboxed Avalonia menu-bar application for
macOS 13 (Ventura) or later. It uses a CoreGraphics event tap to suppress input,
so it cannot filter while macOS Secure Input is active (for example, many password
fields). It does not currently implement automatic game-profile switching.

## Prerequisites

- macOS on Apple Silicon or Intel
- .NET SDK 10
- Xcode Command Line Tools (`sips`, `iconutil`, and `plutil`) when creating a
  local `.app` bundle

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

The bundle script derives `app-icon.icns` from the source-of-truth
`res/app-icon.icon/` asset and copies that authored asset into the bundle. The
menu-bar icon uses `res/app-icon-black.png` in light appearance and
`res/app-icon-white.png` in dark appearance.

## Grant input permissions

1. Open **G915 Fix** from the menu bar, then click **Initialize**.
2. In the **Permissions** section, select and grant **Accessibility** and
   **Input Monitoring**. The button opens the relevant Privacy & Security area;
   Input Monitoring may need to be selected manually after it opens.
3. Quit and reopen the app if macOS asks for it, click **Initialize** again, then
   click **Start**.
4. Confirm the status reads **Active** before testing a keyboard or mouse.

The app stores `config.json` and JSON profiles in
`~/Library/Application Support/G915Fix/`, and diagnostic JSON Lines events in
`~/Library/Logs/G915Fix/filter-diagnostics.jsonl` when diagnostics are enabled.
Use **Open diagnostic heatmap** in the Application section to generate a
self-contained HTML file alongside the log and open it in the default browser.
It opens an empty report when no diagnostics have been recorded yet; enable
Diagnostics and save before filtering to populate it.

Closing the settings window hides it and leaves the menu-bar app running. Use
**Quit G915 Fix** from the menu-bar menu to stop the runtime and exit. The
**Toggle autostart** control creates only the app-owned
`~/Library/LaunchAgents/com.twcrews.g915fix.plist`; test it only after placing
the bundle somewhere permanent, such as `/Applications`.

## Manual smoke checks

- Test ordinary typing, a known bouncing key, modifiers, arrows, keypad keys,
  and optional left/right/middle/X1/X2 mouse debounce.
- Test both `BlockRepress` and `BlockRelease`; stopping or saving a configuration
  while a key is held must not create a delayed key-up afterward.
- Revoke either TCC permission and verify **Start** reports `PermissionRequired`
  rather than claiming the filter is active.
- Test a password field and confirm the app fails open rather than blocking input.
- Test the bundle independently on both `osx-arm64` and `osx-x64` before release.

The local bundle is unsigned and should not be distributed. A release build needs
a stable signing identity, hardened-runtime validation, and notarization so TCC
permissions remain associated with the released application identity.
