# Office DOCX and XLSX

## Implementation profile

This feature is IN PROGRESS until the executed verification evidence in PROGRESS.md is recorded. It is not support for every Office document. DOC, XLS, DOCM, XLSM, PPTX, encrypted packages and signed packages are outside this profile.

The independent `NuntivusRedact.Formats.Office` package uses Microsoft's MIT-licensed Open XML SDK 3.5.1 and framework ZIP/XML APIs. Processors consume an `IEditAwareTextRedactor`, not Core or DI. They never automate Microsoft Office, fetch external content, flatten documents to text or modify input in place.

This is a strict Transitional OOXML (2006 namespaces) profile. Strict OOXML namespaces, markup-compatibility/unknown namespace declarations (even unused declarations), alternate content and vendor extensions are rejected. Many real-world modern Office documents therefore require a future expanded profile. ZIP part names are bounded ASCII paths without percent escapes, spaces, traversal or case aliases; structural detections can also reject otherwise valid documents. Do not equate the file extension with supported content.

## Supported-surface matrix

| Surface | Processing policy |
| --- | --- |
| DOCX paragraphs, runs, tables, headers, footers, footnotes, endnotes, comments | Redact logical paragraph text across formatting runs; retain unaffected XML and formatting. Separate paragraphs/cells and tabs/breaks are not concatenated. |
| DOCX hidden runs | Process regardless of visibility. |
| DOCX tracked insertions/deletions/moves | Reject: current/historical projections need separate verified mapping; neither is silently retained as securely processed. |
| DOCX styles, numbering, page/section layout | Preserve supported typed XML; reject detected sensitive structural values rather than changing semantics. |
| DOCX fields, data bindings, altChunk, custom XML, drawings, text boxes, SmartArt | Reject in this profile. These can carry alternate, cached, linked or image content. |
| XLSX shared strings, inline strings, rich text | Decode ST_Xstring `_xNNNN_` escapes once before detection, including unused strings; re-encode literal escape syntax and retain unaffected run properties. Logical strings are limited to 32,767 UTF-16 code units. |
| XLSX visible, hidden and very-hidden worksheets | Process all parts; visibility does not bypass redaction. |
| XLSX numeric, date, boolean, error cells | Preserve unchanged with supported built-in formats; reject detected sensitive values when replacement would change type. Custom number formats are rejected because quoted display fragments require separate semantic recovery checks. |
| XLSX simple arithmetic/SUM/AVERAGE/MIN/MAX/COUNT formulas using local A1 references | Preserve only the explicitly validated formula grammar. Remove cached results and calculation chains; request full recalculation. No formula evaluation is provided. |
| XLSX other formulas, defined names, external links, connections, pivot/chart caches, drawings, headers/footers, conditional formatting, data validation and phonetic text | Reject. Sensitive formulas are never rewritten as ordinary strings. |
| XLSX legacy comments/notes | Process rich text and authors; VML drawings remain unsupported, so packages containing VML are rejected. |
| Core, extended and custom document properties | Redact supported string values; reject detected typed/structural values, opaque binary variants and escape-like `_xNNNN_` source/replacement data in these unverified contexts. No claim that all identifying metadata is automatically removed. |
| XML comments and processing instructions | Reject rather than preserve an unprocessed alternate surface. DTDs/entities are prohibited. |
| External relationships, signatures, macros, ActiveX, unknown parts/extensions | Reject; do not strip signatures or active content silently. |
| Embedded DOCX/XLSX packages | Recursively process recognized package relationships with shared aggregate size/entry/detection/field/depth limits. This does not enable OLE or drawing-based visual embedding; those containers remain rejected. |
| OLE objects, arbitrary attachments, media/images | Reject. Images require a separate verified OCR/pixel-redaction adapter, not pass-through. |

## Formatting semantics

Detections use original UTF-16 offsets. Replacements inherit the first affected text run's properties; unaffected text retains its run properties. Length-changing edits can alter wrapping, pagination and formula results. Pixel-identical rendering, byte-identical XML, recalculated formulas and arbitrary embedded-object fidelity are not promised. A valid Open XML schema alone does not establish Word/Excel rendering fidelity.
For supported XLSX strings, offsets refer to original decoded logical text, not the physical `_xNNNN_` XML spelling. Literal `_x005F_x0040_` decodes once to literal `_x0040_`, not to `@`. Unsupported encoded controls/unpaired surrogates fail closed; escape forms in Word text, metadata and structural values are rejected rather than guessed. XML carriage returns are entitized on output to avoid newline normalization changing semantic text. Unknown extension payloads are rejected even inside otherwise supported parts.

## Security and staging

