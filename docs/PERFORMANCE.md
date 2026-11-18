# Performance measurements

## Scope and status

PERF-001 measures the implemented managed-text profile, not secure document sanitization or complete sensitive-data discovery. `benchmarks/NuntivusRedact.Benchmarks` is an explicitly nonpackable .NET 10 console host using centrally pinned BenchmarkDotNet 0.15.8 (MIT license, confirmed in the restored package metadata). It is registered in both solution entry points. No production algorithm was changed and no speedup is claimed.

**Rename provenance:** all timing/GC/allocation results below were measured before the owner-requested rename from Redact.NET to NuntivusRedact. Historical log paths, discovered names, commands and source fingerprints are preserved as evidence, not asserted to identify the renamed assemblies. Only namespaces/identities changed; no new timing or performance equivalence claim is made. Current reproduction paths are adapted below; fixture correctness is rechecked separately during rename verification. See MIGRATION.md.

Restricted DOCX/XLSX implementations now exist, but no Office timing/allocation measurements were executed. PPTX, PDF, images and OCR remain unimplemented. Binary benchmarks stay deferred until the relevant feature/security acceptance gates pass. No binary input is treated as text. Subsequent CSV backend and Core edit-aware-result changes are not characterized by these historical measurements; no timing/performance-equivalence claim is made. These measurements do not complete the entire performance milestone or the full roadmap.

## Workloads and measurement boundary

All fixtures are bounded, deterministic synthetic data; no real personal data, randomness, disk/network document I/O or content logging. The fixed replacement is `[REDACTED]`. Each setup verifies accepted detection count and exact text output or structured recovery/primitive preservation before measurement. The preflight command exercises each workload twice; xUnit fixture tests also exercise reusable instances.

| Workload | Input per operation | Registered detectors | Candidate / accepted detections |
|---|---|---|---|
| Text Short | 256 UTF-16 characters (ASCII) | 7 default personal-data regex detectors | 1 / 1 |
| Text Large | 262,144 UTF-16 characters (ASCII), 128 padded 2,048-character blocks | 7 defaults | 128 / 128 |
| Text ManyDetectors | 2,048 characters, 32 distinct dictionary tokens repeated 8 times | 32 distinct public `DictionaryDetector` instances | 256 / 256 |
| Text Overlaps | 672 characters, 32 repeated phrases | 32 dictionaries with nested/identical spans, explicit descending priorities | 1,024 / 32 |
| UTF-8 text, 8 / 128 records | 216 / 3,456 bytes | 7 defaults | 8 / 8; 128 / 128 |
| JSON, 8 / 128 records | 457 / 7,297 bytes | 7 defaults | 8 / 8; 128 / 128 |
| XML, 8 / 128 records | 715 / 11,155 bytes | 7 defaults | 8 / 8; 128 / 128 |

`TextBenchmarks.Redact` invokes the real `RedactionEngine.RedactAsync`: detection, filtering, overlap resolution, replacement and source-free report creation. Policy/engine/detector construction and fixture generation are excluded. Limits are explicit: 1,000,000 input characters, 2,000,000 output characters, 10,000 candidates; individual detectors retain their own bounded defaults. These cases are not a worst-case/adversarial limit test.

`DocumentBenchmarks.Process` invokes the real text/JSON/XML processor `ProcessAsync` with fresh read-only input stream wrappers and empty output `MemoryStream`s per operation. Wrappers, output allocation/disposal, bounded UTF-8 staging, actual parser/writer work, engine calls on fields, reports, built-in output validation and final output writing are included. Fixture generation/UTF-8 input encoding and reusable composition are excluded. Default document limits apply (4,000,000 input bytes, 8,000,000 output bytes, depth 64, 10,000 fields/reports). JSON contains contact strings plus unchanged integer/boolean fields; XML contains leaf contact/count elements and unchanged ordinary attributes. Setup reparses structured output and checks synthetic source removal, counts and unrelated values. No HTTP, CSV, filesystem, concurrency, cold start, engine construction or large binary throughput is represented.

## Current reproduction commands (adapted after rename)

