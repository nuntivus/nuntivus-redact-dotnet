# Detailed task backlog
Office architecture follow-up: VERIFIED locally. All Office static helpers replaced by internal interface-backed package/content/Excel/string services with unchanged public constructors and request-local state. Release build, 187/187 tests (zero skipped), formatting, twelve packages and isolated six-format consumer passed; see PROGRESS.md and artifacts/office-instances-*.log. Existing IDE unloaded-project issue remains a reload follow-up. This does not expand OFF-001/SEC-002 supported scope.

Statuses: NOT STARTED, IN PROGRESS, BLOCKED, COMPLETED, VERIFIED. Only actual successful verification permits VERIFIED. All listed projects not yet created are deferred, not empty completed deliverables.

### DOC-002: Open-source README, documentation and original artwork
Status: VERIFIED (local preparation only; GitHub configuration remains owner-controlled)
Priority: High (owner requested)
Dependencies: FMT-002, DOC-001
Description: Prepare a GitHub-facing README and documentation hub, original SVG logo/banner and truthful build/test/package-preparation badges. Owner selected OWNER/REPOSITORY placeholders; no remote, public release or deployment is configured by this task.
Expected files: README.md, docs/{README,GETTING_STARTED,GITHUB_SETUP,PACKAGE_README}.md, docs/assets/*, CONTRIBUTING.md, Directory.Build.props, tools/verification/docs-check.ps1, .github/workflows/ci.yml, CHANGELOG.md and persistent records.
Acceptance criteria: distinct accessible self-contained SVGs using the supplied parent brand's palette; accurate current feature/package/format tables and security limits; usable docs navigation/examples; actual existing workflow endpoints with explicit placeholders, not hardcoded green status; package README remains self-contained under its existing README.md package path; local checks and CI step configured without publishing.
Required tests: local Markdown/HTML target and SVG checks with negative fixtures; extracted C# example compile/run; original asset browser rendering; both Release solution builds, xUnit, formatting, executable samples, pack/README-content inspection and isolated package consumer.
Evidence — 2026-11-17, main checks complete by 18:32:08+01:00: 29 Markdown files, 154 local targets and two SVGs passed local checks before final tracking edits; 11 negative checker fixtures passed. Isolated Edge rendered banner/logo at 1280×480 / 256×256. Three LF-normalized standalone snippets match and compile/run alongside the JSON snippet against isolated packages. Both Release solutions and IDE build passed; 105/105 CLI tests, zero skipped; IDE format tests 40/40; formatting and three console samples passed. Eleven nupkg/snupkg pairs and four-format/HTTP package consumer passed; all eleven embedded README.md entries match docs/PACKAGE_README.md without GitHub images/placeholders. Expected nonpackable Web API pack warning only. Logs artifacts/docs002-{verification,workspace-build,format,samples,examples,doc-check,doc-check-regressions,package-readmes,package-smoke,svg-render}.log; TRX *18_25_47*.trx; PNG previews artifacts/docs002-{banner,logo}-preview.png. Regression runner retained at artifacts/docs002-check-regressions.ps1. Initial snippet identity mismatch was CRLF/LF only; initial browser invocation completed asynchronously, resolved by explicit process waiting. Final tracking/link check is recorded in PROGRESS.md.
Remaining owner actions: replace README repository URL placeholders and remove setup notices; run hosted workflows and review real badges; enable branch protection/private reporting and review Git/source provenance; human-review GitHub rendering and export/upload a PNG/JPEG social preview if desired. Strict identity check intentionally fails until configured. No remote URL invented, GitHub push, hosted CI success, publication, deployment or new format support is claimed. PKG-001 and full roadmap blockers remain unchanged.

### BRAND-001: Full NuntivusRedact identity rename
Status: VERIFIED
Priority: Critical (owner requested before continuing features)
Dependencies: Existing verified managed implementation and PERF-001
Description: Rename root namespaces, branded DI/HTTP APIs, project/package/assembly identities, both solution entry points, scripts, fixtures and current docs. Preserve domain operations and historical benchmark evidence; no feature implementation or optimization.
Expected files: NuntivusRedact.slnx, NuntivusRedact.Workspace.slnx, NuntivusRedact.csproj, src/NuntivusRedact.*, tests/NuntivusRedact.*, samples/NuntivusRedact.*, benchmarks/NuntivusRedact.Benchmarks, tools/verification/*, docs/MIGRATION.md and persistent records.
Acceptance criteria: all project/solution references resolve; no active legacy package/namespace dependencies; all 85 existing tests pass; all sample and fixture checks pass; eleven renamed package/symbol pairs inspected; isolated consumer uses only renamed product packages; history is explicitly labeled, not rewritten as new measurements.
Required tests: both Release solution builds; all xUnit tests; benchmark correctness preflight; console/API sample smokes; format verification; pack and package-smoke.ps1; active-identity/reference audit.
Evidence — 2026-11-17, checks complete by 17:41:41+01:00: canonical restore/Release build/test/pack passed, 85/85 tests and zero skipped; both on-disk solutions built with zero build warnings/errors. Formatting, ten fixture preflights (each twice), renamed benchmark discovery, three console smokes, live OpenAPI/JSON/no-store/PDF-415 smoke, strict audited restore/vulnerability listing, active-identity/reference checks and eleven renamed package/symbol pairs with isolated consumer passed. Logs: artifacts/brand001-*.log and current renamed TRX. Expected nonpackable Web API pack warning only. Sample server stopped. IDE retains deleted old paths (NU1104/CS0234; new-name test discovery empty); reopen NuntivusRedact.Workspace.slnx to refresh it. CLI/TRX, not stale IDE diagnostics, supplies verification. No timing rerun, publication or expanded format claim.

### FND-001: Solution foundation
Status: VERIFIED
Priority: Critical
Dependencies: None
Description: Establish SDK pinning, nullable/analyzers/XML docs, CPM, metadata, scripts and CI.
Expected files: AGENTS.md, .github/copilot-instructions.md, NuntivusRedact.slnx, global.json, Directory.*.props, .editorconfig, .github/workflows/*, tools/verification/*, docs/*.
Acceptance criteria: meaningful foundation projects compile; no circular references; scripts fail on errors; CI restores/builds/tests/packs; complete roadmap exists.
Required tests: dotnet restore; dotnet build -c Release; test script exit behavior; inspect project references.

### CORE-001: Detection engine and contracts
Status: VERIFIED
Priority: Critical
Dependencies: FND-001
Description: Immutable detection/span/context/category/confidence contracts, explicit registration, priority ordering, overlap resolution, cancellation, sanitized failures.
Expected files: src/NuntivusRedact.Abstractions/*, src/NuntivusRedact.Core/*, tests/NuntivusRedact.Core.Tests/*.
Acceptance criteria: validated ranges/confidence; deterministic overlap; no source in reports/errors; cancellation honored; immutable policy snapshot.
Required tests: boundaries, overlaps, ties, errors, cancellation, concurrent reuse; Release build and xUnit.

### DET-001: Built-in detectors
Status: VERIFIED
Priority: High
Dependencies: CORE-001
Description: Email, phone, IPv4/IPv6, IBAN mod97, URL, card Luhn, custom bounded regex/keywords/dictionary; document limits.
Expected files: src/NuntivusRedact.Detectors.Regex/*, src/NuntivusRedact.Detectors.PersonalData/*, tests/NuntivusRedact.Detectors.Tests/*.
Acceptance criteria: valid/invalid candidates covered; timeout and input limits; no matched source in diagnostics.
Required tests: checksums, adversarial patterns, cancellation, unicode/boundaries, false positive fixtures.

### STR-001: Redaction strategies
Status: VERIFIED
Priority: Critical
Dependencies: CORE-001
Description: Fixed/category replacements, full/partial masking, keyed deterministic pseudonymization and custom extension; overlap-safe transform.
Expected files: src/NuntivusRedact.Strategies/*, tests/NuntivusRedact.Strategies.Tests/*.
Acceptance criteria: deterministic keyed output; no unkeyed hashes; no offset drift/rescanning; distinguish semantics.
Required tests: each strategy, adjacent edits, key isolation, Unicode, invalid settings, concurrent use.

### POL-001: Immutable policy
Status: VERIFIED
Priority: High
Dependencies: CORE-001, STR-001
Description: Builder, category inclusion/exclusion, detector priority overrides, confidence thresholds, custom predicates/strategy selection, validated strict mode.
Expected files: src/NuntivusRedact.Core/*Policy*, tests/NuntivusRedact.Core.Tests/*Policy*.
Acceptance criteria: snapshot reuse; filtering before overlap; explicit rejection of unsafe unsupported mode; invalid configuration rejected.
Required tests: threshold boundaries, exclusions, priorities, custom rules, independent builder snapshots, strict error propagation.

### DI-001: Optional dependency injection
Status: VERIFIED
Priority: High
Dependencies: CORE-001, POL-001
Description: AddNuntivusRedact, AddDetector<T>, AddStrategy<T>, AddDocumentProcessor<T>, options and service lifetime validation; standalone usage preserved.
Expected files: src/NuntivusRedact.Extensions.DependencyInjection/*, tests/NuntivusRedact.Integration.Tests/*.
Acceptance criteria: explicit registration works without service locator; invalid options/lifetimes fail; repeated registration deterministic.
Required tests: container resolution, options errors, scopes/lifetimes, custom extensions, standalone equivalence.

### FMT-001: Text and structured formats
Status: VERIFIED
Priority: Critical
Dependencies: CORE-001, STR-001, POL-001
Description: TXT/CSV/JSON/XML bounded stream processing, explicit format detection, safe parser settings, structural output validation and source path reporting.
Expected files: src/NuntivusRedact.Formats.{Text,Csv,Json,Xml}/*, tests/NuntivusRedact.Formats.Tests/*.
Acceptance criteria: valid outputs; unrelated values semantically preserved; duplicate/key ambiguity handled; no DTDs; input/output limits; cancellation; no partial success on parser/detector errors.
Required tests: quoted multiline CSV, JSON keys/values, XML attributes/namespaces/comments/CDATA, malformed/deep inputs, limits, cancellation, streams remain open.

### FMT-002: Library-backed format processor review
Status: VERIFIED
Priority: High (owner requested)
Dependencies: FMT-001
Description: Review every implemented format processor and delegate syntax parsing/writing to established libraries where appropriate. Replace handwritten CSV syntax with CsvHelper; retain framework libraries already used for JSON/XML/TXT. Do not expand unsupported binary claims.
Expected files: Directory.Packages.props, src/NuntivusRedact.Formats.Csv/*, tests/NuntivusRedact.Formats.Tests/FormatTests.cs, docs/formats/structured.md, docs/ARCHITECTURE.md, docs/DECISIONS.md and persistent records.
Acceptance criteria: dependency/license reviewed and centrally pinned; no public contract break; strict CSV profile, numeric paths, bounds, cancellation, caller streams and staged failure semantics preserved; no unnecessary JSON/XML/text packages.
Required tests: quoted/multiline/blank/ragged CSV, Unicode and output-byte limits, malformed quotes including escaped-quote EOF, replacement escaping, cancellation, concurrent reuse and recovery; full Release tests/build/pack, format check, strict dependency audit and package-only consumer.
Evidence — 2026-11-17, code checks complete by 18:07:26+01:00: CsvHelper 33.1.0 (MS-PL OR Apache-2.0, Apache-2.0 selected) isolated to Formats.Csv. Both Release solutions and IDE build passed; canonical verify.ps1 passed restore/build/test/pack with 105/105 tests, zero skipped and eleven package/symbol pairs. Format tests are 40/40 including 20 new cases. Formatting and strict audited restore/vulnerability listing passed (point-in-time); isolated four-format/HTTP package consumer passed. Expected nonpackable Web API pack warning only. Logs artifacts/fmt002-{formats,verification,workspace-build,format,audit,package-smoke}.log; TRX artifacts/test-results/fmt002-formats.trx and 18_06_24 run files. See ADR-009. No performance rerun or publication; historical CSV timings do not characterize this implementation. Full roadmap and provenance blockers remain unchanged.

### OFF-001: Secure Office support
Status: IN PROGRESS (restricted DOCX/XLSX implemented; PPTX and Microsoft-application/visual follow-ups deferred)
Evidence: dependency/license review, additive edit-aware contracts, both CLI Release solutions, final **186/186 tests (zero skipped)**, twelve package/symbol pairs, six-format isolated consumer, samples, formatting, docs and audit passed. Includes ST_Xstring/opaque-property/custom-number-format hardening. Exact results/commands and the blocked Word COM smoke are in PROGRESS.md. No broad Office fidelity, PDF/OCR, native isolation or binary performance claim.
Priority: High
Dependencies: FMT-001, SEC-001
Description: Open XML DOCX/XLSX/PPTX and content inventory. Handle or reject paragraphs/runs/tables/headers/footers/comments/tracked changes/hidden sheets/formulas/caches/notes/metadata/embedded objects/relationships.
Expected files: src/NuntivusRedact.Formats.Office/*, tests/NuntivusRedact.Formats.Tests/Office*, docs/formats/office.md.
Acceptance criteria: each surface supported with recovery checks or rejected; archive and relationship security bounded.
Required tests: zipped recovery scans, malicious archives, split runs, cached values, hidden content and repair/reopen validation.

### PDF-001: Secure PDF support
Status: NOT STARTED
Priority: High
Dependencies: FMT-001, SEC-001
Description: Research license-compatible backend and obtain approval; real content removal, positioning, metadata/annotations/forms/attachments/revisions/OCR/scanned handling or rejection.
Expected files: src/NuntivusRedact.Formats.Pdf/*, docs/formats/pdf.md, docs/DECISIONS.md, tests/NuntivusRedact.Security.Tests/Pdf*.
Acceptance criteria: license approved; reparsing and supported recovery paths prove removal; unsupported cases fail closed; no cosmetic rectangle claims.
Required tests: extraction, raw object/revision recovery, annotations/forms/attachments/metadata, scanned-document rejection or secure pipeline.

### OCR-001: Image and OCR support
Status: NOT STARTED
Priority: High
Dependencies: CORE-001, SEC-001
Description: OCR contracts, optional Tesseract adapter, coordinate mapping, permanent pixel redaction, sanitized metadata and validated re-encoding.
Expected files: src/NuntivusRedact.Ocr.Abstractions/*, src/NuntivusRedact.Ocr.Tesseract/*, src/NuntivusRedact.Formats.Images/*.
Acceptance criteria: coordinates validated; untrusted native processing isolated as needed; accuracy limitations explicit; multi-frame data handled/rejected.
Required tests: pixel recovery, OCR coordinate fixtures, metadata, frames, malformed images, limits and provider failures.

### WEB-001: ASP.NET Core integration
Status: VERIFIED
Priority: Medium
Dependencies: DI-001, FMT-001, SEC-001
Description: Optional integration, API sample, bounded streaming upload/download, cancellation, structured safe errors and OpenAPI.
Expected files: src/NuntivusRedact.AspNetCore/*, samples/NuntivusRedact.Sample.WebApi/*, tests/NuntivusRedact.Integration.Tests/Web*.
Acceptance criteria: no content logging; request limits before buffering; response not committed on processing failure; no billing/SaaS.
Required tests: HTTP oversized/malformed/cancelled requests, supported processing and sanitized errors.

### SEC-001: Managed-profile security baseline
Status: VERIFIED
Priority: Critical
Dependencies: CORE-001
Description: Managed-text threat model; byte/depth/field/report limits; XML entity restrictions; regex timeouts; concurrency; supported JSON recovery; safe exceptions/output staging and dependency audits. Archive/native/metadata hardening remains SEC-002; this is not the whole security milestone.
Expected files: docs/SECURITY.md, tests/NuntivusRedact.Security.Tests/*, tests/NuntivusRedact.Architecture.Tests/*, .github/workflows/security.yml.
Acceptance criteria: threats mapped to implemented mitigations/tests or explicit unsupported surfaces; dependency audit reviewed; isolation documented.
Required tests: adversarial, concurrency, error leakage, cancellation, raw recovery where supported; package vulnerability audit.

### SEC-002: Binary/native isolation and recovery hardening
Status: IN PROGRESS (restricted Office archive/relationship/metadata/recovery checks implemented; remaining binary/native work deferred)
Priority: Critical
Dependencies: SEC-001; adapter-specific recovery checks completed with OFF-001, PDF-001 and OCR-001
Description: Zip entry/count/ratio/size controls; external relationship rejection; encrypted/embedded content handling; metadata sanitation; safe temporary files; isolated worker/Docker assessment and implementation where necessary; recovery tests for every supported binary surface.
Expected files: tests/NuntivusRedact.Security.Tests/{Office,Pdf,Images}*, tools/verification/*, docs/SECURITY.md, optional isolated worker/container files.
Acceptance criteria: malicious archives/native payloads bounded and isolated; temporary data lifecycle verified; all supported hidden/revision/attachment/metadata surfaces removed or rejected; unsupported surfaces remain explicit.
Required tests: zip bombs/path traversal, worker resource/cancellation/failure cleanup, metadata/revision/attachment/pixel recovery and concurrent processing. No binary security claim until executed checks pass.

### PERF-001: Measurement-driven benchmarks
Status: VERIFIED
Priority: Medium
Dependencies: FMT-001
Description: BenchmarkDotNet short/large/many/overlap/text/JSON/XML with real environment/results. Managed acceptance only; later binary measurements remain PERF-002, not a completed full performance milestone.
Expected files: benchmarks/NuntivusRedact.Benchmarks/*, tests/NuntivusRedact.Benchmarks.Tests/*, Directory.Packages.props, both solution entry points, docs/PERFORMANCE.md.
Acceptance criteria: nonpackable centrally versioned host invokes public product APIs; bounded deterministic fixtures; executed benchmarks and documented environment/actual results; unavailable formats explicitly deferred; existing tests/packages preserved.
Required tests: benchmark discovery, fixture validation/recovery/reuse, representative run, Release build and all xUnit tests, pack and package-only smoke; no optimization without profiling and a matched before/after comparison.
Evidence — 2026-11-17: BenchmarkDotNet 0.15.8 ShortRun completed 10/10 cases in 154.72 seconds; MemoryDiagnoser timing/GC/allocation table, derived throughput, exact commands, inherited runtime controls and raw report paths in PERFORMANCE.md. Preflight passed all ten fixtures; ten new xUnit cases plus all original 75 passed (85/85, zero skipped). Final restore/Release build/test/pack and both solution builds passed by 17:02:34+01:00; 11 product package/symbol pairs remain, isolated consumer passed. Formatting passed after line-ending-only normalization (ordinal content guard proved no workload/setup changes); dependency audit reported no vulnerable packages in configured feeds. No production algorithm changed and no publication occurred. IDE fixture-run requests executed zero tests; CLI/TRX is the actual test evidence.

### PERF-002: Deferred binary-format performance measurements
Status: NOT STARTED
Priority: Medium
Dependencies: OFF-001, PDF-001, OCR-001 and applicable SEC-002 recovery/security checks
Description: Measure Office/PDF and later image/OCR pipelines only after relevant feature/security gates pass. Restricted DOCX/XLSX now exists but has no measurements; PPTX/PDF/images/OCR remain unsupported. Do not create placeholder processors or pretend binary bytes are text.
Expected files: benchmarks/NuntivusRedact.Benchmarks/*, docs/PERFORMANCE.md, adapter fixture tests.
Acceptance criteria: bounded deterministic supported binary workloads, real public API execution, explicit measurement boundaries and actual environment/results; recovery/security correctness verified independently; no speculative optimizations or fabricated numbers.
Required tests: adapter correctness/recovery first; fixture preflight, benchmark discovery and representative runs with actual timing/allocation results. Full milestone 13 remains incomplete until required binary scenarios execute.

### PKG-001: NuGet packaging
Status: BLOCKED
Priority: High
Dependencies: FMT-001, DOC-001
Description: Independent metadata, README/license/XML docs, symbols, Source Link, versioning, package validation/release notes.
Expected files: Directory.Build.props, src/*/*.csproj, .github/workflows/release.yml, docs/RELEASE_PLAN.md.
Acceptance criteria: Release pack succeeds; package contents inspected; no placeholder packages; no publishing without owner approval.
Required tests: dotnet pack -c Release; inspect nupkg/snupkg contents and dependency graph; consumer smoke test.
Evidence: twelve local nupkg/snupkg pairs and an isolated six-format/HTTP/edit-aware consumer passed for the final hardened Office code. README/LICENSE/XML/PDB checks passed. Current blocker: release Source Link/source provenance is not verified for the uncommitted Office changes. A real Git repository/remote and feat/office-format branch now exist; the old no-repository blocker is historical. No package published. Solution pack emits an expected nonpackable Web API sample warning; libraries package successfully.

