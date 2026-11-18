# NuntivusRedact documentation

[Project overview](../README.md) · [Get started](GETTING_STARTED.md) · [API reference](API_REFERENCE.md) · [Contribute](../CONTRIBUTING.md)

NuntivusRedact is an **alpha .NET 10 library**, not a complete document-sanitization service. Implemented profiles cover UTF-8 text, CSV, JSON, XML and restricted [DOCX/XLSX](formats/office.md). PPTX, PDF, images and OCR are unsupported. Successful replacement means configured matches were transformed; it is not proof of anonymization or complete discovery.

## Start here

| Goal | Guide |
| --- | --- |
| Build from source and try a working example | [Getting started](GETTING_STARTED.md) |
| Choose packages and add custom detectors, strategies or processors | [Usage, extensions and DI](EXTENSIBILITY.md) |
| Look up public contracts and operation semantics | [API reference](API_REFERENCE.md) |
| Understand format support and rejection behavior | [Format matrix](formats/README.md) and [strict structured-text profile](formats/structured.md) |
| Understand detector coverage and false positives/negatives | [Detection guide](DETECTION.md) |
| Host the optional raw-body HTTP adapter | [HTTP integration](HTTP.md) |
| Evaluate security boundaries | [Threat model and limitations](SECURITY.md) |
| Report a vulnerability privately | [Security reporting policy](../SECURITY.md) |

## Develop and extend

- [Contributing](../CONTRIBUTING.md): environment, synthetic reproductions, tests and pull-request checklist.
- [Architecture](ARCHITECTURE.md): package boundaries, explicit composition, staging and numeric reports.
- [Architecture decisions](DECISIONS.md): rationale, dependency/license choices and compatibility decisions.
- [Test matrix](TEST_MATRIX.md): categories of checks and remaining coverage boundaries.
- [Performance evidence](PERFORMANCE.md): historical managed measurements, methodology and limits; not a current throughput promise.
- [Migration notes](MIGRATION.md): intentional alpha identity changes and historical provenance.
- [Code of conduct](../CODE_OF_CONDUCT.md) and [license](../LICENSE).

## Prepare a GitHub repository

- [GitHub setup](GITHUB_SETUP.md): replace repository placeholders, enable existing workflows, configure private reporting and understand badges.
- [Original logo and banner](assets/README.md): SVG sources, palette, usage and social-preview export instructions.
- [Release checklist](RELEASE_PLAN.md): local candidate verification and the explicit owner-approval gate. The manual release workflow only prepares artifacts; it does not publish or deploy.
- [Package README source](PACKAGE_README.md): self-contained text embedded as `README.md` in locally built product packages.
- [Changelog](../CHANGELOG.md): unreleased changes; no public NuGet release is implied.

## Samples

All four executable samples use synthetic data. Run them from the repository root with the pinned SDK:

| Sample | Source | Purpose |
| --- | --- | --- |
| Standalone | [Console](../samples/NuntivusRedact.Sample.Console/Program.cs) | Process JSON with explicitly composed detectors/policy |
| Dependency injection | [DI](../samples/NuntivusRedact.Sample.DependencyInjection/Program.cs) | Optional Microsoft.Extensions.DependencyInjection registration |
| Extension | [Custom detector](../samples/NuntivusRedact.Sample.CustomDetector/Program.cs) | Implement a domain-specific detector |
| HTTP | [Web API](../samples/NuntivusRedact.Sample.WebApi/Program.cs) | Local raw-body API with OpenAPI; not a production deployment |

Commands and expected behavior are in [Getting started](GETTING_STARTED.md). Applications own authentication, authorization, TLS, logging policy, keys, concurrency and any business-schema validation.

## Roadmap and evidence

The full roadmap is **incomplete**. Keep restricted DOCX/XLSX capabilities separate from deferred PPTX, broader Office fidelity, PDF/OCR and remaining binary-security work.

| Record | Purpose |
| --- | --- |
| [Project plan](PROJECT_PLAN.md) | Milestones and intended deliverables |
| [Tasks](TASKS.md) | Dependencies, acceptance criteria and status |
| [Progress](PROGRESS.md) | Executed checks, results and known blockers |
| [Resume](RESUME.md) | Continuation context and exact verification commands |

A checked task is not stronger evidence than the actual code and executed results. Hosted CI, repository provenance, branch protection, social-preview configuration and package publication require separate verification after GitHub import.
