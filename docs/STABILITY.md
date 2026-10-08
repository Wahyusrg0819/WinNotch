# Memory stability check — 8 October 2026

**The 15-minute check completed.** Closed Settings windows were reclaimed and working set fell during normal garbage collections. This did not establish flat private memory usage or prove the absence of a longer-term leak. No runtime tuning or forced memory cleanup was added.

## Protocol

The diagnostic build uses the same v0.2.9 application behavior plus the corrected performance harness. After its short warmup/interaction benchmark, `--stability-test` runs ten cycles over five minutes, visiting all seven enabled panels and opening/closing Settings. It then leaves the app idle for ten minutes. The repeated phase does not activate its windows.

Samples record working set, private committed bytes, managed allocations, GC committed memory, handle/thread counts, GC collections, normalized CPU, and weak references to closed Settings windows. Sampling is about every five seconds, without forcing GC, trimming the working set, taking heap dumps, or writing hardware settings. No builds or profilers ran during measurement. Calendar data is isolated and preferences are not saved. The user confirmed no active timer or unsaved preferences before the normal session was replaced temporarily.

## Results

| Measurement | Result |
| --- | ---: |
| Duration | 903.8 seconds (15 minutes 4 seconds) |
| Samples | 178 |
| Largest sample gap | 7.8 seconds |
| Interaction cycles / loaded panels | 10 / 7 |
| Working set, mean / peak / final | 167.7 / 173.0 / 170.7 MiB |
| Private committed bytes, mean / peak / final | 77.5 / 82.8 / 80.8 MiB |
| Normalized CPU, sample mean | 0.140% |
| Handles, first / final / peak | 820 / 814 / 877 |
| Gen2 collections during the stability phase | 3 |
| Closed Settings windows still alive at the end | 0 of 10 |
| Bluetooth polling after collapse | Stopped |

At **317 seconds**, working set fell from **169.9 to 166.1 MiB** across a normal collection. At **628 seconds**, it fell from **173.0 to 165.9 MiB**, while private commit fell from **82.8 to 77.4 MiB**. Every closed Settings window had been reclaimed after these collections. The application continued sampling and was responsive at the external process checks.

The average private commit in an early idle window (330–450 seconds) was **75.8 MiB**, versus **80.0 MiB** during the final two minutes. Those windows are at different points in the collection cycle, so the **4.2 MiB increase is an observation, not proof of a leak**. Equally, the collections and reclaimed windows do not justify declaring all memory stable. Longer repeated observations, especially of private/native memory after collections, remain necessary before making that claim.

These stress/sampler values should not be treated as an old/new application comparison against the earlier short benchmark. The application was not changed to reduce RAM in this task, and the **≤120 MB total-working-set target remains unmet**.

## Evidence and verification

- `artifacts/stability/run1/stability.csv`: all time-series samples.
- `artifacts/stability/run1/stability-result.json`: duration, sample gaps, cycles, panel count, window reclamation, and polling state.
- `artifacts/stability/summary.json`: aggregate statistics and collection transitions.
- `artifacts/stability/publish.txt` and `build-check.txt`: diagnostic publish and successful build plus **105 core checks**, without warnings/errors.
- The process exited after the final summary and all completion assertions were satisfied. The external watcher did not obtain an OS exit code; `exit.json` records that value as null.
- `restored-app.json` records the normal canonical v0.2.9 session restored afterward. Settings and agenda hashes were preserved in `user-data-before.json` / `user-data-after.json`.

The reusable command and its completion safeguards are documented in the [README](../README.md#build-from-source). This check does **not** cover multi-hour usage, sleep/resume, sustained 60 FPS, active playback across different players, or physical device transitions. The next memory investigation should distinguish retained private/native allocations from temporary allocations between natural collections.
