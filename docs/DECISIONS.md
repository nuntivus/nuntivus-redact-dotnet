# Architecture decisions

## ADR-012: Offline multilingual NLP through replaceable providers
Context: owner requested a working local/offline multilingual detector, neutral abstractions and a replaceable provider, with models recommended but provisioned separately.
Decision: add Nlp.Abstractions, Detectors.Nlp and Nlp.Onnx; preserve Core and existing public contracts. IEntityRecognitionProvider returns source-free original UTF-16 entities; NlpDetector validates and maps labels. The optional CPU provider supports a narrow cased WordPiece/BIO profile with local digest-pinned assets, complete bounded windowing, deterministic reconciliation, strict output validation and bounded admission. Runtime code never downloads models. Keep existing regex/dictionaries; do not introduce a generative LLM or pretend generic NER identifies all PII.
Dependencies: centrally pin Microsoft.ML.OnnxRuntime 1.32.0 and Microsoft.ML.Tokenizers 3.0.0, confined to Nlp.Onnx. Inspected restored nuspecs/LICENSE: both MIT. Audited targeted restore passed. Model metadata review recommends Davlan's multilingual cased DistilBERT family conditionally on separate AFL-3.0/license/data review; no model license approval, redistribution or execution is implied. WikiNEuRal explicitly restricts non-commercial research and is not recommended for unrestricted commercial deployment. Exact revisions, labels and source links are in nlp.md; inspected configuration includes DATE labels despite three-type prose in the model card.
Security: native graph parsing/inference is in-process, not sandboxed. SHA-256 proves identity, not safety. Cooperative termination, file/token/window/admission bounds do not guarantee hard native memory/time/crash containment. Hosts must isolate hostile inputs externally. Preserve sanitized strict failures without fallback, original coordinates and unchanged winner-takes-overlap policy. Document cross-field context limits and overlap coverage risk.
Verification: synthetic self-contained ONNX graphs execute real native inference without third-party weights; multilingual fixture strings verify offsets, not trained-model accuracy. Real-model tokenizer parity, target-platform deployment, per-language/domain precision/recall, span coverage and native-worker isolation remain deployment gates. No performance claim or package publication is authorized.

## ADR-011: Replaceable OCR and optional in-process Office pictures
Context: owner requested a separate OCR module, an existing .NET Tesseract wrapper and simple provider replacement, then selected embedded DOCX/XLSX PNG/JPEG pictures, permanent pixel replacement and in-process native execution.
Decision: Ocr.Abstractions supplies provider-neutral word/text/rectangle and image-redactor contracts. Ocr.Tesseract uses Apache-2.0 Tesseract 5.2.0; Formats.Images uses MIT SkiaSharp 3.119.4 and its native assets. Office references only neutral contracts, preserves existing constructors and requires explicit IImageRedactor injection to enable restricted pictures. All touched words are overwritten in decoded pixels; source alpha/metadata/compressed payloads are not retained, and output is fresh PNG. Shared nested-package image budgets and staged SDK validation remain mandatory.
Deployment: adapter-owned buildTransitive targets copy/publish the resolved wrapper dependency's native DLLs for downstream NuGet consumers; product packages do not duplicate third-party native binaries. Trusted language files are caller-owned; production never downloads models. Windows x64 native recognition is the executed profile; other native platforms remain unverified.
Security: native Tesseract/Leptonica/SkiaSharp run in-process by explicit owner choice. Bounded byte/pixel/text/concurrency checks and cooperative cancellation do not contain native crashes or guarantee hard time/memory limits. Host-supplied external isolation remains required for hostile documents; OCR-001/SEC-002 isolation gates are not declared complete. Synthetic recognition/recovery is not an accuracy guarantee, and PDF/PPTX integration stays deferred. See formats/ocr.md and PROGRESS.md for executed checks.

