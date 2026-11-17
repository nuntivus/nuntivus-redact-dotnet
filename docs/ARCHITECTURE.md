# Architecture

Abstractions defines document-independent detection and redaction contracts. Core orchestrates explicitly supplied detectors and strategies with immutable policy. Detectors and Strategies depend only on Abstractions. Format packages depend on Abstractions and consume a text-redactor contract; they do not hardcode detectors. Optional DI composes Core and extensions. Optional ASP.NET builds on DI. OCR contracts are separate from native providers. Samples depend on public packages only.

Text offsets use UTF-16 code units within the supplied text. Detections carry category, span, confidence and detector identity, never source values. Validate ranges before applying edits. Filter by policy before deterministic overlap resolution (priority descending, confidence descending, length descending, start ascending, detector identity ordinal). Transform nonoverlapping spans from original text without rescanning generated replacements. Reports expose counts/locations only, not matched text. Caller-owned streams stay open. Stage bounded output before committing it; output I/O failure still requires callers to discard the destination.

Security boundary: successful text replacement means only accepted detector matches were replaced. It is not proof of anonymization or complete discovery. Structured parsers preserve semantic structure, not original byte formatting. Binary processors are unsupported until their hidden-content and recovery surfaces are verified. No plugin discovery by reflection. No logging of source content. Custom extensions are trusted application code and must follow the same contracts.

Planned package boundaries mirror the master roadmap. Office, PDF, Images, OCR, ASP.NET and benchmark projects must not be added as empty shells. Document deferrals in TASKS.md and RESUME.md.

## Implemented format and HTTP boundaries

Formats.Csv/Json/Xml depend on Formats.Text for bounded UTF-8 staging and numeric field reporting; Text depends only on Abstractions. Each processor parses/transforms/validates before committing output, rejects nonempty seekable destinations, and leaves streams open. Mixed-content XML, DTDs/PIs, sensitive structural XML identifiers and detected non-string JSON primitives fail closed. JSON original/resulting duplicate keys fail. No byte-for-byte formatting, application schema or signature preservation guarantee exists.

Format syntax is delegated to established libraries: CsvHelper in Formats.Csv only, System.Text.Json for JSON, System.Xml/LINQ to XML for XML, and framework encoding/streams for TXT. CsvHelper is centrally pinned and selected under Apache-2.0; request-local configuration preserves the strict CSV profile, with an explicit EOF quote-balance guard. Format-specific redaction traversal, budgets, cancellation, sanitized failures and staged output validation remain owned by the processors. See ADR-009 and formats/structured.md.

Extensions.DependencyInjection depends on Core and Microsoft.Extensions.Options/DependencyInjection only. It explicitly composes singleton extensions, validates required policy, and preserves Core's standalone path. Host container scope validation is required for captive dependencies. AspNetCore depends on DI/Text and the shared framework; it bounds/stages raw-body requests/responses and returns safe problem codes. Hosts own authentication, TLS, authorization, logging configuration and concurrency controls. The sample disables logging and includes static OpenAPI metadata; no production service or isolated worker is implied.

Supported format profiles and remaining binary package deferrals are in formats/README.md. Separate analyzer/policy/validation/OCR contracts remain EXT-001; do not infer them from the implemented detector/strategy/document interfaces.
