# WinNotch v0.2.9 preview validation

Validated on 8 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

**Memory profiling follow-up:** the interaction benchmark retained its closed Settings window across later samples. That reference is now released; corrected short samples measured **158–161 MiB** after repeated use. This corrects the measurement tool, not normal application behavior. A resident-page and heap investigation is recorded in [MEMORY-PROFILE.md](docs/MEMORY-PROFILE.md). The portable below is unchanged, and its original measurements remain historical observations.

**Stability follow-up:** a 15-minute, 178-sample check reclaimed all ten closed Settings windows, recorded three natural Gen2 collections, and confirmed Bluetooth polling stopped. Working set averaged **167.7 MiB** and peaked at **173.0 MiB**. Private memory ended higher than the early idle window, so this is not a claim of flat usage or leak-free long sessions. See [the full stability report](docs/STABILITY.md).

## Deferred module panels

- The expanded shell still loads only when needed. Its seven module panels now use WPF's deferred compiled resources: the selected panel is created on first use and reused afterward. Unvisited panels are not instantiated or updated. Returning to Timer retains the entered duration; selecting the current tab avoids repeated setup. Volume controls and the tab bar remain available immediately. This uses existing WPF resources, with no new application dependency.
- First expansion no longer repeats every module update after applying settings. A newly selected panel reads current service state, and keyboard focus is directed to an available control in that panel. Timer completion can still bring the timer panel forward. Module polling and permission behavior are unchanged; Bluetooth stops polling when its panel closes.
- **233 checks passed:** 105 core plus 128 required published UI/native checks. Coverage includes deferred startup, suppressed expansion, only the first module loading, loading the second module on selection, retained timer input, keyboard focus, passive events, module enable/disable, all previous notification/download/Bluetooth checks, and unchanged-brightness suppression. A real brightness write was independently read back and the original value restored successfully.
- Build and publish passed with no warnings or errors. Timer, media, brightness, Bluetooth, and Settings captures were visually inspected; the footer reads 0.2.9. Smoke tests use isolated agenda/download fixtures, simulated Bluetooth devices, and no saved preferences. No physical Bluetooth toggle or Windows permission change was performed.

Evidence: `artifacts/v0.2.9/build-check-release.txt`, `publish-release.txt`, and `smoke-release/smoke-results.json` (exit code 0, all 128 required booleans true). The final synchronous smoke expansion was **65.8 ms**; it excludes animation completion and is not the first-use layout benchmark below.

## Performance checkpoint

The comparison uses v0.2.8 plus the same opt-in `--performance-test` harness as v0.2.9. All modules and the idle clock were enabled; no media session was active. The harness uses an isolated empty agenda, preserves saved preferences, warms up for eight seconds, and takes ten samples 500 ms apart per phase. It measures the first expansion including `UpdateLayout`, visits all seven panels six times, opens/closes Settings without saving, then revisits the panels twelve more times. Build, packaging, and profiling do not run during these samples. No screenshots, hardware writes, forced collections, or working-set trimming are part of this benchmark.

| Sample | Initial idle WS | First panel WS | After repeated use WS | First expansion + layout |
| --- | ---: | ---: | ---: | ---: |
| v0.2.8 baseline 1 | 125.3 MiB | 143.8 MiB | 161.2 MiB | 187.7 ms |
| v0.2.8 baseline 2 | 124.9 MiB | 139.9 MiB | 161.5 MiB | 355.0 ms |
| v0.2.9 release 1 | 126.6 MiB | 138.9 MiB | 163.1 MiB | 117.1 ms |
| v0.2.9 release 2 | 126.4 MiB | 139.3 MiB | 163.3 MiB | 124.4 ms |

Working sets above are phase means. The final runs used 72.5–72.7 MiB private bytes after repeated use, with normalized CPU of 0.116–0.211%. Median already-loaded panel switches were 3.8–4.7 ms; Bluetooth polling stopped after collapse in both runs. First expansion was faster in these observations, but **RAM after visiting all modules did not improve** and was slightly higher. Source reports and the comparison are in `artifacts/v0.2.9/{baseline-run1,baseline-run2,release-run1,release-run2}/performance.json` and `comparison-release.json`.

These are short local observations, not cold-boot, animation-frame-rate, or long-session guarantees. Earlier traces included WPF UI Automation callbacks without repeated layout/state changes; background automation can affect allocation and CPU samples. No accessibility behavior was disabled. Intermediate candidates, WMI-reader experiments, and profiled runs are excluded from the final comparison.

