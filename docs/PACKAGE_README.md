# NuntivusRedact

Composable detection and redaction libraries for **.NET 10**, licensed under **Apache-2.0**.

**Unreleased alpha candidate (`0.1.0-alpha.1`).** These packages are currently built and consumed locally; no public NuGet release, production deployment or support commitment is asserted. APIs may change during alpha.

This text is embedded as `README.md` in every product package. It intentionally contains no repository-relative images, badges or unconfigured repository links. The source checkout contains the complete documentation, executable samples and original artwork.

## What the library does

- Explicitly compose detectors, an immutable policy and transformation strategies without a required DI container.
- Detect personal-data candidates with documented heuristic limitations, bounded regex or dictionaries/keywords; implement custom detectors for application-specific needs.
- Apply deterministic priority/confidence/overlap rules, then fixed/category replacement, masking or keyed HMAC pseudonymization.
- Process a bounded UTF-8 TXT/CSV/JSON/XML profile with cancellation, numeric source-free reports and staged output validation.
- Optionally register extensions with Microsoft DI or host a bounded raw-body ASP.NET Core adapter.

Successful processing means configured matches were transformed. It is **not proof that all sensitive data was found**, anonymization, secure deletion of source files or erasure of managed buffers. Masking and pseudonymization have different semantics from irreversible anonymization.

## Choose only the modules you need

| Package | Purpose |
| --- | --- |
| NuntivusRedact.Abstractions | Immutable detection, strategy and document contracts |
| NuntivusRedact.Core | Engine, immutable policies and overlap resolution |
| NuntivusRedact.Detectors.Regex | Bounded custom regex and dictionary/keyword detection |
| NuntivusRedact.Detectors.PersonalData | Email, phone, IP, IBAN, URL and credit-card candidates |
| NuntivusRedact.Strategies | Replacement, masking and keyed pseudonymization |
| NuntivusRedact.Formats.Text | Plain UTF-8 text and bounded staging/reporting |
| NuntivusRedact.Formats.Csv | Strict comma-separated CSV using CsvHelper |
| NuntivusRedact.Formats.Json | System.Text.Json-based processing |
| NuntivusRedact.Formats.Xml | Hardened System.Xml/LINQ to XML processing |
| NuntivusRedact.Extensions.DependencyInjection | Optional explicit DI registration |
| NuntivusRedact.AspNetCore | Optional raw-body HTTP adapter |

Core depends on Abstractions, not on DI or ASP.NET. Format packages consume an explicitly supplied text redactor and do not hardcode detectors. The CSV dependency is isolated to the CSV package; its CsvHelper dependency is used under the Apache-2.0 license option.

## Minimal standalone example

Reference Core, Detectors.PersonalData and Strategies together. No published-feed installation is implied; configure the local candidate feed first.

```csharp
using NuntivusRedact;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Strategies;

var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build();
var engine = new RedactionEngine(
    [new DetectorRegistration(PersonalDataDetectors.Email())], policy);

var result = await engine.RedactAsync("Contact alice@example.com.");
if (result.Text != "Contact [REDACTED].")
    throw new InvalidOperationException("Synthetic example verification failed.");
Console.WriteLine($"Replacements: {result.Detections.Count}.");
```

This uses synthetic data. Do not log real source or transformed documents. Use cancellation tokens and limits appropriate to the application. Explicitly register any additional detector/strategy/processor; there is no reflection discovery or automatic unknown-format fallback.

## Supported profile and limits

- TXT: explicit UTF-8 text, with an optional UTF-8 BOM.
- CSV: comma-separated records, quoted multiline cells and doubled quotes; headers are ordinary cells. No dialect inference or spreadsheet-formula neutralization.
- JSON: string keys/values with primitive types preserved; duplicate/colliding keys and detector matches in non-string primitives fail closed.
- XML: ordinary attributes, leaf text/CDATA and comments; DTDs, processing instructions, mixed content and detected structural identifiers are rejected.
- **Office, PDF, images and OCR are unsupported.**

Default document budgets are 4 MB input, 8 MB output, depth 64, 10,000 inspected fields/nodes and 10,000 accepted detections. Text-engine limits also apply. Processing buffers bounded data in managed memory, rather than providing constant-memory streaming. Invalid UTF-8, malformed input, unsupported features and exceeded limits fail closed.

Input/output streams must be distinct and remain caller-owned; seekable destinations must start empty at position zero. Output is staged before commit, but an arbitrary destination can still fail during writing. **Discard the destination after any exception.** No original byte formatting, schema/signature preservation or complete discovery guarantee is made.

The HTTP adapter is not a production service. Applications own authentication, authorization, TLS, logging configuration, rate/concurrency limits, deployment isolation and business-schema validation. Extensions are trusted application code and must be thread safe, honor cancellation/bounds and never retain or log sensitive content.

## Development and security

The source checkout provides `docs/GETTING_STARTED.md`, `docs/README.md`, `docs/formats/structured.md`, `docs/SECURITY.md`, executable samples and verification scripts. `SECURITY.md` at the source root describes private vulnerability reporting; do not post real documents, keys or vulnerability details in public issues. The owner must establish and monitor a private reporting channel; no response SLA is promised.

The full roadmap, binary recovery/native-isolation work, repository/Source Link provenance and final release gates remain incomplete. Local tests and package creation are not hosted CI, a public release or a security certification. Package publishing requires explicit owner approval.

The included `LICENSE` file contains the Apache-2.0 terms. Third-party dependencies retain their own licenses.
