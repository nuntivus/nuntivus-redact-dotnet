# NuntivusRedact alpha rename

The owner approved a full rename on 2026-11-17 before further feature development. This is an intentional pre-release identity break, not a framework upgrade or redaction behavior change. No packages have been published; no compatibility facade/type forwarding is provided.

## Identity mapping

| Previous identity | Current identity |
|---|---|
| Redact.NET brand | NuntivusRedact |
| Redact root namespace | NuntivusRedact |
| Redact.* projects, assemblies, namespaces and packages | NuntivusRedact.* |
| Redact.NET.slnx | NuntivusRedact.slnx (canonical, 24 projects) |
| nuntivus-redact.slnx | NuntivusRedact.Workspace.slnx (25 projects including preserved scaffold) |
| nuntivus-redact.csproj / nuntivus_redact scaffold namespace | NuntivusRedact.csproj / NuntivusRedact |
| AddRedactNet / MapRedactNet | AddNuntivusRedact / MapNuntivusRedact |
| RedactNetBuilder / RedactOptions | NuntivusRedactBuilder / NuntivusRedactOptions |
| AddRedactHttp / RedactHttpExtensions | AddNuntivusRedactHttp / NuntivusRedactHttpExtensions |
| RedactHttpHandler / RedactHttpOptions | NuntivusRedactHttpHandler / NuntivusRedactHttpOptions |
| REDACT_PACKAGE_FEED (verification fixture environment) | NUNTIVUSREDACT_PACKAGE_FEED |

Project directories, csproj filenames, references, sample/benchmark/test paths, package consumers, CI and current documentation follow the new prefix. Package version remains 0.1.0-alpha.1 because the old candidates were never published; the package IDs themselves changed. Rebuild consumers against the new package/assembly identities rather than mixing old and new assemblies. Update imports, branded API calls, project/package references and any application configuration using assembly-qualified names.

## Intentionally unchanged

Domain operations/types (`RedactAsync`, strategy `Redact`, `RedactionEngine`, `IRedactionStrategy`, detection/policy/report contracts), `/redact/{format}` routes and their operation metadata, `[REDACTED]` markers, format/detector identifiers, input/output semantics, limits and security restrictions remain unchanged. The actual workspace root directory remains `C:/Users/salva/Documents/GitHub/nuntivus-redact`; no checkout or remote repository was moved or invented.

## Visual Studio and verification

Reopen `NuntivusRedact.Workspace.slnx` (or canonical `NuntivusRedact.slnx`) if Visual Studio retains old project paths, old test identities or the prior temporary BenchmarkSuite1 entry. Do not recreate missing old projects. Use explicit solution paths because two solutions coexist.

Run `./tools/verification/verify.ps1`, `./tools/verification/verify.ps1 format-check`, and `./tools/verification/package-smoke.ps1`. The package smoke rejects legacy product IDs/dependencies and verifies renamed assembly filenames, public consumer composition and all four supported formats. Do not use prior generated bin/obj or old package candidates as evidence of the renamed build. Scripts create new package-only consumer directories with a nuntivusredact prefix.

Old local package candidates are preserved under ignored `artifacts/legacy-redact-packages-*`, outside `artifacts/packages`. Historical logs, TRX files, benchmark reports and temporary consumer paths retain their original identities and dates; they are not current rename verification. PERFORMANCE.md labels recorded timings and hashes as pre-rename. No timing is rerun for this rename, and no new performance equivalence, optimization or expanded format-security claim is made. Current executed rename results are recorded in PROGRESS.md and RESUME.md.

The broader roadmap is still incomplete: Office/PDF/images/OCR, binary/native hardening, remaining extension interfaces, binary benchmarks, source provenance and the final release gate are unchanged. Never publish without explicit owner approval.