The **≤120 MB memory target remains unmet**, and the **<100 ms first-use response target remains open**. Loading fewer panels helps first use; visiting every module still creates all seven panels. Sustained 60 FPS, physical sleep/resume, longer sessions, and the hardware/accessibility/distribution gaps from previous previews remain unverified. The next priority is to finish this release checkpoint before specifying custom modules and plugin API behavior.

## Artifact and user data

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.9**, 165,802,302 bytes. SHA-256: `277AB41F620E437C12F0ADD69DB4A66E1566075D9B596D7C73DF0F947B0E14DD`.

MakeAppx validated `dist/WinNotch-0.2.9-x64.msix`; it remains unsigned and uninstalled. Evidence: `artifacts/v0.2.9/package-release.txt` and `artifact-release.json`. Settings and the real agenda remained byte-identical after testing; hash evidence is in `user-data-before.json` and `user-data-after.json` in the same artifact folder.

The canonical portable was left running normally and responsive after validation (`running-release.json`). The final source whitespace check passed.

---

# v0.2.8 historical validation

Validated on 7 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Bluetooth devices and radio control

- The Bluetooth panel lists paired Classic/LE devices, combines entries with the same Windows device container, and shows reported connection status. Battery information is optional: only a valid 0–100 value from `System.Devices.BatteryLife` is shown for connected devices. Missing metadata is unavailable, not zero. This documented byte property is defined in the installed Windows SDK `10.0.26100.0/um/propkey.h`. No active discovery, GATT connection, pairing, or extra dependency was added.
- Reads start when the panel opens and repeat every 15 seconds while visible; Refresh reads immediately. Switching panels, collapsing, hiding, or disabling stops polling. Startup does not enumerate Bluetooth or request permission. The list is capped at 32 devices, asynchronous reads have cancellation/timeouts, and disable/dispose ignores stale results. Device names and identifiers are never logged or persisted.
- Turn on/off requests access only from the button action and changes Bluetooth radios that are on/off; it rechecks the radio kind before each native write. Wi-Fi, airplane mode, and Windows policy are not changed. Accepted writes are read back before reporting success; unconfirmed changes ask for a refresh. Windows denial, hardware blocks, and missing adapters have disabled controls or explanatory status, with a Windows Bluetooth settings shortcut. Turning off disconnects Bluetooth accessories. Disabling the module leaves the radio unchanged.
- The implementation follows Microsoft's [RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.requestaccessasync?view=winrt-26100) and [SetStateAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.setstateasync?view=winrt-26100) contracts. The package declares `bluetooth` and `radios` device capabilities.
- **227 checks passed:** 105 core plus 122 required UI/native checks, including 23 Bluetooth checks. Simulations cover lazy reads, no automatic access request, device deduplication, missing/zero/invalid battery values, keyboard focus, both toggle directions, permission caching and retry, denied writes, accepted-but-unconfirmed writes, polling lifetime, module disable, settings serialization, absent/blocked radios, stale reads, double-click protection, and disposal during access requests. All seven module tabs fit beside Settings. Bluetooth normal/denied/blocked screenshots and Settings were visually inspected.
- A separate read-only check against the actual Windows API succeeded: one Bluetooth radio was **Off**, six paired devices, none connected, and no battery level available. No physical radio write, access prompt, or OS permission change was performed. Real on/off control, Windows consent/denial, live connected-device battery reporting, radio transitions through sleep, and multiple adapters still need hardware validation. The synthetic results do not establish those physical behaviors.
- Build/publish and unsigned MSIX validation passed. The only change after the successful published UI run was the displayed version label from 0.2.7 to 0.2.8; the app was rebuilt, repackaged, and the native read-only probe rerun. Existing settings and the real agenda were preserved; smoke tests used their isolated fixtures.

Evidence: `artifacts/v0.2.8/build-check.txt`, `publish.txt`, `package.txt`, `native/bluetooth-read.json`, and `smoke-final/smoke-results.json` plus `bluetooth-*.png`. The UI run exited with code 0; all 122 required booleans passed. First synchronous expansion measured **100.6 ms**, before animation completion, so the <100 ms target remains open.

## Resource checkpoint and artifact

