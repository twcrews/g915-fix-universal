# Core application boundary

`G915Fix.Core` defines portable filtering policy and the contracts consumed by the shared desktop UI. It must not reference Avalonia, CoreGraphics, Win32, Linux display servers, native key codes, or platform-specific configuration locations.

## Configuration

`AppConfiguration` is the canonical, JSON-bindable configuration model. Hosts load it with the standard `Microsoft.Extensions.Configuration` JSON provider; `ConfigurationCompiler` resolves HID key tokens and produces the immutable debounce options used by filters.

`config.template.json` contains the current schema. The canonical schema uses HID names such as `Backspace`, `Enter`, and `LeftControl`; Windows virtual-key migration belongs in a Windows compatibility layer, not Core.

`JsonAppConfigurationStore` supplies the missing write half of .NET configuration with atomic app-owned JSON writes. Hosts choose the configuration directory. `JsonProfileStore` and `AppProfileService` implement portable profile discovery, startup-profile selection, and activation.

## Runtime and platform services

`IInputFilterRuntime` is the only contract the desktop UI needs to control a native backend. Platform hosts implement it, create their native hooks/event taps, and synchronously invoke the Core debounce filters. Runtime status distinguishes inactive, active, permission-required, unsupported, and faulted states.

Permissions, autostart, foreground access, and game process monitoring remain platform seams. They must report unavailable or consent-required states accurately.

## Diagnostics

Filters emit versioned `FilterDiagnosticEvent` values through `IFilterDiagnosticSink`. `JsonLinesFilterDiagnosticSink` writes those values through a bounded asynchronous queue, so disk I/O cannot block the native input callback. `G915Fix.Heatmap` consumes the resulting JSON Lines stream.
