# NuntivusRedact agent instructions

## Session startup
Before changing code, read AGENTS.md, docs/PROJECT_PLAN.md, docs/TASKS.md, docs/PROGRESS.md, docs/ARCHITECTURE.md, docs/DECISIONS.md, and docs/RESUME.md. Inspect actual code and test results; reconcile tracking before choosing the next dependency-ready task.

## Goals and architecture
Use NuntivusRedact as the product name, root namespace and package/project prefix. Keep domain terms such as RedactionEngine and RedactAsync; old identities belong only in explicitly historical evidence or migration notes.
Build a modular, extensible, security-sensitive .NET 10 document redaction library. Abstractions has no product dependencies. Core depends only on Abstractions. Detectors, strategies, document processors, OCR adapters, DI, and ASP.NET integrations depend inward, never the reverse. Composition and explicit registration; no reflection plugin discovery, service locator, generic repository, global mutable state, or unnecessary framework.

## Coding and compatibility
Use modern C#, nullable references, SDK projects, central package management, XML documentation on public APIs, immutable reusable configuration, cancellation, bounded asynchronous I/O, thread-safe reusable services, Logging and Options only when useful. Preserve package independence and source compatibility or document breaking changes. Use existing libraries; justify new dependencies and review licenses. Apache-2.0 was approved by the owner. Never publish without explicit approval.

## Security
No document content in logs, reports, exception messages, or telemetry. Distinguish detection, masking, pseudonymization, and irreversible removal. Never claim all sensitive data was found. Fail closed on unsupported features, malformed input, resource limits, or failed output validation. Never return partial output as secure. Bound regex execution and input size; prohibit XML external entities and DTDs. Binary formats require metadata/revision/attachment/recovery tests before support claims. Untrusted native parsing requires isolation assessment.

## Tests and documentation
Use xUnit. Cover malformed/adversarial inputs, cancellation, overlap, deterministic behavior, concurrent use, and recovery paths. Build affected projects, discover and run tests, and record exact results. Benchmark only measured work; no invented performance claims. Document every advertised public feature, its limits, security semantics, and extension example. Keep samples compiling.

## Progress and completion
Update docs/TASKS.md, PROGRESS.md, and RESUME.md after each coherent unit. Statuses: NOT STARTED, IN PROGRESS, BLOCKED, COMPLETED, VERIFIED. VERIFIED requires implementation, public docs, passing build/tests, checked acceptance criteria, and no known blocker. No empty projects or placeholder implementations to inflate progress. Keep all requested but unfinished projects explicitly deferred. Preserve exact commands, results, blockers, relevant files, and next actions before interruption. Repository state is authoritative, not checkboxes.