One sequential old/new pair used unchanged preferences and agenda, eight seconds warmup and about 20 seconds sampling per process. Build, packaging, and UI interaction were finished before sampling. Bluetooth was enabled by default but its panel remained closed, so it did not poll. No forced GC or working-set trimming was used.

| Portable | Mean / peak working set | Private bytes | Normalized CPU | Input-idle proxy |
| --- | ---: | ---: | ---: | ---: |
| v0.2.7 | 129.2 / 129.6 MiB | 48.7 MiB | 0.077% | 1,459 ms |
| v0.2.8 | 125.4 / 125.7 MiB | 48.6 MiB | 0.111% | 1,182 ms |

The **≤120 MB target remains unmet**. These short observations do not prove a memory improvement or long-session stability. Evidence: `artifacts/v0.2.8/idle-comparison.json`. The new canonical portable was left running. Settings and the real agenda were byte-identical before/after; hashes are in `user-data-before.json` and `user-data-after.json`.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.8**, 165,790,014 bytes. SHA-256: `096DB262A4CF81E3EA97A857F5DD65871E8916DCDBE2897E6409871519E5B4D3`. The unsigned, uninstalled package is `dist/WinNotch-0.2.8-x64.msix`.

Next priority: **performance and release checkpoint**, then requirements for custom modules and plugin API. Remaining hardware, accessibility, and distribution checks from earlier previews still apply.

---

# v0.2.7 historical validation

Validated on 7 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Downloads from a local folder

- The user selected local folder observation without a browser extension. Downloads follows the Windows known Downloads folder or a folder chosen in Settings. Files shows `.crdownload`/`.part` metadata and the five most recent observed final renames. Open folder opens Explorer; the module never reads file contents, changes files, or launches a downloaded file.
- A completion alert requires an observed rename from a supported temporary extension to a final name plus readable metadata for the final file. A missing temporary file alone is not completion. Existing final files, ordinary new files, cancellation/deletion, cross-folder moves, unsupported extensions, and activity while the app was closed are not inferred as downloads. Sizes are observed file lengths, not percentage, transferred bytes, rate, or ETA. A temporary file may be paused or interrupted.
- The native folder watcher coalesces event bursts and reads metadata off the UI thread. It refreshes every two seconds while temporary files exist, with no scan timer in a quiet folder. Disable/folder switch disposes the old watcher and ignores stale asynchronous results. An unavailable folder clears stale results and retries every 30 seconds or on Retry. Folder-event loss discards uncertain queued completions and rescans; the list is bounded to 32 temporary files, five recent completions, and 64 queued final paths.
- These safeguards follow the documented duplicate-event and buffer-overflow behavior of [Microsoft FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0). The default location comes from [SHGetKnownFolderPath](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderpath), not an assumed `C:\Users\...\Downloads` path. No dependency was added.
- **204 checks passed:** 105 core and 99 required published UI/native checks, including 21 new Downloads/layout checks. Real filesystem fixtures cover baseline/no replay, growing size, both temporary extensions, rename completion, passive focus, no duplicate emission, idle polling stop, cancellation, ordinary-file filtering, suppression without replay, disable, unavailable-folder retry, folder switching, the bounded list, folder removal/recovery, preference serialization, and disposal during pending initialization. All six module tabs fit without colliding with Settings, and the Downloads action receives keyboard focus.
- The tests used isolated synthetic files under `artifacts/v0.2.7/smoke-final`; the user's actual Downloads folder and agenda were not used as fixtures. Final partial-file, ready-alert, and Files panel images were inspected. Real browser downloads, native buffer overflow under extreme load, network/redirected-folder disconnection, and physical sleep during a download remain validation gaps. The folder observation contract does not promise universal browser coverage or file integrity.
- Build and publish succeeded. MakeAppx validated the unsigned, uninstalled `dist/WinNotch-0.2.7-x64.msix`. The portable is 165,749,054 bytes, version **0.2.7**.

SHA-256: `3DB53BC43C3B14024EB35AE88E04D408929D1CDB88D36012821AE1EBEDB05F2E`

Evidence: `artifacts/v0.2.7/build-check.txt`, `publish.txt`, `package.txt`, `smoke-final/smoke-results.json`, and the `downloads-*.png` captures. The final smoke process exited with code 0; all 99 required booleans are true. First synchronous expansion was **111.9 ms**, before animation completion, so the <100 ms target remains open.