### DOC-001: Documentation and working samples
Status: VERIFIED
Priority: High
Dependencies: FMT-001, DI-001
Description: README/quick start/API/extensions/DI/standalone/security/format matrix/install/versioning; console, DI, custom detector and Web API samples.
Expected files: README.md, docs/*.md, docs/formats/*, samples/*, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, CHANGELOG.md, LICENSE.
Acceptance criteria: every advertised feature matches implementation; examples compile; all limitations visible.
Required tests: Release sample builds and smoke runs; links and API review.

### EXT-001: Remaining first-class extension contracts
Status: NOT STARTED
Priority: High
Dependencies: CORE-001; OCR provider contracts coordinated with OCR-001
Description: Evaluate and implement first-class custom analyzer, policy, validation-rule and OCR-provider contracts with explicit registration. Current custom behavior is supported by ISensitiveDataDetector, strategy/processor interfaces, and immutable policy predicates; separate analyzer/policy/validator/OCR interfaces are not yet delivered.
Expected files: src/NuntivusRedact.Abstractions/*, src/NuntivusRedact.Ocr.Abstractions/*, src/NuntivusRedact.Core/*, src/NuntivusRedact.Extensions.DependencyInjection/*, samples/*, docs/EXTENSIBILITY.md.
Acceptance criteria: working external extension examples, inward dependencies, thread safety, bounded/cancellable processing, public XML docs and compatibility review; no reflection discovery or unnecessary parallel abstractions.
Required tests: external implementation/registration, validation failure semantics, cancellation, configuration snapshots and architecture checks.

## Executed checkpoint evidence — 2026-11-17

- Final tools/verification/verify.ps1 at 2026-11-17T16:43:34+01:00: restore and Release build passed (0 build warnings/errors), **75/75 tests passed, 0 skipped**, all 11 package/symbol pairs created. All 6 HTTP tests also passed via Visual Studio; IDE build passed. Full evidence in PROGRESS.md.
- Core 14, detectors 19, strategies 3, formats 20, DI/HTTP 9 (after new test), security 9, architecture 1.
- Three console samples executed with assertions; Web API live Kestrel smoke passed JSON/no-store/OpenAPI/unsupported PDF 415. Background server stopped.
- dotnet format --verify-no-changes passed; dependency audit reported no vulnerable packages in configured feeds.
- package-smoke.ps1 inspected README/LICENSE/XML/PDB and ran an isolated package-only consumer successfully.
- That earlier checkpoint had no benchmarks. PERF-001 managed measurements are now verified above. OFF-001, PDF-001, OCR-001, SEC-002, EXT-001, PERF-002 and REL-001 remain unfinished; PKG-001 remains BLOCKED. No empty binary projects were created. Full project is NOT complete.

### REL-001: Final project verification
Status: NOT STARTED
Priority: Critical
Dependencies: All preceding tasks
Description: Restore, Release build/test/pack, security and benchmarks separately; inspect all claims, projects, docs and packages.
Expected files: docs/RELEASE_PLAN.md, docs/TEST_MATRIX.md, docs/PROGRESS.md, docs/RESUME.md.
Acceptance criteria: all mandatory milestones VERIFIED; no placeholders in advertised features; complete release checklist.
Required tests: dotnet restore; dotnet build -c Release; dotnet test -c Release; dotnet pack -c Release; relevant security and benchmark runs.