## ADR-010: Open XML SDK and edit-aware Office profile
Context: DOCX/XLSX must preserve supported rich formatting without retaining hidden sensitive copies. Owner approved the Office implementation plan; arbitrary embedded objects and pixel-identical rendering are not promised.
Decision: pin DocumentFormat.OpenXml 3.5.1 centrally, used only by the independent Office package. The restored net10.0 dependency graph is DocumentFormat.OpenXml.Framework 3.5.1 and System.IO.Packaging 10.0.2; all three nuspecs declare MIT. Use bounded framework ZIP/XML preflight before SDK parsing, fresh output packaging and SDK validation. Do not use Office Interop, ClosedXML or native parsing. Add an optional edit-aware interface/result without changing ITextRedactor or RedactionResult; Core emits replacements from its existing single execution pass. Replacement-bearing objects are sensitive transient data, not reports.
Evidence: artifacts/office-review/{restore,audit,probe}.log; strict audited restore and vulnerability listing passed at review time. An isolated net10.0 probe created/reopened DOCX and XLSX, checked typed bold formatting and passed OpenXmlValidator. This is feasibility, not application rendering or full Office verification. A nullable assertion in the generated probe was corrected before the successful run.
Consequences: only the explicit profile in formats/office.md is eligible for support. SDK schema validation does not establish semantic completeness or rendering fidelity; unsupported parts/features fail closed. No PDF/OCR/PPTX/native-worker or binary-performance scope is completed by this decision.

## ADR-001: .NET 10 and modular packages — 2026-11-17
Context: owner requires .NET 10 LTS and third-party extensions.
Options: monolith vs explicit independent packages.
Decision: pin installed SDK 10.0.401, target net10.0, use modular SDK projects with central package versions.
Rationale: small dependency surface and explicit contracts.
Consequences: consumers need .NET 10; binary formats do not burden text-only users.

## ADR-002: Apache-2.0 — 2026-11-17
Context: redistribution and future commercial service require a clear license.
Options: Apache-2.0, MIT, defer distribution.
Decision: owner explicitly selected Apache-2.0 in this session.
Rationale: permissive distribution with express patent terms.
Consequences: include full license; review all additional dependencies separately. No PDF backend license selected yet.

## ADR-003: Preserve original scaffold — 2026-11-17
Context: repository contains only Class1.cs and the original solution/project, with no Git metadata.
Options: delete/rename vs retain a nonpackable compatibility project.
Decision: preserve existing files; original solution will point at implemented modules, original root project excludes recursive new sources and remains unadvertised.
Consequences: two solution entry points can coexist; the canonical and workspace entry points are now NuntivusRedact.slnx and NuntivusRedact.Workspace.slnx (renamed by ADR-008). No destructive cleanup without approval.

## ADR-004: Fail closed and stage output — 2026-11-17
Context: partial redaction is dangerous; detector errors may carry source text.
Options: partial success vs sanitized failure.
Decision: strict execution only initially; sanitize extension failures without inner exceptions. Bound input and output and fully parse/transform before writing. Unsupported binary formats are rejected by absence of a processor, not treated as text.
Consequences: non-strict mode is deferred until an explicit incomplete-result contract exists. Byte-format preservation is not promised for structured data. Output stream failure may leave partial bytes; caller must discard destination.

## ADR-005: Shared bounded UTF-8 profile — 2026-11-17
Context: TXT/CSV/JSON/XML need identical safe stream staging and reporting without duplicating limits.
Options: new infrastructure package vs reusable base alongside Text.
Decision: structured format modules depend on Formats.Text's BufferedDocumentProcessor and request-local DocumentProcessingContext; Text depends only on Abstractions. Require empty destinations and explicit registered-format selection, with no binary fallback.
Rationale: one tested staging boundary without an otherwise empty package.
Consequences: memory-bounded buffering rather than constant-memory transformation; original formatting/schema/signature preservation is not promised. Reject unsupported XML mixed content and structural matches, JSON collisions and detected non-string primitives. Reports use numeric locations and field-relative UTF-16 offsets.

## ADR-006: Optional HTTP adapter and truthful completion gates — 2026-11-17
Context: a useful managed-text delivery is possible before secure binary backends exist.
Options: fake binary shells vs explicitly unsupported formats and independent managed checks.
Decision: provide optional bounded ASP.NET adapter and four executable samples; split SEC-001 managed checks from unfinished SEC-002 binary/native hardening. Track missing analyzer/policy/validator/OCR interfaces in EXT-001. Keep PKG-001 blocked until actual Git/source provenance is verifiable despite successful local package generation.
Rationale: independently tested progress without inflating project completion or security claims.
Consequences: static OpenAPI is kept with the API sample; hosts own authentication/TLS/authorization/concurrency and logging. Office/PDF/images/OCR/worker/benchmark work remains visible and the final release gate remains unpassed. No PDF dependency license or remote repository identity has been invented.