## Resource checkpoint

One sequential old/new pair used identical saved settings and agenda, eight seconds warmup, and about 20 seconds sampling per process. No Settings panel, build, packaging, or profiler ran during sampling. The new version also enabled its default Downloads watcher. The comparison is a short local observation, not a proven memory improvement or long-session guarantee.

| Portable | Mean / peak working set | Private bytes | Normalized CPU | Input-idle proxy |
| --- | ---: | ---: | ---: | ---: |
| v0.2.6 | 133.2 / 133.6 MiB | 47.9 MiB | 0.087% | 1,641 ms |
| v0.2.7 | 128.1 / 128.6 MiB | 48.3 MiB | 0.082% | 1,274 ms |

The **≤120 MB target remains unmet**. The new canonical executable was left running after the comparison. Settings stayed byte-identical (SHA-256 `1C94C77F924EC5E15B916CE2906C4E528847201A34182DEE8A6DB2E63921B640`), as did the real agenda (`0C913E06A65FCAE97F22081FF8052B307DAA054472614FA128CC278888321378`). Evidence: `artifacts/v0.2.7/idle-comparison.json`.

Next implementation: **Bluetooth**, followed by the performance/release checkpoint, custom modules, and plugin API. The remaining hardware and notification checks from previous versions still apply.

---

# v0.2.6 historical validation

Validated on 7 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Local calendar

- The user selected an offline agenda without an account. Calendar supports one-time event creation, editing, deletion, a next-event panel, and optional silent reminders five minutes before the event. It is enabled by default; disabling it preserves saved events.
- Agenda data lives in `%LOCALAPPDATA%\WinNotch\agenda.json`. Writes replace the previous file after writing the replacement. Failed saves leave in-memory state unchanged; malformed files are preserved with editing disabled. No agenda content is logged.
- Reminder status is saved before publishing the transient event, preventing duplicate delivery after polling or restart. Rescheduling rearms the reminder; changing only the title does not. A late launch or resume catches up only while the event is still upcoming and within five minutes. Past events are retained without replay. Suppressed reminders are consumed without later replay, using the existing pause/fullscreen/exclusion rules.
- The 15-second calendar timer stops when disabled or no future events remain. The editor is created only when opened. Existing media/timer controls retain their deferred construction. No dependency or external service was added.
- **183 checks passed:** 105 core checks and 78 required published UI/native checks. The new cases cover durable add/edit/delete, time ordering across offsets, invalid input, reminder boundaries, restart deduplication, rescheduling, opt-out, simultaneous reminders, elapsed-time catch-up, failed saves, malformed files, editor input, passive reminder focus, agenda display, keyboard focus, suppression, disable, and settings compatibility. Existing notification permission simulations also passed; real Windows toast delivery was not repeated for this update.
- `agenda.png`, `calendar-panel.png`, and `calendar-reminder.png` were visually inspected. The UI smoke test uses a separate synthetic agenda under its output folder and does not save user preferences or change the real agenda. Physical sleep with an active Calendar reminder, daylight-saving transitions on another system, and the Delete confirmation interaction remain manual checks; persistence/deletion logic is covered by the core runner.
- Build and publish completed without warnings or errors. `dist/WinNotch-0.2.6-x64.msix` passed MakeAppx validation; it is unsigned and uninstalled. The portable executable is running from the canonical path.

Evidence: `artifacts/v0.2.6/build-check.txt`, `publish.txt`, `package.txt`, and `smoke/smoke-results.json` (all 78 required booleans true).

The first synchronous panel expansion measured **142.0 ms** in this run, before animation completion. The <100 ms response target remains open. Performance samples below are short observations, not long-session guarantees.

The existing user-started process (PID 19128, canonical portable, started at 14:20 WIB) was sampled for **30.49 seconds at 18:36 WIB**, after roughly 4 hours 16 minutes of uptime and agenda interaction. It remained responsive throughout the sample: **155.9 MiB** mean working set (155.7–157.2 MiB), **73.7 MiB** private bytes, and **0.125%** normalized CPU. One future event was saved; titles and event times were not copied into the report. No build, packaging, or UI interaction ran during the sample. This is an existing-session observation, not a fresh-launch benchmark, controlled old/new comparison, or continuous four-hour monitor. The **≤120 MB RAM target remains unmet**.

