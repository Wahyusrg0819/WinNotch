# WinNotch v0.1.1 preview validation

Validated on 3 October 2026, Windows 11 x64, primary display at 125% (120 DPI), .NET SDK 10.0.401.

## Passed

- Release build: zero warnings and errors.
- 30 deterministic state/display checks: arbitration, visibility policies, 100/125/150/200% DPI placement, negative monitor origins, small-display bounds, unknown-DPI fallback, fullscreen policy, and ambiguous Windows busy states.
- 9 native media integration checks using a silent local Windows MediaPlayer session: title/artist propagation, artwork decoding, playing state, pause/resume, previous/next, disable and re-enable.
- 4 native foreground checks against a real test window: normal-window visibility, borderless fullscreen detected through Windows events (106 ms observed), restore delay, and recovery after leaving fullscreen. Windows blocked activation from the background runner, so the fixture was brought to the foreground through Computer Use before checking.
- 13 checks against the published portable executable: passive `WS_EX_NOACTIVATE`, tool-window style, absence from taskbar, native tray registration, no focus theft on peek, expiry, expanded interaction style, Escape routed-key handling, focus restoration, collapse on deactivation, hidden window on suppression, tray-menu opening while hidden, and centered primary-display placement. The tray callback is injected into the application's own HWND for this check; it is not a physical tray click.
- Live browser session metadata and album art were also observed in the expanded UI. Battery and default audio endpoint are readable on this machine.
- Existing appearance/settings remain in place. The native tray reuses the WPF menu and no longer loads Windows Forms.

Total: **56 checks passed** (30 state/display, 9 media, 4 fullscreen, 13 UI/native). Evidence: `artifacts/v0.1.1/smoke-final/smoke-results.json`, its PNGs, the console integration runner, and `artifacts/v0.1.1/final-comparison.json`.

## Performance remains a preview limitation

Two sequential comparison rounds used the old compressed executable and the updated uncompressed/native-tray executable, the same saved preferences, six seconds of warmup, and eight seconds of sampling per process. No forced GC, working-set trimming, or disabled core modules were used. Playback was not controlled during these process samples.

| Metric | v0.1 baseline | v0.1.1 |
| --- | ---: | ---: |
| Idle working set | 303.0–303.6 MiB | 135.4 MiB |
| Private committed bytes | 144.7–145.2 MiB | 62.5 MiB |
| Input-idle startup proxy | 1.35–1.64 s | 0.91–1.08 s |
| Sampled CPU | 0.000–0.012% | 0.000–0.012% |

Working set improved by approximately **55%**, but the <=120 MB target is **not met**. Input-idle timing is a process readiness proxy, not a cold-boot or full-media-readiness measurement. CPU values are limited by the short sampling interval and process-counter resolution. Sustained 60 FPS has not been measured. UI smoke tests allocate screenshots and open settings; their memory/CPU values are not idle benchmarks.

The packaging experiment isolated most of the reduction: removing single-file compression reduced working set from 303.1 to 149.2 MiB with the same source; replacing the Forms tray reduced it further. A ReadyToRun experiment used more memory and was not selected. Raw experiments remain in `artifacts/v0.1.1/`. [Microsoft documents the memory-loading and compression behavior of single-file apps](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

During native testing, an ordinary window with a smaller client area returned `QUNS_BUSY` (2). Treating this alone as fullscreen caused false hiding. The revised policy uses actual client coverage for that case, distinguishes fullscreen on another monitor, and still honors explicit presentation/locked-desktop states. Display placement now reads the window's actual DPI and recomputes after monitor changes.

## Still requires hardware or manual checks

- Physical charger insertion/removal, low/full battery, and battery-saver transitions.
- Physical volume keys and disconnect/reconnect of audio devices (Core Audio callbacks are wired; the test reads the default endpoint).
- Exclusive fullscreen games and explicit presentation mode. A real borderless fullscreen test passed; exclusive game engines still need manual validation.
- Physical 100%, 150%, 200%, and mixed-monitor DPI. Placement arithmetic at all four scales is checked; this machine's physical 125% layout was verified.
- Startup after Windows login, reduced motion/high contrast on the actual desktop, physical keyboard/tray clicks, and tray recovery after a real Explorer restart. Routed Escape and the native tray callback path are checked programmatically.

Notifications, timers, brightness, follow-active-monitor mode, and per-app exclusions remain outside the v0.1 scope as described in the PRD.

## Portable artifact

`dist/WinNotch/WinNotch.exe` is an uncompressed, self-contained Windows x64 executable (~158 MiB). It runs without installing the .NET runtime or requesting administrator access. The larger executable is the deliberate tradeoff for lower memory use and faster measured readiness.

SHA-256: `9E6871E452C7434CEEFFBCFF398568B3FC8A30C788162CF4BCF2B49E9958CD1C`
