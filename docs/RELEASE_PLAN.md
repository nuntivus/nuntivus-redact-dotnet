# Release plan

No publishing is authorized. Release workflow is manual package preparation only.

## Candidate checks
- Run tools/verification/verify.ps1: restore, Release build, tests, pack; stop on any nonzero exit.
- Run tools/verification/verify.ps1 format-check.
- Run dependency audit and review transitive license/vulnerability results.
- Run samples and inspect nupkg/snupkg contents, metadata, README, LICENSE, XML docs, assembly dependencies and a fresh consumer restore/build/run.
- Initialize/configure the real Git repository with owner approval; verify correct origin, commit metadata, Source Link and reproducible symbol source retrieval before release. There is no Git repository in the initial workspace.
- Establish package baseline/API compatibility checks before a stable release.
- Verify documented support matrix and do not advertise absent binary processors.
- Complete remaining Office/PDF/OCR/binary-security/extension/performance milestones and recovery checks before claiming the complete master roadmap is delivered. The managed Web integration is implemented and tested, not a production service.
- Managed benchmark environment/results are recorded in PERFORMANCE.md (PERF-001); run controlled longer measurements before regression budgets. Office/PDF/images/OCR measurements remain PERF-002 and require real recovery-verified implementations first. These initial numbers do not complete the roadmap or authorize release.
- Confirm security reporting contact/support policy before production use.

## Approval gate
Only after all mandatory tasks are VERIFIED may REL-001 pass. Owner must explicitly approve package publication; do not add publish tokens, pushes or automatic upload-to-feed steps. Candidate packages in artifacts/packages are local, not released.