Evidence: `artifacts/v0.2.6/idle-existing-session.json`. The running session and its real agenda were left intact. User settings remained byte-identical to the pre-update file (SHA-256 `1C94C77F924EC5E15B916CE2906C4E528847201A34182DEE8A6DB2E63921B640`).

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.6**, 165,716,286 bytes.

SHA-256: `41349B9739DF72D9F975FA183EB02603CD3F659262C3144DF1566F7F963BF73D`

At this checkpoint the next implementation was **Downloads**, now delivered in v0.2.7 above. Deep performance work remains a release checkpoint; crashes, leaks, or substantial regressions stay immediate fixes. Calendar recurrence, ICS import, and account sync are outside the selected local-agenda scope.

---

# v0.2.5 historical validation

Validated on 5 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Follow-up: 7 October 2026

- On 5 October, after the user manually revoked Windows access, the still-running v0.2.5 Settings window reported **Access denied**. A controlled toast was confirmed in Windows history while that status remained denied. Live clearing of an already-visible preview at the exact revocation instant was not asserted.
- On 7 October, the existing process reported **Listening locally** again. Access had already been restored outside this run; no permission UI was operated by automation. The process was a later launch, so this establishes recovery after relaunch, not same-process regrant behavior or denial of the first permission dialog.
- **75 checks passed again on the unchanged published executable:** ten real delivery/detail/dismiss/original-retention/no-replay checks across Windows PowerShell and Windows PowerShell ISE, plus 65 UI/native checks including the nine simulated permission/lifecycle cases. The live test exited with code 0 and its controlled preview/dismissed captures were inspected. The 84 core checks were subsequently rerun in the lifecycle pass below.
- Actual tray restart also passed: the smoke parent exited with code 0 and exactly one responsive replacement started. Launching the executable a second time opened Settings on that same process and the second process exited with code 0. The replacement reported **Listening locally** without a permission prompt.
- The existing process had been open for **17.33 hours**. After closing Settings, a 30.55-second sample remained responsive throughout, with **153.2 MiB** working set (min/max both 153.2), **72.8 MiB** mean private bytes, **0.150%** normalized CPU, and 768–773 handles. This is a short observation at the end of an existing session, not continuous monitoring of those 17 hours or proof that no leak exists. RAM remains above the PRD target.
- The sign-in Run entry points to the existing canonical executable with `--background`. The subsequent lifecycle pass below adds OS sleep records and user confirmation of automatic startup. Physical charger/audio transitions remain to be tested. No reboot, sign-out, or suspend was triggered by the tests.
- Settings stayed byte-identical with the hash listed below. The executable hash/version is unchanged; there was no rebuild. The canonical app is left running after the restart check. No new diagnostic log entry was observed; the latest entry was from 3 October, which does not prove a crash-free session.

Evidence: `artifacts/v0.2.5/follow-up-2026-10-07/existing-session.json`, `live/notification-live-results.json`, the controlled PNGs in `live`, `smoke/smoke-results.json`, `restart-results.json`, `single-instance.json`, and `permission-follow-up.json`.

## Sleep/resume and sign-in: 7 October 2026

