# GitHub setup and open-source launch checklist

[Documentation](README.md) · [Contributing](../CONTRIBUTING.md) · [Release approval](RELEASE_PLAN.md)

This guide prepares the repository for owner-controlled GitHub hosting. It does **not** create a remote, push commits, publish packages, deploy a service or claim a hosted workflow has passed. The owner selected clearly marked placeholders until the real repository URL is known.

## 1. Set the repository identity

1. Choose the owner/organization, repository name and intended visibility. Audit the source and Git history for real personal data, credentials, private URLs and generated output before making anything public. Use synthetic fixtures only.
2. Replace the exact token **`OWNER/REPOSITORY`** in the root README badge/link URLs with the approved `organization/repository` identity, then remove the pre-import setup notice and placeholder-only alternative text. It appears in workflow badge URLs, issue/star/commit badge URLs and their links. Do not copy the parent Nuntivus repository identity unless this library genuinely lives there.
3. Keep the product name, root namespace and package prefix **NuntivusRedact**. The parent application and this library are separate projects; the supplied parent-brand screenshot is not an affiliation or deployment claim.
4. Check GitHub repository settings, default branch and the actual origin/commit metadata. Configure repository/Source Link metadata only after the owner confirms the real URL. Local package checks alone do not resolve the provenance blocker in [Tasks](TASKS.md).
5. Set the GitHub description to: `Modular .NET 10 detection and redaction libraries for UTF-8 text, CSV, JSON and XML.` Suggested topics: `dotnet`, `csharp`, `redaction`, `privacy`, `csv`, `json`, `xml`. Do not add unsupported PDF/Office/OCR support claims.

Do not put credentials in badge URLs, Markdown or workflow files. Replace placeholders before announcing the public repository. Historical logs/benchmark identities retain their provenance; do not rewrite them as new measurements.

## 2. Understand the badges

| Badge | Source | Meaning |
| --- | --- | --- |
| CI | `.github/workflows/ci.yml` | Actual GitHub workflow status for restore/build/test/pack, format, documentation and consumer checks on Windows/Linux |
| Package preparation | `.github/workflows/release.yml` | Actual status of a manually dispatched candidate-artifact build; **not** a published release or deployment |
| Dependency audit | `.github/workflows/security.yml` | Actual workflow result for NuGet restore auditing and vulnerability listing; not a security certification |
| .NET 10 | Static Shields badge | Target framework, not test status |
| Apache-2.0 | Static Shields badge | Repository license |
| Alpha | Static Shields badge | Development maturity, not production readiness |
| NuGet: not published | Static Shields badge | Present package availability; change only after approved publication actually succeeds |
| Last commit / issues / stars | Shields GitHub endpoints | Live repository metadata after identity substitution; not copied counts from the parent project |

Workflow badges use GitHub's native `actions/workflows/<filename>/badge.svg` URLs. Links open the corresponding workflow page. Repository metadata badges use `img.shields.io/github/.../OWNER/REPOSITORY`. Query parameters only select labels/styles; no hardcoded `passing` or `deployed` status is used.

Until the placeholders are replaced and hosted jobs actually run, remote badges may be unavailable, unknown or show no status. This is expected. With no branch query parameter, workflow badges follow GitHub's default-branch behavior; add a branch filter only after confirming the real branch name. The local verification evidence in [Progress](PROGRESS.md) is not evidence that GitHub Actions has executed.

External badges contact GitHub/Shields; do not include sensitive data in their URLs. If external images are unavailable, badge alternative text and local documentation still describe the project.

## 3. Enable and review existing Actions

All existing workflows declare `contents: read`; no publish token or write permission is needed for their present behavior. Review action versions, organization policies and runner availability before enabling workflows in the actual repository.

| Workflow | Trigger | Output |
| --- | --- | --- |
| [CI](../.github/workflows/ci.yml) | Push and pull request | Windows/Linux verification evidence, including locally built package candidates |
| [Prepare packages (no publishing)](../.github/workflows/release.yml) | Manual `workflow_dispatch` from the default branch | `candidate-packages` artifact containing local `.nupkg`/`.snupkg` candidates |
| [Dependency security](../.github/workflows/security.yml) | Push, pull request and weekly schedule | Restore audit and package vulnerability listing |

