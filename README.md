# G915 Cross-platform Fix

**A tiny, user-mode keyboard filter that makes a stuttering keyboard feel brand new again.**

Some Logitech G915/G915X units (and other keyboards with the same defect) emit *impossible* HID sequences, phantom key repeats and double-presses that arrive faster than any human could ever type. The result is maddening: `thiss becomess thiis`, a held `Ctrl` that randomly lets go mid-shortcut, a game character that won't keep walking. 

This app sits quietly in your system tray, watches the keyboard at the lowest user-mode level the OS allows, and silently drops those invalid events *before* they ever reach your applications.

- No admin, root, or kernel privileges needed
- No drivers or installers
- No firmware flashes
- No registry or system modifications
- No data collection&mdash;runs fully offline

## Features

### Real-time stutter filtering

A low-level keyboard hook inspects every key event and discards repeats that arrive faster than a configurable threshold (default **28 ms**, below the biomechanical limit of a real double-tap). Filtering is per-key configurable and certain keys (Backspace, Enter, volume) are excluded by default, so legitimate fast input is never touched. 

**CapsLock is always excluded** in every mode, since dropping one of its events could desync the toggle and its indicator light.

### Two filter modes

- **Block double presses** (`BlockRepress`, default), blocks the spurious *re-press*, so one tap produces one character. Ideal for normal typing.
- **Protect held keys** (`BlockRelease`), withholds the spurious *release*, so a held modifier or movement key stays down through a bounce. Ideal for `Ctrl`/`Shift` shortcuts and gaming, at the cost of a few milliseconds of release latency.

Switch between them live from **Tray → Filter mode**; the choice is saved and applied immediately.

### Mouse-button debouncing

A worn or chattering mouse switch turns a single physical click into a phantom double-click. The same idea that fixes the keyboard fixes the mouse: an optional low-level mouse hook drops a button press that arrives within a threshold of that button's previous release, so one click stays one click. It covers the left, right, middle, and both side (X1/X2) buttons, and is **off by default** so keyboard-only users are unaffected.

Turn it on from **Tray → Enable mouse click debounce** (saved to `config.json`), tune the window with `MouseMinRepeatIntervalMs`, and exclude specific buttons with `ExcludedMouseButtons`. The default **50 ms** window sits well below an intentional double-click, so real double-clicks are preserved.

> [!NOTE]
> The debounce is **generic, not mouse-specific.** It works at the OS input layer on any standard pointing device's button events, regardless of make, model, or driver. It is not tied to a particular mouse, and there is nothing to configure per device.

### Profiles (with a ready-made Gaming profile)

Keep more than one configuration side by side. Drop any number of config `.json` files next to the app and switch between them live from **Tray → Profile**. Every file that looks like a config (it shares our setting names) appears as a selectable profile; the base `config.json` is marked **(default)**. Selecting one loads it as the live configuration and applies it instantly, great for keeping, say, a precise everyday-typing setup and an aggressive gaming setup a click apart. Selecting a profile also makes it your **startup profile** (saved to `config.json` as `DefaultProfile`), so the app comes back up on it next time, including at Windows sign-in; pick **(default) config** to clear that and start on the base config again.

A **`gaming.json`** profile ships in the box, tuned for movement:

- **Protect held keys** (`BlockRelease`) mode so a chattering key can't drop your held **W/A/S/D** mid-run.
- Tight **12 ms** per-key release on **W/A/S/D and crouch (right Ctrl)** so stops stay razor-sharp, while tapped action keys keep the full protective threshold.
- Elevated-window pop-ups off; mouse debouncing left off so it can't swallow rapid clicks.

Activate it from **Tray → Profile → gaming**.