Run in PowerShell from the repository root with SDK 10.0.401 installed. Use Release and no attached debugger. Complete builds/tests before timing; do not run them concurrently with benchmarks. `--verify-fixtures` is a correctness preflight, not a timing result. Do not edit benchmark setup/logic after a successful measurement when comparing production changes.

```powershell
Set-Location C:/Users/salva/Documents/GitHub/nuntivus-redact
dotnet run --project benchmarks/NuntivusRedact.Benchmarks/NuntivusRedact.Benchmarks.csproj -c Release -- --verify-fixtures
dotnet run --project benchmarks/NuntivusRedact.Benchmarks/NuntivusRedact.Benchmarks.csproj -c Release --no-build -- --list flat
dotnet run --project benchmarks/NuntivusRedact.Benchmarks/NuntivusRedact.Benchmarks.csproj -c Release --no-build -- --filter '*' --job short --exporters json --join --artifacts artifacts/benchmarks/nuntivusredact-followup *> artifacts/nuntivusredact-benchmark.log
```

The timing command above is a proposed post-rename follow-up, not an executed measurement. The historical executed timing command used `benchmarks/Redact.Benchmarks/Redact.Benchmarks.csproj` and `--artifacts artifacts/benchmarks/perf001-20261117 *> artifacts/perf001-benchmark.log`. The recorded run uses BenchmarkDotNet's out-of-process `ShortRun`: one launch, three warmup iterations and three measured iterations per parameter combination, with adaptive invocation counts and `[MemoryDiagnoser]`. Ten parameter combinations cover the two discovered methods. This is a representative initial baseline, **not** a stable regression budget or statistically strong capacity assessment. Error is BenchmarkDotNet's 99.9% confidence-interval half-width; with only three samples it can be large. Gen columns count collections per 1,000 operations; Allocated is managed allocation per operation, not retained heap, peak working set or total native allocation. Engine and stream rows have different boundaries; do not interpret their ratios as equivalent-work speedups.

For a longer unprofiled follow-up, use the same command with `--job default` and a **different** artifacts directory/log; that follow-up has not been executed. CPU or allocation traces should be collected separately before optimizing. These timing runs are unprofiled; no profiler hotspot claim is made.

## Recorded environment — 2026-11-17