Inventory every ZIP entry and relationship before SDK loading. Enforce input/output bytes, inflated entry/aggregate bytes, entry count, compression ratio, XML depth/characters/nodes, logical text fields, detections and nested-package depth. Reject duplicate/ambiguous names, path traversal, orphan parts and unsupported content types. Rebuild output into a fresh bounded ZIP, reopen it with the SDK and validate before committing it to the caller's empty destination. Caller-owned streams stay open. Any output-I/O failure requires discarding the destination; no atomicity is promised for arbitrary streams.

Reports contain only numeric part/field paths and source-free detections, never part names, field values, replacement strings or source text. Edit-aware results contain sensitive transient replacement data and must not be logged. Successful processing means accepted detector matches were replaced within the supported profile, not that every sensitive value was discovered.

No native parser is used. Managed parsing still has CPU/memory risks: hosts must limit concurrent requests and use an isolated worker with external time/memory limits for hostile workloads. This library does not claim to implement that deployment boundary.

## Construction and explicit registration

Reference Formats.Office, Core, a detector package and Strategies. Core's RedactionEngine implements the optional edit-aware interface; no DI is required. With an existing engine, caller-owned streams and cancellation token:

```csharp
var limits = new NuntivusRedact.Formats.Office.OfficeLimits
{
    MaximumInputBytes = 8 * 1024 * 1024,
    MaximumNestingDepth = 2
};
var processor = new NuntivusRedact.Formats.Office.DocxDocumentProcessor(engine, limits);
var report = await processor.ProcessAsync(input, output, cancellationToken);
```

Use XlsxDocumentProcessor for XLSX. The compiling executable DI example is [the DI sample](../../samples/NuntivusRedact.Sample.DependencyInjection/Program.cs). Registration is explicit:

```csharp
services.AddNuntivusRedact(policy)
    .AddDocumentProcessor<DocxDocumentProcessor>()
    .AddDocumentProcessor<XlsxDocumentProcessor>();
```

Import NuntivusRedact.Extensions.DependencyInjection and NuntivusRedact.Formats.Office; register your thread-safe detectors explicitly. DI aliases IEditAwareTextRedactor to the same ITextRedactor instance and does not replace a custom legacy redactor. Such an implementation must add the edit-aware interface or Office resolution fails. Custom edits must be ordered/nonoverlapping, not split UTF-16 surrogate pairs, and reconstruct final text exactly; invalid results fail before output. Existing text-format APIs are unchanged. The HTTP sample still enables only its four text formats; Office is not implicitly exposed through HTTP.

### Immutable OfficeLimits defaults

| Property | Default |
| --- | --- |
| MaximumInputBytes / MaximumOutputBytes | 8 MiB / 16 MiB compressed |
| MaximumInflatedBytes | 32 MiB aggregate actual input inflation; also caps serialized output and conservative replacement growth |
| MaximumEntryBytes | 8 MiB inflated/serialized per entry |
| MaximumEntries / MaximumCompressionRatio | 512 aggregate / 200 |
| MaximumXmlDepth / MaximumXmlNodes | 64 / 200,000 aggregate reader nodes |
| MaximumFields / MaximumDetections | 50,000 inspected values / 10,000 accepted detections |
| MaximumNestingDepth | 3 embedded levels below the outer package (0 rejects embedded packages) |

All budgets apply per processing call; nested packages share the aggregate counters and may conservatively count container bytes and nested bytes separately. Input/output staging and parsed XML may allocate multiple bounded copies. Text-engine limits still apply per logical field. Limits must be positive except nesting (0 through 8); XML depth is capped at 256. Configure host concurrency separately.

Formula support is deliberately limited to uppercase local A1 references (optional dollar signs), ranges, finite decimal constants, parentheses, unary/binary arithmetic and SUM/AVERAGE/MIN/MAX/COUNT calls. Formula text is limited to 8,192 characters and bounded nesting. Named, cross-sheet, external, array/shared, string-literal and dynamic formulas are rejected. Cached values are inspected and removed, not silently trusted. Recalculation changes values and may be costly in a consuming application; this library does not evaluate formulas or guarantee spreadsheet business semantics.

## Acceptance and verification

xUnit fixtures must cover split-run and variable-length edits, formatting properties, revisions, metadata, unused strings, hidden sheets, formulas/caches, nested packages, sentinel recovery from every output part, malformed ZIP/XML, unsupported features, limits, cancellation, concurrent reuse and precommit failures. SDK reopen/schema validation is automated; actual Word/Excel repair/rendering checks require an available application and must be recorded separately, never inferred.

PPTX remains deferred under OFF-001. PDF/OCR/native-worker scope remains unfinished under SEC-002. No binary benchmark or publication is authorized by this implementation.