## ADR-007: Separate managed measurements from deferred binary benchmarks — 2026-11-17
Context: the managed implementation is verified but has no prior throughput/allocation evidence; unsupported binary formats cannot be benchmarked meaningfully.
Options: synthetic reimplementations, profiler-only observations, or a nonpackable BenchmarkDotNet host invoking actual public product APIs.
Decision: use centrally pinned BenchmarkDotNet 0.15.8 (MIT, restored metadata reviewed), explicit standalone composition, bounded deterministic synthetic fixtures, MemoryDiagnoser and out-of-process timing. Register host and xUnit fixture-test projects in both solutions before source creation. Keep all development dependencies outside the eleven product packages. Do not copy engine/processor algorithms into benchmarks.
Rationale: repeatable measurements exercise real detection/overlap/stream/parser/report behavior while excluding reusable setup; correctness preflight and ten xUnit cases guard fixtures. ShortRun is an initial environment-specific baseline, not a regression SLA. No production optimization is made without targeted profiling.
Consequences: PERF-001 covers executed managed scenarios; PERF-002 explicitly retains deferred Office/PDF/images/OCR measurements until their implementations and recovery checks exist. The full performance milestone and project are not complete. Inherited Visual Studio runtime controls and statistical limits are recorded in PERFORMANCE.md; local hashes are not Source Link provenance. CI runs fixture tests, not noisy timing gates. No publication authorized.

## ADR-008: Owner-approved full NuntivusRedact rename — 2026-11-17
Context: owner requested a name/namespace change and explicitly selected full scope: namespaces, projects, packages, solutions and docs.
Options: namespace/API-only changes versus complete brand/identity rename.
Decision: use NuntivusRedact as brand, root namespace and product package/assembly prefix. Canonical NuntivusRedact.slnx and preserved NuntivusRedact.Workspace.slnx replace the old entry points; root scaffold becomes NuntivusRedact.csproj. Rename branded DI/HTTP APIs while preserving domain names such as RedactionEngine, RedactAsync, strategy Redact methods, markers and /redact routes.
Rationale: one consistent identity before further implementation; no published packages require compatibility forwarding.
Consequences: intentional alpha source/package/assembly identity break, documented in MIGRATION.md. Historical benchmark measurements/logs/fingerprints keep their original provenance and are not retimed or relabeled as renamed measurements. Old package candidates are archived outside the current feed. Visual Studio may require reopening the renamed solution to discard cached old project paths. No feature or algorithm changes, publication or security-scope expansion.

## ADR-009: Established format libraries instead of custom syntax implementations — 2026-11-17
Context: owner requested a review of all format processors and library adoption wherever appropriate.
Options: retain handwritten CSV parsing/escaping; use CsvHelper; add alternative JSON/XML packages despite existing framework libraries.
Decision: use centrally pinned CsvHelper 33.1.0 in Formats.Csv only. Keep System.Text.Json, System.Xml/LINQ to XML and .NET UTF-8/stream APIs for JSON, XML and TXT. Reviewed the downloaded CsvHelper nuspec: license expression is MS-PL OR Apache-2.0; select the already approved Apache-2.0 option. Its net9.0/net8.0 dependency groups contain no additional packages. Reviewed parser source at the package-recorded commit 5dad8b8b1d8b074f8353cfd482e939db788a8927.
Rationale: delegate syntax parsing/escaping to maintained libraries without unnecessary dependencies or changes to public processor contracts. Use explicit invariant-culture comma/RFC configuration, no trimming/comments/blank-line skipping, always-quoted output and staged revalidation. A regression test demonstrated that CsvHelper accepts an unfinished escaped quote at EOF; retain a narrow quote-balance guard over library-parsed raw records to fail closed.
Consequences: only CSV consumers acquire CsvHelper. Numeric paths, headers-as-data, ragged/blank rows, CR/LF/CRLF input, resource limits, cancellation and sanitized failures remain supported. Twenty new CSV regression cases pass alongside the original suite (105 total tests). Eleven package/symbol pairs and the isolated four-format consumer pass. No binary support or publication; earlier CSV benchmark timings are historical, not measurements of the new library-backed implementation. Revalidate strict-profile tests when changing the dependency version.
