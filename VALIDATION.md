# WinNotch v0.1 preview validation

Validated on 3 October 2026, Windows 11 x64, primary display at 125% (120 DPI), .NET SDK 10.0.401.

## Passed

- Release build: zero warnings and errors.
- 15 deterministic state/arbitration checks: priority, coalescing, expiry, restoration, fullscreen suppression, expanded interaction, smart hide, active-only mode, and pause/resume.
- 9 native media integration checks using a silent local Windows MediaPlayer session: title/artist propagation, artwork decoding, playing state, pause/resume, previous/next, disable and re-enable.
- 10 checks against the published portable executable: passive `WS_EX_NOACTIVATE`, tool-window style, absence from taskbar, no focus theft on peek, expiry, expanded interaction style, focus restoration, collapse on deactivation, hidden window on suppression, and centered primary-display placement.
- Live browser session metadata and album art were also observed in the expanded UI. Battery and default audio endpoint are readable on this machine.
- Settings UI was inspected using Windows Computer Use. Native title bar and application icon are included in the published build.

Evidence: `artifacts/smoke-final/smoke-results.json`, `artifacts/smoke-final/*.png`, the console integration runner, and `artifacts/idle-performance.json`.

## Performance remains a preview limitation

A separate 8-second sample of the portable app after startup reported **0.41% CPU**, **298.5 MiB working set**, and **138.2 MiB private committed memory**. The <=120 MB RAM target is not met. The brief sample is not a long-running performance guarantee. The smoke-test process allocates screenshots and opens settings; its post-interaction memory/CPU numbers are not an idle benchmark.

The small overlay uses software rendering, which reduced graphics allocation in the initial comparison. More memory work is needed before calling this a lightweight release. Startup under two seconds and sustained 60 FPS have not been benchmarked.

## Still requires hardware or manual checks

- Physical charger insertion/removal, low/full battery, and battery-saver transitions.
- Physical volume keys and disconnect/reconnect of audio devices (Core Audio callbacks are wired; the test reads the default endpoint).
- Exclusive fullscreen games and presentation mode. Suppression behavior itself is automated; real game detection still needs manual validation.
- 100%, 150%, 200%, and mixed-monitor DPI. This machine's 125% layout was verified.
- Startup after Windows login, reduced motion and high contrast on the actual desktop, and keyboard Escape input (the collapse/focus path is checked programmatically).

Notifications, timers, brightness, follow-active-monitor mode, and per-app exclusions remain outside the v0.1 scope as described in the PRD.

## Portable artifact

`dist/WinNotch/WinNotch.exe` is a compressed, self-contained Windows x64 executable (~78 MiB). It runs without installing the .NET runtime or requesting administrator access.

SHA-256: `D1B4B108C6FDA8363C421D6E8446DD50492C50BE5AA6D926E750EC4F80B5D612`
