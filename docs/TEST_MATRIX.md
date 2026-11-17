# Test matrix

| Project | Coverage |
|---|---|
| NuntivusRedact.Core.Tests | spans, confidence, deterministic priority/overlap, policy snapshot/filtering, limits, cancellation, surrogate safety, source-free failures, concurrency |
| NuntivusRedact.Detectors.Tests | positive/negative heuristics, IPv4/IPv6, IBAN mod97, card Luhn/whitespace, dictionary copy/boundaries, regex timeout/input/candidate bounds and safe failures |
| NuntivusRedact.Strategies.Tests | fixed/category/full/partial masks, grapheme handling, keyed deterministic/category-separated pseudonyms, per-category policy and no rescanning |
| NuntivusRedact.Formats.Tests | CSV multiline/quotes/empty fields, JSON names/strings/numbers/collisions, XML attributes/comments/CDATA/XXE/mixed-content, invalid UTF-8, depth/size/fields, cancellation, empty-output and stream ownership |
| NuntivusRedact.Integration.Tests | optional DI equivalence/idempotence, options/lifetimes; bounded HTTP declared/actual size, malformed JSON 422, safe errors/headers, cancellation and multipart rejection |
| NuntivusRedact.Security.Tests | malicious XML, encoded JSON recovery, strategy/I/O leakage, staged output and concurrency/report bounds |
| NuntivusRedact.Architecture.Tests | allowed inward product assembly references and no Microsoft.Extensions dependency in Core |
| NuntivusRedact.Benchmarks.Tests | ten deterministic text/document parameter cases: setup output/recovery checks, accepted counts and repeated reuse of real benchmark entry points; no timing assertions |

Source-free reports expose only configured category/ID, numeric structural path and field-relative UTF-16 coordinates. Do not use real PII fixtures.

BRAND-001 rename verification (2026-11-17): 85/85 tests passed, zero skipped, under NuntivusRedact identities via executed CLI/TRX. Both renamed solutions build; all fixture preflights, four sample smokes, format check, strict audit and eleven-package isolated consumer pass. Evidence is artifacts/brand001-*.log; details in PROGRESS.md. IDE still caches old paths and needs reopening the renamed solution; its empty new-name discovery is not test evidence. Benchmark timing was not rerun.

Executed results live in PROGRESS.md and artifacts/test-results/*.trx. Managed checkpoint: 75/75 original tests and four sample smokes passed (including live Kestrel). PERF-001 adds ten fixture cases: 85/85 tests passed; all ten BenchmarkDotNet ShortRun cases executed separately, with actual environment/results and limits in PERFORMANCE.md. Eleven package/symbol pairs and isolated consumer were rechecked. Visual Studio discovered the ten fixture tests but IDE run requests reported zero executed; use CLI/TRX evidence, not those requests. Binary recovery/benchmarks, native isolation, cross-platform hosted CI execution and Source Link retrieval are not implied. Unimplemented milestones remain in TASKS.md.