- Windows 11 Home x64, version 10.0.26200; SDK 10.0.401 (commit e34a38d2ae), MSBuild 18.9.11; installed host/runtime .NET 10.0.12 (commit 95017c711e).
- CPU reported by Windows: 11th Gen Intel Core i7-11800H @ 2.30 GHz, 8 physical cores / 16 logical processors; 34,046,353,408 bytes physical memory. BenchmarkDotNet's process-level CPU/GC details are authoritative for the actual run and appear with the results below.
- Runner logs record selecting the Windows High performance power plan (`8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`, localized as `Prestazioni elevate`) and successfully restoring Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`, `Bilanciato`). No independent clock/thermal/affinity measurement was made.
- Visual Studio Community 2026 18.10.3 environment. Inherited process runtime controls were **not** normalized: `COMPLUS_FusionEnableForcedFullClosureWalk=1`, `COMPlus_ThreadPool_UsePortableThreadPool=0`, `DOTNET_gcConcurrent=0`, `DOTNET_GCHeapCount=2`, `DOTNET_GCNoAffinitize=1`, `DOTNET_MULTILEVEL_LOOKUP=0`, `DOTNET_TC_CallCountThreshold=1000`, `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`. Do not generalize this host's non-concurrent workstation GC results to default-runtime or server-GC deployments. No affinity/power-plan claim is made beyond BenchmarkDotNet's own recorded output.
- No Git metadata, approved remote or commit exists, so there is no verified source provenance. Nothing was published. Exact local run reports/logs are retained below; source identity must be re-established for future comparisons.

## Executed results

The recorded run completed successfully: **10/10 benchmark cases**, global elapsed time **154.72 seconds**. Report timestamp: **2026-11-17 16:56:17** local time. No production optimization was performed. BenchmarkDotNet reported Windows 11 `10.0.26200.9974/25H2/2025Update/HudsonValley2`, X64 RyuJIT `x86-64-v4`, .NET 10.0.12 (`10.0.1226.42308`) and **Non-concurrent Workstation GC**. Input is ASCII, so UTF-16 character counts above also equal encoded UTF-8 byte counts for text fixtures.

Actual joined BenchmarkDotNet summary (unchanged values; `?` indicates an inapplicable parameter):

| Type               | Method  | Scenario      | Format | Records | Mean         | Error        | StdDev     | Gen0     | Gen1     | Gen2     | Allocated  |
|------------------- |-------- |-------------- |------- |-------- |-------------:|-------------:|-----------:|---------:|---------:|---------:|-----------:|
| **TextBenchmarks**     | **Redact**  | **Short**         | **?**      | **?**       |     **13.93 μs** |     **6.175 μs** |   **0.338 μs** |   **0.3510** |        **-** |        **-** |    **4.38 KB** |
| **TextBenchmarks**     | **Redact**  | **Large**         | **?**      | **?**       | **12,087.36 μs** | **6,251.090 μs** | **342.643 μs** | **265.6250** | **265.6250** | **265.6250** | **1090.38 KB** |
| **TextBenchmarks**     | **Redact**  | **ManyDetectors** | **?**      | **?**       |     **85.02 μs** |    **37.338 μs** |   **2.047 μs** |   **8.7891** |   **0.4883** |        **-** |   **108.7 KB** |
| **TextBenchmarks**     | **Redact**  | **Overlaps**      | **?**      | **?**       |    **151.42 μs** |    **12.204 μs** |   **0.669 μs** |  **27.8320** |   **6.8359** |        **-** |  **345.42 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **json**   | **8**       |     **64.12 μs** |    **96.825 μs** |   **5.307 μs** |  **11.7188** |   **0.9766** |        **-** |  **143.88 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **json**   | **128**     |    **972.35 μs** |   **504.372 μs** |  **27.646 μs** | **162.1094** |  **40.0391** |        **-** | **1992.38 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **text**   | **8**       |     **14.03 μs** |     **4.229 μs** |   **0.232 μs** |   **2.1057** |   **0.1221** |        **-** |      **26 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **text**   | **128**     |    **211.16 μs** |   **114.578 μs** |   **6.280 μs** |  **10.2539** |   **1.4648** |        **-** |  **126.53 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **xml**    | **8**       |    **109.55 μs** |   **122.496 μs** |   **6.714 μs** |  **22.9492** |   **1.9531** |        **-** |  **285.59 KB** |
| **DocumentBenchmarks** | **Process** | **?**             | **xml**    | **128**     |  **1,777.31 μs** | **1,248.234 μs** |  **68.420 μs** | **273.4375** | **101.5625** |        **-** | **3411.62 KB** |

Large text incurred Gen2 collections and about 1.06 MiB managed allocation per operation. Structured 128-record cases allocated about 1.95 MiB (JSON) and 3.33 MiB (XML). These are measured investigation candidates, not proven CPU hotspots or a justification to change production code without a targeted profile and a matched before/after run. Confidence intervals are broad, especially the small structured cases; repeat longer runs on a controlled host before setting regression thresholds. There is no before/after table because nothing was optimized.

Throughput below is **derived**, not an independent load/concurrency test: `operations/second = 1,000,000,000 / Statistics.Mean` (nanoseconds from the full JSON report, before display rounding). One operation is one complete fixture. Exact managed bytes/op are copied from that report's `Memory.BytesAllocatedPerOperation`; displayed KB above mean 1,024 bytes.

| Workload | Derived operations/second | Managed bytes/operation |
|---|---:|---:|
| Text Short | 71,795.09 | 4,488 |
| Text Large | 82.73 | 1,116,553 |
| Text ManyDetectors | 11,761.81 | 111,312 |
| Text Overlaps | 6,603.94 | 353,712 |
| JSON / 8 | 15,595.73 | 147,328 |
| JSON / 128 | 1,028.44 | 2,040,192 |
| UTF-8 text / 8 | 71,285.16 | 26,624 |
| UTF-8 text / 128 | 4,735.73 | 129,568 |
| XML / 8 | 9,128.28 | 292,440 |
| XML / 128 | 562.65 | 3,493,503 |

Raw evidence (local ignored artifacts, not publication or committed provenance):
- `artifacts/perf001-benchmark.log` (complete console output).
- `artifacts/benchmarks/perf001-20261117/BenchmarkRun-20261117-165343.log` (BenchmarkDotNet runner log).
- `artifacts/benchmarks/perf001-20261117/results/BenchmarkRun-joined-2026-11-17-16-56-17-report-github.md` (table above).
- Same basename with `-report.csv`, `-report.html` and `-report-full-compressed.json` (raw statistics and per-iteration/GC data).

Historical pre-rename SHA-256 fingerprints (not current source hashes or verified Git/Source Link provenance):
- `benchmarks/Redact.Benchmarks/ManagedBenchmarks.cs`: `D21BD5650B2814D459004F9C378BD078808F589884D2706112C64E894DE3EE2F`.
- `benchmarks/Redact.Benchmarks/Program.cs`: `51810C90776A4DB21D4F4BEDA63DBBAF41D029D30F687DD5C0AED8C2E374A17B`.
- `Directory.Packages.props`: `D92DFCCF83B23CC756F42796C77AD2E7D15E296A6F13C6635CB258C6AFFC2430`.

The generated benchmark initially used CRLF/mixed line endings. The format check exposed this; only line endings were normalized to repository-required LF after timing. An ordinal comparison of the original text with CRLF replaced by LF against the formatted file passed: no benchmark logic, parameters or setup changed. Fingerprints above are for the normalized files. Production code was untouched; final builds/tests are rerun after normalization. This is not a new performance variant or an optimization comparison.

## Historical correctness and packaging checks (before rename)

- Fixture preflight passed all ten cases twice; discovery returned `Redact.Benchmarks.TextBenchmarks.Redact` and `Redact.Benchmarks.DocumentBenchmarks.Process` (ten parameter combinations).
- `./tools/verification/verify.ps1` completed **2026-11-17T16:57:39.8664715+01:00**: restore, Release build and pack passed; **85/85 tests passed, zero skipped** (all original 75 plus ten xUnit fixture cases). Log: `artifacts/perf001-verification.log`; TRX under `artifacts/test-results/`. Pack still produced eleven product nupkg/snupkg pairs; benchmark host and fixture tests are nonpackable. Expected existing nonpackable Web API sample pack warning remains.
- After line-ending normalization, final `./tools/verification/verify.ps1` and IDE solution Release build completed by **2026-11-17T17:02:34.9656790+01:00**: both builds zero warnings/errors; **85/85 tests passed, zero skipped**; exactly eleven product nupkg/snupkg pairs. Log: `artifacts/perf001-final-verification.log` and `artifacts/perf001-ide-build.log`. `.git` absence was rechecked; Source Link remains blocked. Only the known nonpackable Web API pack warning occurred.
- Visual Studio discovered the ten fixture tests, but two IDE run requests (project then fully qualified method filters) reported **zero executed**; these are not passing-test evidence. The executed CLI xUnit/TRX results above are the verification evidence; no further IDE retries were made.
- `./tools/verification/package-smoke.ps1` passed: eleven product package/symbol pairs inspected; isolated package-only consumer passed all four formats and HTTP service resolution. Log: `artifacts/perf001-package-smoke.log`; consumer retained at `C:/Users/salva/AppData/Local/Temp/redact-consumer-b7839cf0c2a641d690c71830d2c8d99d`.
- `dotnet build nuntivus-redact.slnx -c Release --no-restore -tl:off` passed with zero warnings/errors; canonical solution built through verify. Log: `artifacts/perf001-ide-build.log`.
- After LF normalization, `dotnet format Redact.NET.slnx --verify-no-changes --no-restore --verbosity quiet` passed. Log: `artifacts/perf001-format.log`.
- `dotnet restore Redact.NET.slnx -p:NuGetAudit=true -p:NuGetAuditMode=all -p:TreatWarningsAsErrors=true -tl:off` and `dotnet list Redact.NET.slnx package --vulnerable --include-transitive` passed at 2026-11-17T17:01:08+01:00; configured feeds reported no vulnerable packages, including the benchmark/test development graph. Point-in-time audit only. Log: `artifacts/perf001-audit.log`.
