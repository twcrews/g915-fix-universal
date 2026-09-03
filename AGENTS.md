# AGENTS.md

## Project Context

This repository is a fork. The goal of this fork is to add cross-platform functionality across Windows, macOS, and Linux.

The original project can be browsed at the public GitHub repo called `lucduguaysita/G915-Stutter-Fix`. Local git history can also be referenced, although the original code has been deleted in the latest commits.

## Guidance for Agents

- Preserve existing behavior unless a task explicitly requires changing it.
- Prefer changes that move the project toward platform-agnostic design.
- When adding or modifying functionality, consider Windows, macOS, and Linux compatibility.
- Avoid introducing platform-specific assumptions unless they are isolated behind clear abstractions.
- Update documentation when behavior or platform support changes.

## Architectural Decisions

- `G915Fix.Core` owns platform-neutral contracts and filtering policy. Native input capture/suppression, reinjection, elevation/UIPI checks, foreground-window inspection, process enumeration, and autostart registration belong in platform implementations, not Core.
- Keyboard input in Core is normalized to USB HID Keyboard/Keypad usages (`HidKeyboardUsage`); adapters must translate native key codes before calling Core. Do not add Windows virtual-key semantics back to Core.
- Core debounce filters are synchronous and must be called by a native hook before it delivers an event. `BlockRelease` uses `IReleaseScheduler` and `IKeyboardInputInjector`; platform implementations provide the native reinjection behavior.
- User-facing key tokens resolve through `IKeyboardTokenResolver` to HID usages. Windows VK numeric/config compatibility belongs in a Windows compatibility layer.
- `IForegroundInputAccessDetector`, `IGameProcessMonitor`, and `IAutostartService` are Core seams. Their platform implementations must accurately report unsupported, unknown, conflict, or user-consent states rather than pretending a feature works.
- Update checking is platform-neutral: `GitHubReleaseUpdateChecker` implements `IUpdateChecker` with injected `HttpClient`, reports a structured status, and only links to releases; it never downloads or installs updates.
- Game-list refresh logic is shared and portable (`DiscordGameListUpdater`), but network access remains isolated in the cross-platform `G915Fix.GameListUpdater` companion CLI. Select Discord's `win32`, `darwin`, or `linux` filter from the host platform; do not restore the old hard-coded Windows default.
- Structured filter diagnostics are versioned JSON Lines events (`FilterDiagnosticEvent`) emitted through `IFilterDiagnosticSink`. `G915Fix.Heatmap` consumes and streams/aggregates those events, renders self-contained generic HID HTML, and retains a legacy Windows text-log importer only for historical logs. Keep report rendering and device-specific layouts outside Core.
