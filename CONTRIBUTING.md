# Contributing

Contributions and reproducible reports using **synthetic data** are welcome. This project is alpha; the complete roadmap is not finished and no production support SLA is offered. Read the [code of conduct](CODE_OF_CONDUCT.md) and [repository instructions](AGENTS.md) before contributing.

## Set up

1. Read the [architecture](docs/ARCHITECTURE.md), [decisions](docs/DECISIONS.md), [security model](docs/SECURITY.md) and [task backlog](docs/TASKS.md). Check existing implementation/evidence before starting an unfinished milestone.
2. Install the SDK pinned by [global.json](global.json) and PowerShell 7. Follow [Getting started](docs/GETTING_STARTED.md).
3. Use the actual GitHub repository after its owner configures it; `OWNER/REPOSITORY` badge URLs are intentionally unconfigured pre-import placeholders, not a fork URL.
4. Create a focused branch and keep changes scoped. Public contract changes, new dependencies and binary-format support need an explicit design/security discussion before implementation.

## Report bugs and propose changes

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.yml) in GitHub Issues once the repository is configured. Include SDK/OS/package versions, a minimal synthetic reproduction, expected/actual behavior and safe error codes. For documentation defects, include the affected page and broken command/link. For feature proposals, describe the use case, support boundary and compatibility impact.

**Never attach real documents, personal data, keys, private URLs or content-bearing exception logs.** Vulnerabilities belong in the private channel described in [SECURITY.md](SECURITY.md), not public issues. If no private channel is configured, request a private contact without publishing details. Do not invent a maintainer address or response commitment.

## Implementation rules

- Keep dependencies inward; Core must not acquire adapter/DI/framework dependencies. Compose and register extensions explicitly—no reflection discovery or service locator.
- Preserve NuGet/source compatibility, or document intentional alpha breaks in [CHANGELOG.md](CHANGELOG.md) and the [migration guide](docs/MIGRATION.md).
- Use nullable modern C#, documented public APIs, immutable reusable configuration, bounded asynchronous I/O and cancellation. Follow existing formatting and `.editorconfig`.
- Reuse established libraries where appropriate; justify new dependencies, centrally pin versions and review licenses/vulnerabilities.
- Do not log/retain source content, leak it through reports/exceptions, silently return incomplete processing or weaken fail-closed behavior. Use synthetic fixtures only.
- Add xUnit positive, negative, boundary, cancellation, concurrent-use and security/recovery checks as applicable. Binary formats need hidden-content/revision/metadata/attachment recovery tests before any support claim; absence is preferable to a fake secure implementation.
- Do not add empty placeholder projects, advertise unsupported formats or invent benchmark/security results. Collect measurements before performance claims or optimization.

## Verify before a pull request

Run from the root with the pinned SDK:

```powershell
pwsh -File ./tools/verification/verify.ps1
pwsh -File ./tools/verification/verify.ps1 -Task format-check
pwsh -File ./tools/verification/docs-check.ps1
pwsh -File ./tools/verification/package-smoke.ps1
```

The first script restores, builds Release, tests and packs. The POSIX alternative is `sh tools/verification/verify.sh`; the package/documentation scripts use PowerShell 7. Always name the solution in direct dotnet commands because multiple root entry points coexist. Record actual test counts and failures, not just requested test runs. A stale IDE test request that runs zero tests is not passing evidence.

For code changes, run the relevant executable samples and targeted tests too. For documentation/branding, verify relative links, inspect SVGs at narrow and wide sizes, and ensure the package README remains self-contained. The checker validates local targets and SVG structure, not remote links, hosted Actions results or visual rendering. Do not include generated `artifacts/`, package binaries, secrets or document content in a pull request.

## Pull-request checklist

- Explain the problem, scope and design choices; link the relevant issue/task when available.
- Include executed checks/results and state any platform or coverage limitations.
- Add/update public XML docs, usage examples, limits, rejection/security semantics and relevant tests.
- Update [changelog](CHANGELOG.md), [tasks](docs/TASKS.md), [progress](docs/PROGRESS.md) and [resume record](docs/RESUME.md) after each coherent unit. Mark VERIFIED only after the actual acceptance checks pass; preserve unfinished work and next commands.
- Keep README badges truthful and repository-specific. Do not hardcode a green CI/deployed badge or copy parent-project counts.
- Keep original artwork self-contained and accessible; see [asset guidance](docs/assets/README.md).
- Describe new dependencies and confirm license/security review and package compatibility.

Before a release, follow the [release checklist](docs/RELEASE_PLAN.md). **No package publication is authorized without explicit owner approval.** The current manual workflow only prepares candidate artifacts; a local green build does not complete the roadmap or deploy a production service.
