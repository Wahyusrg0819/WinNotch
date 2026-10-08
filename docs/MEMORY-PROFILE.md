# Memory investigation — 8 October 2026

The next RAM checkpoint found a retention bug in the **benchmark**, not evidence of the same leak in normal WinNotch operation. The benchmark is corrected and a read-only resident-page diagnostic is now available. No new runtime tuning was adopted: the tested GC setting did not demonstrate a consistent saving.

## What remains in memory

On this Windows 11 x64 laptop at 125% DPI, with all modules enabled and no active media session, the original interaction benchmark produced this resident-page snapshot after visiting every panel and closing Settings:

| Snapshot | Total resident | Private resident | Shareable resident |
| --- | ---: | ---: | ---: |
| Original benchmark | 163.8 MiB | 47.0 MiB | 116.8 MiB |
| Corrected benchmark | 159.9 MiB | 43.1 MiB | 116.8 MiB |

These are snapshots taken after the benchmark report was written, not the earlier phase averages below. They were captured **before** collecting a heap dump. The original map included 37.1 MiB mapped from the portable executable and 23.8 MiB of unnamed mappings, alongside Windows/runtime/graphics libraries and private regions. Unnamed mappings were not attributed to a particular subsystem.

The corrected heap dump contained **3,484,848 bytes (3.3 MiB) of reachable managed objects**. This is part of process memory, not an additional amount to add to the table. Private committed bytes, private resident bytes, total resident bytes, and reachable managed objects measure different things. `GC.GetTotalMemory(false)` can also include objects awaiting collection, which explains why its benchmark value varies more than the reachable-object snapshot.

`Measure-MemoryMap.ps1` uses [QueryWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-queryworkingset), groups page metadata, and reconciles every page with the total. Microsoft's [Shared flag](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-psapi_working_set_block) means **shareable**, not proof that another process currently uses that page. We retain total working set as the existing ≤120 MB target metric; reporting private pages does not make that target achieved.

## Retention fix and comparison

`PerformanceValidation.RunAsync` held its local `SettingsWindow` across later awaits after closing it. SOS `gcroot` traced a strong reference from the benchmark's async state machine to that window. The benchmark now clears that reference immediately after closing. A second dump's live-object query found **no reachable `WinNotch.SettingsWindow`**. Normal `App.OpenSettings` already clears its window reference on close; no equivalent production fix was needed.

| Interaction sample | Mean idle WS | Mean WS after repeated use | Private commit after repeated use |
| --- | ---: | ---: | ---: |
| Original benchmark | 125.3 MiB | 162.2 MiB | 73.1 MiB |
| Corrected, default GC, run 1 | 125.3 MiB | 158.0 MiB | 68.3 MiB |
| Corrected, default GC, run 2 | 124.9 MiB | 161.0 MiB | 71.8 MiB |
| Corrected, GC conserve-memory 6 | 125.3 MiB | 158.4 MiB | 70.6 MiB |

Each phase uses ten samples 500 ms apart after eight seconds startup warmup. The same seven panels are visited in two batches, with one Settings visit between them. Agenda fixtures are isolated, preferences unchanged, and no profiler/build runs during the measured phases. No forced GC or working-set trimming is used. The post-measurement dump is kept separate because [dump collection may page memory into the process](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump).

The corrected default runs measured first expansion plus layout at **128.2–133.1 ms**. Bluetooth polling stopped after collapse. The [GC conserve-memory setting](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#conserve-memory), tested only in the diagnostic child process, landed inside the default run-to-run range. Its potential extra collections/pauses are not justified by these results, so application/runtime settings remain unchanged. The benchmark correction must not be presented as a demonstrated RAM improvement for normal users.

## Reproduction and validation

Build from current source, close other WinNotch instances, then use the `--performance-test ... --memory-profile` workflow in the [README](../README.md#build-from-source). The profile flag keeps only that diagnostic instance alive for 45 seconds after writing its report, allowing the page map or a local heap dump to be captured. It does not affect normal startup.

Evidence is under the Git-ignored `artifacts/memory-profile/` folder: `after-use-map.json`, `corrected-map.json`, `retention.txt`, `corrected-live-heap.txt`, `comparison.json`, and per-run `performance.json` files. Full dumps stay local; the report contains counts/types and memory sizes, not notification text or agenda contents.

- The current source built without warnings/errors and passed **105 core checks**.
- The page tool reconciled its totals internally and was compared with the independent process working-set reading. Its invalid-process failure path was also checked.
- The diagnostic instances exited after the normal run or bounded profile hold. No application dependency was added.
- The v0.2.9 portable remains the same binary. Its normal session was restored, with settings and real agenda hashes preserved.

The **≤120 MB total working-set and <100 ms first-use targets remain open**. These short samples do not establish a leak-free long session, sleep/resume reliability, or sustained animation performance. Further RAM work should investigate the fixed runtime/native/mapped footprint; another UI-object micro-optimization is unlikely to recover the entire gap shown here.
