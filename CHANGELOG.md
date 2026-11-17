# Changelog

## 0.1.0-alpha.1 — Unreleased

- GitHub-facing README with original SVG logo/banner, clearly marked repository placeholders, real workflow badge endpoints, package/support tables and documentation navigation. No hosted status or deployment claim.
- New documentation index, getting-started and GitHub setup guides; expanded contributor guidance, bounded local link/SVG checks and a CI check step.
- Separate self-contained package README embedded as README.md in all eleven product packages, without GitHub-only artwork or placeholder links.
- CSV parsing/escaping now uses centrally pinned CsvHelper 33.1.0 under its Apache-2.0 option, with strict-profile regression coverage and an EOF quote-balance guard.
- Breaking alpha rename: NuntivusRedact root namespaces, project/assembly/package names and solution entry points; branded DI/HTTP APIs renamed consistently. No old packages were published. See docs/MIGRATION.md. Historical benchmark results retain their pre-rename provenance.

- Initial .NET 10 modular contracts, deterministic strict engine and immutable policies.
- Bounded regex/keyword/dictionary and heuristic personal-data detectors.
- Replacement, masking and HMAC-SHA256 pseudonymization strategies.
- Optional Microsoft DI, bounded UTF-8 TXT/CSV/JSON/XML profile and numeric source-free reports.
- Optional bounded ASP.NET raw-body integration with sanitized errors and a local OpenAPI sample; no production service claim.
- xUnit checks, executable synthetic samples, verification scripts and CI.
- Office/PDF/images/OCR are unsupported. See the [backlog](docs/TASKS.md) for unfinished milestones. No NuGet publication performed.
