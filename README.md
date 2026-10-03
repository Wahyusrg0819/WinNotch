# WinNotch

A native Windows notch for music and small system moments. Built with C#, .NET 10, WPF, WinRT media/power APIs, and Win32/Core Audio. No browser engine, server, account, analytics, or runtime network dependency.

## Run

Run `dist/WinNotch/WinNotch.exe`. The portable build includes the .NET runtime; administrator access is not required. Keep it in a permanent folder before enabling startup.

- Click the notch to open the player; click outside or press Escape to collapse.
- Right-click the notch or its tray icon for settings, pause/resume, and exit.
- Double-click the tray icon, or launch the executable again, to open settings.
- `WinNotch.exe --settings` opens settings on launch.
- Smart hide hides the idle notch over maximized title bars. Active media and brief system events still appear. Choose **Always visible** to keep the idle notch present.
- Startup is opt-in through settings; it creates a per-user Windows Run entry pointing at the current executable. Turn it off before moving/deleting that executable.

## v0.1 preview scope

- Attached black notch with flat upper corners, hover feedback, resize/fade animation, optional floating style, three sizes.
- Explicit Hidden / Idle / Peek / Compact / Expanded state manager with expiring priority events. Charger events restore the previous media state without an idle flash.
- System media session, title, artist, artwork, play/pause, previous/next, and read-only timeline. Controls follow the active player's supported capabilities.
- Battery/charger/low-battery/full/saver events and volume/mute callbacks, including default audio device changes.
- Primary-monitor positioning, per-monitor DPI awareness, fullscreen/presentation hide, delayed restore, and smart hide for maximized apps.
- Passive overlay uses `WS_EX_NOACTIVATE`; the expanded panel becomes keyboard-interactive. Tool window is excluded from the taskbar and Alt+Tab.
- Tray, local settings, startup option, reduced-motion preference, and high-contrast colors.

Notifications, timers, brightness, per-app exclusions, and follow-active-monitor mode are v0.2 work. A paused media session stays compact while Windows continues to expose it. The Windows volume flyout is not suppressed.

## Develop

Requires Windows and .NET 10 SDK. A project-local SDK, when present in `.tools/dotnet`, takes precedence over the system SDK.

```powershell
.\build.ps1                  # Release build
.\build.ps1 -Check           # State/arbitration checks + build
.\build.ps1 -Check -Publish  # Portable, self-contained x64 executable
.\dist\WinNotch\WinNotch.exe --smoke-test D:\WinNotch\artifacts\smoke
# Native media integration tests (in a normal Windows desktop session):
.\.tools\dotnet\dotnet.exe run --project tests\WinNotch.IntegrationChecks -c Release
```

The smoke-test option runs a short UI/native check, saves local images and a JSON report, then exits. Its charging popup is a labeled test fixture in the test code; normal operation only displays live device events. No settings are saved by the smoke test. Close another WinNotch instance before running it.

The integration runner creates a temporary silent local media session and verifies metadata, artwork, playback state, play/pause, next, and previous. It refuses to issue controls if another session becomes current. Temporary audio and artwork stay under `artifacts/integration`.

## Structure

`Core/NotchStateManager.cs` owns presentation and priority. `App.xaml.cs` wires modules to the state manager. `Modules/` uses Windows events; media timeline refresh runs only while an actively playing expanded panel is visible. `ForegroundService` listens for foreground/bounds changes and checks presentation mode on a two-second watchdog. `OverlayWindow` owns window style, layout, and animation. `SettingsWindow` edits the small JSON settings file.

Settings live in `%LOCALAPPDATA%\WinNotch\settings.json`. Diagnostics log only timestamp, module, exception type, and HRESULT; media titles, artist names, artwork, and message text are not logged. Logs rotate at 128 KB.

Windows App SDK is not a dependency in this milestone: the APIs used here are available directly through the Windows target framework and Win32. Introduce it only for a feature that needs it.

## Validation boundaries

The automated checks cover priority, coalescing, expiry, suppression, pause/resume, and visibility policies. The UI smoke test checks native window flags, focus behavior for passive events, state transitions, primary display placement, module availability, and a brief resource-use sample. A short sample is not a long-running performance guarantee.

Physical charger changes, every external media player's control support, exclusive fullscreen games, login startup, and physical mixed-DPI monitors require hardware/manual validation. The executable does not request notification access in this release.

See [VALIDATION.md](VALIDATION.md) for verified results and remaining limitations. The current portable build is a functional preview; its measured working set is above the PRD's <=120 MB target.

## API references

- [Call WinRT from desktop .NET](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [System media sessions](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [Core Audio endpoint notifications](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolumecallback)
- [Fullscreen/presentation notification state](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state)
