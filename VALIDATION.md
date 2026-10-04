# WinNotch v0.2.2 preview validation

Validated on 4 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Live notification follow-up

After the initial release checks below, the user explicitly granted Windows notification access and enabled previews in Settings. The running **portable** executable reported `Listening locally`. `tests/Send-NotificationTest.ps1` sent one silent toast through the installed Windows PowerShell app identity. Computer Use visually confirmed the Alerts panel displayed **Windows PowerShell**, **WinNotch live test**, and the matching test body. A separate `-CheckOnly` query confirmed this tagged test toast remained in Windows notification history after the peek ended. No MSIX installation or certificate trust change was needed.

The test reads only its own tagged toast for the sender-side history assertion and does not log personal notification content. Live denial/revocation, permission persistence after upgrades, and live dismissal-button retention remain unverified; the existing synthetic dismissal test still passes. The script leaves the original test toast to expire after ten minutes and supports a read-only `-CheckOnly` mode. The user's new Ice/floating/true-black settings and Chrome exclusion were preserved.

## Initial release checks (before the user granted notification access)

- Release build and self-contained publish succeeded; **84 deterministic checks passed**. New cases cover critical-only fullscreen, hard suppression, notification baselines, duplicate prevention, stale notifications, identifier reuse, and re-enable behavior.
- **52 published UI/native checks passed**. New checks cover old-setting migration, new-setting serialization, themes/black level, clock, notification preview/detail/dismiss/privacy suppression, brightness support, and module toggles. Existing timer, volume UI, display selection, exclusions, focus and tray checks remain passing.
- **136 checks total**, plus a separate real-process restart check: the old process exited with code 0, exactly one new process started, and the replacement was responsive. The restart was invoked through the tray menu's routed click handler. Active-timer cancellation confirmation still needs a manual click-through check.
- The brightness slider changed the physical laptop display from 100% to 95%, independently read back through WMI, then restored it to 100% in a finally block. Restoration was independently checked again after the test. External monitors/DDC/CI are not implemented.
- Notification tests use **synthetic content only**. The real listener is disposed before the smoke fixture runs; no personal notifications are read and no permission dialog is approved. Live allow/deny/revoke behavior and delivery still need a manual end-to-end test. The module is off by default and never removes original Windows notifications. The latest preview is cleared on disable, pause, exclusion, presentation, or fullscreen hiding.
- The Windows SDK MakeAppx tool successfully validated and built `dist/WinNotch-0.2.2-x64.msix`, with `userNotificationListener`, `globalMediaControl`, and full-trust capabilities. It is **unsigned**, not installed or ready to install until signed/trusted. No certificates, security settings, or Developer Mode settings were changed. Packaged permission and sign-in startup are unverified; see [packaging notes](packaging/README.md).
- Final notification, brightness, clock, and settings captures were visually inspected. Preferences are still the same file content as before testing (SHA-256 `579F6FAEDE35AE826E288BB20D54F27715E62E367CE93E8B5F48AE6BFD340AC5`). Tests do not save settings.
- Evidence: `artifacts/v0.2.2/smoke-final/smoke-results.json`, PNGs in that folder, `restart-results.json`, and `idle-comparison.json`. Restart leaves the final portable build running with the user's saved preferences.

A short sequential idle comparison used the same saved settings, 6 seconds warmup and 10 seconds sampling per executable. Notifications and clock were off; brightness was enabled by default in v0.2.2. v0.2.1 measured 150.3 MiB working set / 0.010% CPU; v0.2.2 measured 133.7 MiB / 0.106% CPU, with 54.0 MiB private bytes and a 916 ms input-idle proxy. These single samples do not establish a memory improvement or cold-start guarantee. **The 120 MB RAM target remains unmet.** The later published build adds test captures and a slower retry only for unsupported brightness devices; the performance sample is of the preceding v0.2.2 build, not the exact final executable. Post-screenshot smoke measurements are not idle benchmarks.

Remaining manual validation: real notification permission/delivery and signed MSIX installation, physical multi-monitor/mixed DPI, exclusive fullscreen games, sleep/resume, startup at sign-in, Windows high contrast/reduced motion changes, physical charger/audio-device changes, and long sessions. Fullscreen critical policy is tested through the state machine and published UI fixture; exclusive games were not exercised this update.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.2**.

SHA-256: `74B81DDC2A65F10B257785F26921C731A01B24131019DD8F6C9E5FAA192C9D4E`

---

# v0.2.1 historical validation

Validated on 4 October 2026, Windows 11 x64, one physical display at 125% DPI.

- Release build and portable publish succeeded without warnings/errors.
- **68 deterministic checks passed**, including nine new display-selection/fallback and exact executable-exclusion checks.
- **35 published UI/native checks passed**, including real monitor enumeration, selected display, disconnected-name fallback, follow-mode target availability, settings serialization, hiding for the current external foreground app, and restoration when that exclusion is removed. Previous timer, volume UI, focus, and tray smoke checks remain passing.
- Total for this update: **103 checks passed**. Evidence: `artifacts/v0.2.1/smoke/smoke-results.json` and its screenshots. User preferences were not saved by tests.
- The settings UI was visually inspected. A physical second monitor was unavailable: actual cross-monitor movement, mixed DPI, and unplug/replug remain unverified. Selection policies were tested with multiple simulated display names and existing negative-origin/DPI placement cases.
- Exclusions match executable basename, case-insensitively, for all instances. Protected processes whose names cannot be read and apps represented by shared ApplicationFrameHost are not supported. Rules do not log window titles or process paths.
- Selected displays use Windows display device names. A missing name falls back to primary; if Windows renumbers a display after a hardware change, reselect it in Settings.
- No new dependencies, benchmarks, native media/audio round trips, or physical fullscreen tests were needed for this bounded update. Earlier measurements below are historical, not new performance claims.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.1**.