After import, run CI and the audit, inspect their logs/artifacts, then manually dispatch package preparation if desired. Verify the badge links against the real workflow filenames. Check each matrix job rather than assuming an aggregate green badge proves every desired platform.

Do not use `pull_request_target` or expose secrets to untrusted pull-request code. Set artifact retention and review Actions permissions in repository settings. Add required status checks in branch protection/rulesets **after** the jobs exist, using the exact names GitHub reports. Protect the default branch, require review as appropriate and restrict bypass permissions to the intended maintainers.

There is no production deployment workflow. The sample ASP.NET host is a local integration example; no cloud account, endpoint, container service or deployment badge should be invented. Production hosting needs a separate reviewed design for authentication, authorization, TLS, logging, limits, isolation and operations.

## 4. Configure community and security settings

- Keep the root [license](../LICENSE), [contribution guide](../CONTRIBUTING.md), [code of conduct](../CODE_OF_CONDUCT.md) and [security policy](../SECURITY.md) visible.
- Enable GitHub private vulnerability reporting if available. Confirm the owner-monitored private channel before promising it is operational. There is no established response SLA or support commitment; do not invent an email address.
- Review the synthetic-data-only [bug report template](../.github/ISSUE_TEMPLATE/bug_report.yml). Public issues must not contain real documents, keys or vulnerability details that belong in private reporting.
- Enable GitHub dependency graph/security features according to repository policy and review alerts. A passing dependency audit is point-in-time evidence, not proof of no vulnerabilities.
- Decide whether discussions, project boards, releases and external contact channels should be enabled. Add maintainer identities or `CODEOWNERS` only when the owner supplies the correct accounts.

## 5. Add the artwork

The root README uses a relative URL to the original [SVG banner](assets/nuntivus-redact-banner.svg); the [SVG logo](assets/nuntivus-redact-logo.svg) is available separately. Both work without a hosted CDN. Review them in GitHub's light and dark themes and at narrow widths after import.

For **Settings → General → Social preview**, export a PNG or JPEG with a 1280 × 640 canvas and safe padding, then upload it manually. The SVG banner is 1280 × 480; center it on a matching dark background rather than stretching it. GitHub's social-preview uploader does not accept SVG. See [asset guidance](assets/README.md) for palette, accessibility and licensing. A repository avatar or organization avatar is a separate owner-managed setting.

No raster export, upload, social-card preview, trademark registration or parent-project endorsement is claimed by the local SVG source files.

## 6. Verify before announcing

From the repository root with the pinned SDK and PowerShell 7:

```powershell
pwsh -File ./tools/verification/docs-check.ps1 -RequireRepositoryIdentity
pwsh -File ./tools/verification/verify.ps1
pwsh -File ./tools/verification/verify.ps1 -Task format-check
pwsh -File ./tools/verification/package-smoke.ps1
```

The first command intentionally fails while root README badge/link placeholders remain. Without `-RequireRepositoryIdentity`, it allows the selected pre-import placeholders and reports them. The checker validates local Markdown/HTML link targets and self-contained SVG structure; it does not fetch remote links, prove hosted workflow success or render artwork visually.

The GitHub README is separate from the self-contained [package README source](PACKAGE_README.md), which is embedded as `README.md` in all product packages. Do not embed repository-relative artwork or unconfigured remote badges into NuGet packages. Inspect the built package README as part of the consumer smoke check.

## 7. Keep releases and deployment honest

The version is an **unreleased `0.1.0-alpha.1` candidate**, not a public release. The manual workflow only uploads candidate artifacts to its Actions run. Never replace that fact with a fake NuGet version/download badge, GitHub release badge or deployment claim.

Complete the [release checklist](RELEASE_PLAN.md), provenance work and explicit owner approval before any package publishing or GitHub release. Update changelog, package availability text and badges only when their claims become true. Office/PDF/images/OCR and the full roadmap remain unfinished; an attractive README does not complete them.
