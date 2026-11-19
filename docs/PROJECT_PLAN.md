# NuntivusRedact project roadmap

Owner-requested NLP-001 adds optional offline multilingual entity recognition contracts, detector adaptation and a local ONNX provider, with models provisioned separately. See [NLP profiles and deployment gates](nlp.md) and TASKS.md; native isolation and trained-model accuracy are not implied by synthetic inference tests.

Each milestone has a corresponding detailed task in TASKS.md. All are mandatory for full completion. Create projects only when implementing meaningful functionality. Partial delivery is not project completion.

| Milestone | Task | Deliverable |
|---|---|---|
| 1 Foundation | FND-001 | .NET 10 SDK solution, analyzers, CPM, XML docs, packaging metadata, CI, scripts, instructions |
| 2 Detection | CORE-001 | Immutable contracts, confidence, deterministic priority/overlap pipeline, cancellation/errors |
| 3 Detectors | DET-001 | Email, phone, IP, IBAN, URL, cards, bounded custom regex/keywords/dictionary |
| 4 Strategies | STR-001 | Replacement, category, full/partial masking, keyed pseudonymization, custom extension |
| 5 Policy | POL-001 | Immutable policies, builder, category/confidence/priority/custom rules, strict execution |
| 6 DI | DI-001 | Optional registration, detector/strategy/processor APIs, options and lifetime validation |
| 7 Structured formats | FMT-001 | Bounded stream TXT/CSV/JSON/XML, structure validation, format selection |
| 8 Office | OFF-001 | DOCX/XLSX/PPTX, content inventory, recovery tests, explicit rejection |
| 9 PDF | PDF-001 | License-approved backend, true removal, recovery/revision/metadata tests |
| 10 OCR/images | OCR-001 | OCR contracts, Tesseract, coordinates, pixel replacement and metadata removal |
| 11 ASP.NET | WEB-001 | Optional bounded streaming endpoint, safe errors, OpenAPI, API sample |
| 12 Security | SEC-001 / SEC-002 | Managed baseline plus unfinished binary/native/worker/recovery hardening |
| 13 Performance | PERF-001 / PERF-002 | Managed BenchmarkDotNet environment/results; binary scenarios deferred until implemented and recovery-verified |
| 14 Packaging | PKG-001 | Independent packages, symbols, Source Link, validation, release notes; no publishing |
| 15 Documentation | DOC-001 | Guides, API reference, four executable samples, supported-format matrix |
| 16 Final | REL-001 | Release restore/build/test/pack, benchmarks/security checks, audit claims |

Dependencies are specified in TASKS.md. POL-001 may be implemented alongside CORE-001 to make filtering precede overlap resolution. No black-rectangle PDF implementation is acceptable. No default project is advertised as a product package.

Cross-cutting EXT-001 tracks remaining first-class analyzer/policy/validation extension interfaces. Existing detector/strategy/processor interfaces, policy predicates and separate OCR-provider/image-redactor contracts are implemented; remaining contracts are not claimed complete. OCR-001 includes a locally verified optional PNG/JPEG/Office-picture group, while native-worker isolation remains unfinished. PKG-001 retains release provenance gates despite successful local packages and consumer checks. Full roadmap completion remains REL-001, not a restricted-profile checkpoint.