SHA-256: `B0995953BA9E4A00C0D90A3A859AB867500F2E0F85505EBE76373FA17BBBDD53`

---

# v0.2.0 historical validation

Validated on 4 October 2026, Windows 11 x64, primary display at 125% (120 DPI), .NET SDK 10.0.401.

## Current release: passed

- Release build/publish: zero warnings and errors.
- **59 deterministic checks**: state/display/fullscreen policies, elapsed-time countdown, pause/resume remainder, late completion, invalid durations, four focus sessions with correct short/long breaks, and timer/media/event priority.
- **28 published UI/native checks**: the previous 13 native checks plus 15 checks for custom timer input, presets, pause/resume, persistent compact display, media access, event restoration, completion while hidden, starting the next break, disabling the module, and audio availability. Buttons use WPF routed events; fullscreen suppression is simulated by the fixture.
- **11 native audio checks**: volume and mute/unmute writes with independent endpoint reads, external callbacks, no duplicate peeks from our own writes, invalid inputs, exact restoration, and rejection after disposal. Output was briefly silenced and the original floating-point volume and mute were restored in a finally block; restoration passed.
- Timer setup, paused/completed states, media panel, and settings were visually inspected at the saved small size. Computer Use also inspected the live portable and switched to Timer. The helper targets Settings and activates it before input, collapsing the overlay; routed checks provide repeatable interaction coverage.
- Existing appearance/startup settings were preserved; smoke checks do not save preferences.

**98 checks passed** (59 core + 28 published UI/native + 11 audio). Evidence: `artifacts/v0.2.0/smoke-final/smoke-results.json`, its PNGs, and the console check runners. Reproduce with `build.ps1 -Check`, the published `--smoke-test` option, and `dotnet run --project tests/WinNotch.IntegrationChecks -c Release -- --controls-only`. The audio check requires an output and briefly silences it.

The native media and real borderless-fullscreen checks recorded below passed in v0.1.1; they were not rerun for v0.2. The state/display policies and published UI/native checks were rerun.

## Timer and volume behavior

- Custom timers accept 1–180 whole minutes. Focus is 25 minutes, short break 5, long break 15; every fourth completed focus offers a long break. Each next session starts explicitly. Done/Cancel resets the cycle.
- Countdown stays compact ahead of media and returns after battery/volume events. Completion priority is 90, below critical battery at 100. Completion is visual and silent and remains until acknowledged.
- Fullscreen hiding and Pause WinNotch hide the timer without pausing it. Completion during hiding returns afterward. The timer's own Pause button stops the countdown.
- Timer state stays in memory. Exit or disabling the module cancels it. Restart recovery and waking the computer are not implemented.
- Elapsed time uses `Environment.TickCount64`, which includes sleep on Windows/.NET 10 and is independent of wall-clock edits. Large elapsed jumps are tested; physical sleep/resume is not. Recheck the clock when upgrading .NET because later runtime versions differ. [Microsoft documentation](https://learn.microsoft.com/en-us/dotnet/api/system.environment.tickcount64?view=net-10.0).
- Volume controls the default multimedia output, preserves mute when moving the slider, and disables when no output is available. [Core Audio API](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar).

## Current performance

Two sequential v0.1.1/v0.2 comparisons used the same preferences: warmup/sample intervals of 6/8 seconds, then 8/10 seconds. No forced GC, working-set trimming, or disabled core modules. No timer was running; playback was not controlled.

| Metric | v0.1.1 | v0.2.0 |
| --- | ---: | ---: |
| Idle working set | 130.9–131.0 MiB | 113.9–132.7 MiB |
| Private bytes | 61.6 MiB | 47.2–62.2 MiB |
| Input-idle startup proxy | 1.30–1.45 s | 1.03–1.13 s |
| Sampled CPU | 0.000–0.019% | 0.000% |

Evidence: `artifacts/v0.2.0/idle-comparison.json` and `idle-comparison-repeat.json`. The lower memory sample did not repeat: **the <=120 MB target is not consistently met**. No new RAM improvement is claimed. CPU values have limited resolution; input-idle is not cold boot or media readiness. Smoke memory includes screenshots and is not an idle benchmark.

Still unverified: physical sleep/hibernate/resume; hours of mixed media/timer use; active-timer resource measurements; audio-device swaps during interaction; keyboard-only slider use; exclusive fullscreen games; physical mixed-DPI screens; login startup; desktop high contrast/reduced motion changes; physical charger transitions; Explorer restart; sustained 60 FPS.

Monitor selection/follow-active-monitor, per-app exclusions, notifications, and brightness remain planned.

## Current portable artifact

`dist/WinNotch/WinNotch.exe`: version **0.2.0**, self-contained Windows x64, 165,270,494 bytes (~158 MiB), no separate runtime installation or administrator access required.

SHA-256: `965373548F9F534E9520401C818D64F7FCCBD26FE19291A337F05F5A92E31C1D`

---

# v0.1.1 historical validation

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