> **Reality check:** see [Gaming and anti-cheat](docs/USAGE.md#gaming-and-anti-cheat) for what a
> filter can and cannot do in games, kernel-level anti-cheat and Raw Input can keep keystrokes away
> from any user-mode hook, and no profile changes that.

### Auto-switch profiles for games

Let a game pick your profile for you. Turn on **Tray → Game profile switching → Auto-switch profiles for games** and the app watches for a running game and temporarily activates a matching profile, reverting to your base profile the moment the game closes. World of Warcraft maps to the **WoW** profile out of the box; every other detected game uses `DefaultGameProfile` (`gaming` by default). You can still pick a profile by hand while a game is running, that manual choice holds until the game closes and is not saved as your startup default. The status line in the submenu shows what is in effect.

Which executables count as "a game" comes from **Discord's public detectable-games list**. Choose **Check for game list update** to fetch it: a small, network-isolated companion, `GameListUpdater.exe`, downloads the list and writes `games.txt` next to the app. The updater uses HTTP cache validators when available, so later checks can skip re-downloading unchanged data. The resident tray app itself never makes that call, so with the feature off (the default) nothing new touches the network or watches your processes. Configure the mapping with `AutoSwitchProfilesForGames`, `GameProfileMap`, and `DefaultGameProfile` in `config.json`.

Advanced/manual updater usage:

```text
GameListUpdater [--os win32|linux|darwin|all] [--output PATH] [--cache PATH|--no-cache]
                [--timeout SECONDS] [--retries COUNT] [--api-url URL]
```

The tray integration keeps the historical defaults: `--os win32`, output `games.txt` next to the app,
and a sidecar HTTP cache file next to that output.

### Friendly, forgiving configuration

`ExcludedKeys` and per-key thresholds accept key **names** exactly as they appear in the log, the `VK_` prefix is optional and matching is case-insensitive. Generic modifiers (`Ctrl`, `Shift`, `Alt`) expand to both the left and right keys. Raw numeric virtual-key codes still work as an escape hatch, and unrecognized names are reported in the log rather than failing silently.

### Elevated-window awareness

Windows security (UIPI) prevents a normal-user hook from filtering input to **administrator** windows. The app detects this state, turns the tray icon **yellow** with a plain-language tooltip, shows a brief focus-safe corner notice (toggleable), and records it in the log, recovering automatically when a normal window regains focus.

### Hardware-token support

A security key that "types" a one-time password (a **YubiKey** tap or hold) sends its characters at machine speed, and those codes often contain repeated characters that the filter can mistake for a stutter and drop, breaking authentication. Set `"BurstBypass": true` in `config.json` and the filter recognises a sustained burst of keystrokes far faster than a human can type and steps aside for its duration, so every character (repeats included) passes through. It is **off by default** with no tray toggle, since a normal keyboard never reaches the burst threshold. See [Hardware tokens](docs/USAGE.md#hardware-tokens-yubikey-and-similar) for the one edge case it cannot fully cover.

This setting is only for genuine hardware. Snippet expanders (TextExpander, Espanso), Grammarly's inline corrections, and similar tools don't use a real keyboard at all: they retype text through Windows' synthetic-input API (`SendInput`), which Windows tags distinctly from a physical key press. The filter recognises that tag and always lets such keystrokes through, `BurstBypass` or not, so these tools need no configuration.

### Update checking

On startup the app makes a single best-effort request to the GitHub releases API to see whether a newer version is out. If one is, and notifications are on, a brief toast points you to the release page; the **About** box always reports the status (latest, newer available, could not check, or disabled) and only reads the cached result, so it opens instantly and never waits on the network. No data is sent and nothing is downloaded or installed. This is the only outbound network access the app makes, and `"CheckForUpdates": false` turns it off so the app stays **100% offline**.

### Diagnostic heatmap

`KeyboardHeatmap.exe` turns your filter log into a beautiful, self-contained HTML report so you can see exactly which keys (and which days) are the worst offenders.

## Heatmap

A diagnostic visualization showing which keys generate filtered/duplicate events, rendered with a warm ember intensity ramp (light and dark themes), a "busiest row" flag, summary stat cards, an optional daily-activity chart, and a banner that surfaces any configuration warnings found in the log.

### Photo layout (default)

Filtered key/click counts overlaid directly on photos of a G915X keyboard and G502X Plus mouse.

<img width="880" alt="Keyboard Repeat Filter heatmap report, photo layout" src="docs/Heatmap new.png" />

### Classic layout

The original diagram-style report, still available via **Generate report (classic)** or the `-classic` flag.

<img width="880" alt="Keyboard Repeat Filter heatmap report, 2.0 ember theme" src="docs/heatmap.png" />

| Argument | Default | Description |
|---|---|---|
| `logFile` | `KeyboardRepeatFilter.log` in the current dir (or `LogFilePath` from `config.json`) | Path to the filter log file. |
| `outputFile` | `KeyboardHeatmap.html` next to the log file | Path for the generated HTML report. |
| `-v` / `--v` | off | Include the **Daily filtered event count** section in the output. |

**Examples:**

```bash
# Generate a heatmap from the default log file
KeyboardHeatmap.exe

# Generate a heatmap from a specific log file
KeyboardHeatmap.exe "C:\temp\KeyboardRepeatFilter.log"

# Generate a heatmap including the daily filtered-event chart
KeyboardHeatmap.exe -v
```

The report is a single `.html` file with no external dependencies, open it in any browser. On success, `KeyboardHeatmap.exe` opens it for you automatically.

## Quick Start

### KeyboardRepeatFilter

1. Build the solution in `Release` mode (or download a release).
2. Open the `releases` folder after the build completes.
3. Ensure it contains `KeyboardRepeatFilter.exe`, `KeyboardHeatmap.exe`, `Newtonsoft.Json.dll`, `config.json`, and the bundled `gaming.json` and `WoW.json` profiles.
4. Copy those files to a writable folder of your choice (for example `C:\Utils\KeyboardRepeatFilter`).
5. Run `KeyboardRepeatFilter.exe`.
6. Confirm the tray icon appears and type normally, the stutter should be gone.

Right-click the tray icon to switch **Profile**, switch **Filter mode**, toggle **mouse-button debouncing**, toggle the notice popup, enable **Autostart**, or launch the heatmap.

#### "Unknown publisher" is normal

The first time you run `KeyboardRepeatFilter.exe`, Windows may show one or both of these prompts:

- **User Account Control** ("Do you want to allow this app to make changes?") listing **Publisher: Unknown**, with a yellow banner.
- **SmartScreen** ("Windows protected your PC") with a **Run anyway** option hidden behind **More info**.

This is expected and harmless. The executables are not code-signed, so Windows cannot display a verified publisher name. Code-signing certificates cost money and need renewing every year, which isn't justified for a tiny, open-source, fully offline utility. Nothing about these warnings indicates the app is unsafe.

To run it: on the **UAC** prompt click **Yes**; on the **SmartScreen** prompt click **More info**, then **Run anyway**. If you'd rather verify before trusting it, the complete C# source is in the `src` folder, the only network access is an optional version check you can disable (`"CheckForUpdates": false`), and you can build the executables yourself from source (see [Build Environment](#build-environment)).

#### Antivirus false positives

Some antivirus products (BitDefender has been reported) may flag or quarantine the executables. This is a **false positive**, and it comes from two harmless facts about the app, not from anything malicious:

- The executables are **not code-signed** (see "Unknown publisher" above), so they carry no publisher reputation that a scanner can trust.
- The app is, by design, a **low-level keyboard hook** (`WH_KEYBOARD_LL`). That is the same Windows API a keylogger would use, so heuristic/behavioral engines flag the *technique* even though this app only discards stutter events and never records or transmits keystrokes (see [Is this safe?](FAQ.md) in the FAQ).

The release itself scans clean. On VirusTotal, the v3.0.2 download is reported as [**0 / 92** security vendors flagging it](https://www.virustotal.com/gui/url/12238663a35da5da28a291dbdea3077d420a0a644a08136c470507a846e0fa49/detection):

> No security vendors flagged this URL as malicious.

If your antivirus quarantines it, you can:

1. **Verify it yourself** by uploading the release `.zip` (or the URL) to [VirusTotal](https://www.virustotal.com).
2. **Add an exclusion** for the folder you run it from (for example `C:\Utils\KeyboardRepeatFilter`) in your antivirus settings.
3. **Report the false positive** to your vendor so it gets whitelisted. BitDefender users can submit the sample at the [BitDefender false-positive form](https://www.bitdefender.com/consumer/support/answer/29358/).
4. **Build it yourself** from the `src` folder if you'd rather not run a prebuilt binary at all.

### KeyboardHeatmap

`KeyboardHeatmap.exe` parses `KeyboardRepeatFilter.log` and produces a single self-contained `.html` heatmap, no dependencies required. You can run it directly, or launch it from **Tray → Heatmap → Generate report**. (Logging must be enabled, see below.)

> [!TIP]
> The heatmap is built from the log, so set `"LogLevel": "Trace"` in `config.json` and make sure `LogFilePath` points somewhere writable. If no log exists yet, the tray launcher explains exactly what to do instead of failing silently.

## Configuration at a glance

Everything is controlled by `config.json` next to the executable. Full reference and examples live in [`docs/USAGE.md`](docs/USAGE.md); the short version:

| Setting | Default | Purpose |
|---|---|---|
| `LogLevel` | `Info` | `Trace` logs every filtered key (needed for the heatmap). |
| `LogFilePath` | `C:/Temp/KeyboardRepeatFilter.log` | Where the log is written. |
| `DefaultProfile` | _(none)_ | Profile to load automatically at startup; normally set for you by selecting one in **Tray → Profile**. |
| `HeatmapDays` | `all` | Heatmap window: `all`, or a number of days back from now to chart (older entries skipped). |
| `FilterMode` | `BlockRepress` | `BlockRepress` (stop double presses) or `BlockRelease` (protect held keys). |
| `ShowElevatedWindowNotice` | `true` | Show the brief popup when an admin window is focused. |
| `RunAsAdmin` | `false` | Relaunch elevated on every launch (UAC prompt each time). Toggle from the tray. |
| `MinRepeatIntervalMs` | `28.0` | Repeats faster than this are treated as stutter. |
| `BurstBypass` | `false` | Opt-in: step aside during machine-speed input bursts so hardware tokens (YubiKey) keep repeated characters. Not needed for snippet expanders (TextExpander, Espanso) or Grammarly: those retype via synthetic keystrokes, which the filter always lets through regardless of this setting. |
| `CheckForUpdates` | `true` | One startup version check against GitHub; set `false` to stay fully offline. |
| `ExcludedKeys` | `["Back", "Return"]` | Keys never filtered, by name or number (CapsLock is always excluded). |
| `PerKeyMinRepeatIntervalMs` | `{}` | Per-key threshold overrides, by name or number. |
| `FilterMouseButtons` | `false` | Enable debouncing of chattering mouse buttons. |
| `MouseMinRepeatIntervalMs` | `50.0` | Mouse clicks faster than this are treated as chatter. |
| `ExcludedMouseButtons` | `[]` | Mouse buttons never filtered (`Left`, `Right`, `Middle`, `X1`, `X2`). |

The **Filter mode**, **Enable mouse click debounce**, and **Disable nag popups** tray toggles write straight back to this file, so the GUI and the config file never disagree.

## Documentation

- Usage and configuration: [`docs/USAGE.md`](docs/USAGE.md)
- Frequently asked questions: [`FAQ.md`](FAQ.md)
- Change history: [`CHANGELOG.md`](CHANGELOG.md)
- Troubleshooting: [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md)
- Security policy: [`SECURITY.md`](SECURITY.md)
- Smoke test checklist: [`docs/SMOKE_TESTS.md`](docs/SMOKE_TESTS.md)
- Release process: [`docs/RELEASE.md`](docs/RELEASE.md)
- Config template: [`config.template.json`](config.template.json)
  