- The previously observed process (PID 13404) started on **6 October at 19:47:34 WIB**, before the recorded power transitions, and was still responsive during the 7 October 13:06–13:07 sample. Windows Kernel-Power records show four Modern Standby entry/exit pairs, plus Sleep at **00:02:58** and resume at **00:03:22** on 7 October. This verifies that the same app process survived real power transitions on this laptop; it does not measure immediate recovery latency or continuously monitor the session.
- The user explicitly confirmed that WinNotch appeared automatically after signing into the last Windows session. The recorded process began **56.6 seconds after boot**, and the current Run command matches `"D:\WinNotch\dist\WinNotch\WinNotch.exe" --background`. Taken together, these establish successful automatic startup in that observed session, not a guarantee for every login or another machine.
- **All 84 core checks passed again.** Existing countdown cases cover a delayed callback, elapsed-time catch-up, completion exactly once, and a paused timer retaining its remaining time. No redundant test framework or app lifecycle code was added.
- The countdown's existing `Environment.TickCount64` clock includes sleep on the deployed **.NET 10 Windows** runtime, consistent with [Microsoft's version-specific documentation](https://learn.microsoft.com/en-us/dotnet/api/system.environment.tickcount64?view=net-10.0). The current clock therefore needed no change. This does not replace a physical sleep test with an active countdown.
- **The basic lifecycle checkpoint passes for this observed laptop session.** Active timer/media behavior through physical sleep, device reconnection, wake-up latency, and other Windows/hardware combinations remain release-validation work. No new source changes or portable build were needed; the settings and executable hashes remain unchanged and v0.2.5 stays running.

Evidence: `artifacts/v0.2.5/lifecycle-2026-10-07/session-power-evidence.json` contains the bounded Windows power-event records and startup evidence; `core-checks.txt` contains the 84-check result. The responsive process sample remains in `follow-up-2026-10-07/existing-session.json`.

At this checkpoint the next implementation was **Calendar**, now delivered in v0.2.6 above. Broader Windows/app notification compatibility, first-prompt denial, same-process permission recovery, and extended hardware lifecycle checks remain release-validation gaps.

## Notification fixes and checks

- Denied access now retains its specific Windows privacy-settings guidance. Previously, an enabled listener replaced the denial message with generic permission instructions.
- A failed read from an older enabled state no longer overwrites the current disabled state or emits a stale clear event. Disposed listeners ignore pending failures and permission requests. Access is checked again after an asynchronous read, before interpreting its result.
- **159 checks passed in this update:** 84 core, 65 published UI/native checks (including nine simulated notification permission/lifecycle cases), and ten real notification checks. Media integration, brightness hardware writes, and restart were not repeated in this update.
- Simulated permission checks run through the real `NotificationService` with only its three Windows calls replaced by delegates. They cover denied/unspecified access without automatic prompts, explicit permission retry, revocation before/during a read, disable during a failing read, re-enable while a read is pending, and disposal. They do **not** establish that the Windows privacy UI was exercised.
- The published executable received two silent controlled toasts using the existing **Windows PowerShell** and **Windows PowerShell ISE** identities. For each, the live check verified delivery/app identity, displayed detail, the actual WPF dismiss-button handler, continued presence of the tagged original in Windows history, and no replay on the next poll. Preview and dismissed-panel captures were visually inspected. These are two controlled sender identities, not validation of arbitrary third-party applications or every toast layout.
- The live test never requests permission or removes Windows notifications. It captures only its own uniquely titled test previews; the sender checks only its tagged test toast. Test originals expire after ten minutes.
- The final v0.2.5 Settings window reported **Listening locally** after relaunch without another permission prompt. The later manual revocation observation and recovery check are recorded in the 7 October follow-up above; remaining permission-test limits are explicit there.
- Build/publish completed successfully. MakeAppx validated `dist/WinNotch-0.2.5-x64.msix`; it remains unsigned and uninstalled.
- Latest user settings stayed byte-identical throughout this update: SHA-256 `1C94C77F924EC5E15B916CE2906C4E528847201A34182DEE8A6DB2E63921B640`. The idle clock is now enabled by the user's saved preference, unlike the prior v0.2.4 benchmark.

## Resource check and remaining limits

A fresh final executable, 8 seconds warmup and 20.25 seconds sampling, measured **125.0 MiB** mean working set (125.6 MiB sampled peak), **48.4 MiB** private bytes, and **0.135%** normalized CPU. Input-idle startup proxy was **1,508 ms**. All saved modules were enabled, including the clock; no Settings/panel, build, packaging, or profiler ran during sampling. This is one short local sample and is not a like-for-like regression comparison with the earlier clock-off sample.

The **RAM target <=120 MB remains unmet**. First synchronous panel expansion in the final smoke fixture measured **116.1 ms**, before animation completed; the <100 ms response target remains unestablished. Long-session memory, sustained animation FPS, cold boot/sign-in, sleep/resume, physical mixed-DPI monitors, exclusive games, and broader notification applications remain open. Deep optimization stays at the release checkpoint; crashes, leaks, or substantial regressions remain immediate fixes.

Evidence: `artifacts/v0.2.5/smoke-final/smoke-results.json`, `live/notification-live-results.json`, the preview/dismissed PNGs in `live`, and `idle.json`.

Repeat the synthetic cases with `--smoke-test <output>`. Run the opt-in live check with `--notification-test <output> <absolute-path-to-tests/Send-NotificationTest.ps1>` after closing other WinNotch instances, enabling previews, and granting access manually. Keep WinNotch in the foreground during the live run so intentional app exclusions do not suppress test events.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.5**.

SHA-256: `F7BCB488ADBA116592A737747EFE557E0F288E22979D93D5A7A466C851CF8EDD`

---

# v0.2.4 historical validation

Validated on 5 October 2026, Windows 11 x64, one physical laptop display at 125% DPI. SDK 10.0.401; both compared portable builds contain .NET/WindowsDesktop runtime 10.0.12.

## Changes and verification

- The expanded media/timer/display/alerts controls now live in `ExpandedControls` and are created only on the first allowed expansion. Compact media, timer, and transient events continue working before the panel exists. Its initial values come from the current services/settings; later openings reuse the same controls and preserve typed input.
- `TieredCompilation=false` uses the runtime's existing compilation option. In an isolated run of the same candidate, disabling tiered compilation reduced idle working set from about 127 MiB to 119.7 MiB and CPU to 0.130%; the final executable measurements follow below. No module was disabled, no collection forced, and no working set trimmed. [Microsoft runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation#tiered-compilation).
- **157 checks passed:** 84 core, 60 published UI/native, 9 native media, and 4 native fullscreen checks. Five new UI checks cover deferred construction, suppressed expansion, first-use initialization, current audio state, and reuse/input retention. The first fullscreen attempt could not obtain foreground focus; the retry passed after Computer Use activated the fixture window. All media operations targeted the named silent local fixture.
- Published smoke testing passed with real brightness writes/restoration and a real-process restart. The parent exited with code 0 and exactly one responsive replacement started. An independent WMI read confirmed brightness restored to 100%.
- The final portable reported **Listening locally** after restart, and Settings/media/brightness/timer views were visually inspected. Permission settings were not changed. This update did not repeat real toast delivery or denial/revocation tests.
- Release publish completed without warnings/errors. `dist/WinNotch-0.2.4-x64.msix` passed MakeAppx validation and remains **unsigned and uninstalled**.
- User settings stayed byte-identical (SHA-256 `47842713820B889F87BFC88D24DDCB22255B40986FA47EB97F332824B9DF1F7B`), and the sign-in entry still points to the canonical portable. The final executable is left running.

## Measurements and limits

Two sequential old/new pairs used identical saved settings, 8 seconds warmup, and 20 seconds sampling per process. Notifications and brightness were enabled; no active media, countdown, open Settings window, or expanded panel was present during sampling. Live listener readiness was checked separately on the final build. Builds, profilers, and integration fixtures were not running during sampling.

| Executable | Mean / peak sampled working set | Private bytes | Normalized CPU | Input-idle startup proxy |
| --- | ---: | ---: | ---: | ---: |
| v0.2.3, round 1 | 135.1 / 135.9 MiB | 52.5 MiB | 0.193% | 1,458 ms |
| v0.2.4 final, round 1 | 121.3 / 121.9 MiB | 47.6 MiB | 0.125% | 1,310 ms |
| v0.2.3, round 2 | 129.6 / 130.5 MiB | 52.5 MiB | 0.236% | 1,231 ms |
| v0.2.4 final, round 2 | 120.7 / 121.1 MiB | 47.5 MiB | 0.135% | 1,345 ms |

Mean initial idle working set across these two rounds fell from **132.35 to 121.0 MiB** (about 8.6%). This is a small local sample, not a general guarantee or a cold-boot measurement. The first synchronous expansion took **112.9 ms** in the final smoke fixture, before its animation completed; the PRD's <100 ms response target is therefore not established.

After opening Settings and the panel and running the media/fullscreen fixtures, both windows were closed. A separate 60-second idle sample of that same final process measured **157.6 MiB** working set (157.5–158.2), **70.5 MiB** private bytes, and **0.124% CPU**. The controls are intentionally retained for reuse. These numbers do not establish a leak or long-session stability; they show that initial idle savings do not persist as the same absolute memory level after normal interaction.

**The ≤120 MB RAM target remains unmet**, including after interaction. Units above are MiB (1,048,576 bytes), not decimal MB. Startup at sign-in, cold boot, sustained animation FPS, extended sessions, exclusive games, physical mixed-DPI monitors, and notification denial/revocation remain open.

Evidence: `artifacts/v0.2.4/final-comparison.json`, `post-use-idle.json`, `candidate-comparison.json`, `compilation-experiment.json`, `smoke-final/smoke-results.json`, the PNGs in that folder, and `restart-results.json`.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.4**.

SHA-256: `97B5196A279927775E301F013C6906328570E376F45BABB47B3A94E778265514`

---

# v0.2.3 historical validation

Validated on 4 October 2026, Windows 11 x64, one physical laptop display at 125% DPI.

## Performance changes and correctness

- Foreground executable lookup starts with a 512-character buffer instead of 32,768 characters, retaining the larger retry for `ERROR_INSUFFICIENT_BUFFER`. Normal foreground exclusion and restoration passed the native fixture; the long-path retry was not exercised on this machine.
- Brightness polling keeps its existing cadence but only updates the UI for the first sample, a changed value, or a write acknowledgement. A write is still acknowledged when the hardware rounds to the existing value, so the slider can reflect the actual supported step.
- Settings use System.Text.Json source-generated metadata. Round-trip compatibility and legacy fullscreen defaults passed without changing the on-disk format or the user's preferences.
- **84 core checks and 55 published UI/native checks passed: 139 total**, plus a real-process restart. The old process exited with code 0 and exactly one responsive replacement started from the canonical portable path.
- New native checks verified that unchanged brightness polls emit no UI update and an unchanged write still emits one acknowledgement. The physical display changed from 100% to 95% and was restored to 100%; an independent WMI read confirmed restoration. Existing timer, media/audio UI, display, exclusion, theme, notification fixture, focus, and tray checks passed. Updated brightness and Settings captures were visually inspected.
- The final portable reported **Listening locally** before and after the restart, with no new permission prompt. Real toast delivery was verified in v0.2.2 below; this update checked listener readiness and synthetic preview behavior, not a new delivery round-trip. Denial/revocation and broader Windows/MSIX permission behavior remain open.
- Release build and self-contained publish succeeded without warnings/errors. MakeAppx validated and produced `dist/WinNotch-0.2.3-x64.msix`; it remains **unsigned and uninstalled**.
- Saved settings stayed byte-identical: SHA-256 `47842713820B889F87BFC88D24DDCB22255B40986FA47EB97F332824B9DF1F7B`. The sign-in entry still points to `dist/WinNotch/WinNotch.exe`. The final portable was left running.

## Idle measurements on the final executable

Sequential final-old-final launches used identical saved preferences: media, battery, volume, timer, internal brightness and notifications enabled; idle clock off, no playing media or active countdown. Each run used 8 seconds warmup and 20 seconds sampling. Settings was opened only after sampling to confirm the final listener was active. No build, profiler, forced collection, or working-set trimming ran during these samples.

| Executable | Mean / peak sampled working set | Private bytes | CPU, normalized across logical processors | Input-idle startup proxy |
| --- | ---: | ---: | ---: | ---: |
| v0.2.3 final, run 1 | 130.8 / 131.5 MiB | 52.3 MiB | 0.193% | 1,088 ms |
| v0.2.2 baseline | 130.6 / 131.4 MiB | 53.5 MiB | 0.198% | 1,139 ms |
| v0.2.3 final, run 2 | 130.8 / 131.5 MiB | 51.6 MiB | 0.174% | 1,071 ms |

These samples show slightly lower private bytes, but **do not demonstrate a reduction in total working set**. Earlier candidate/baseline samples varied from 129.6 to 135.4 MiB. The **≤120 MB RAM target remains unmet**; CPU was within the PRD's <0.5% target in these short idle samples. Input-idle is not full module readiness or a cold-boot guarantee. Long sessions, animation FPS, and interaction latency still need measurement.

Exploratory ReadyToRun and GC-conservation runs did not show a useful working-set reduction, so neither configuration was adopted. An initial attached measurement was excluded after the running process reported notifications off despite the saved preference; fresh baseline and final instances reported `Listening locally`.

Evidence: `artifacts/v0.2.3/final-comparison.json`, `candidate-comparison.json`, `smoke-final/smoke-results.json`, the PNGs in that folder, and `restart-results.json`. The updated `tests/Measure-Idle.ps1` records process IDs and sample peaks and can leave the last process running for a status check.

Portable: `dist/WinNotch/WinNotch.exe`, version **0.2.3**.

SHA-256: `73A49405B224264AF9C66CE131466F3CC09D4E84E89C0DCEE86B31765578B329`

---

# v0.2.2 historical validation

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
