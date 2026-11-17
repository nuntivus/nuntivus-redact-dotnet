# Supported structured-text profile

All processors require explicit registration, distinct caller-owned input/output streams, UTF-8 input (optional UTF-8 BOM), and a fresh empty destination. They buffer bounded input and stage output in managed memory before writing. There are no temporary files. Defaults: 4 MB input, 8 MB output, depth 64, 10,000 inspected fields/structural nodes, 10,000 accepted detections. Text engine limits also apply to each semantic field. Invalid UTF-8 is rejected. Streams stay open. Any exception means discard destination; a failed write cannot be atomic for arbitrary streams. Buffers/managed strings are not securely erased.

## TXT
`TextDocumentProcessor` redacts the complete decoded text. It is not a fallback for unknown/binary formats. Numeric paths are `/`; coordinates are original UTF-16 offsets.

## CSV
`CsvDocumentProcessor` accepts comma separators, doubled quotes, LF/CRLF/CR records and quoted multiline fields. It processes headers and data cells identically, preserving cell values but normalizing quoting and row endings. Other dialects are not guessed. Malformed quote sequences fail. Ragged rows are allowed. Paths are numeric `/rows/{i}/cells/{j}`. Spreadsheet formula injection is not sanitized: do not open untrusted CSV as a spreadsheet without a separate formula-safety policy.

Parsing and escaping use centrally pinned CsvHelper 33.1.0 under its Apache-2.0 license option. Request-local parser/writer instances use invariant culture, explicit comma delimiters, no trimming, no comment handling and no blank-record skipping. Every output cell is quoted; record separators normalize to CRLF without an added terminal separator. Empty input remains empty. A narrow raw-record quote-balance check rejects an unterminated escaped-quote case accepted by this CsvHelper version. Bad-data errors are sanitized, and output is reparsed before commit. No dialect inference, object mapping or spreadsheet formula rewriting is enabled.

## JSON
`JsonDocumentProcessor` processes decoded string keys and string values, preserves raw numbers and primitive types, and reparses output. Original duplicate names or collisions caused by key redaction fail. A detector match in a number, boolean or null token fails with UnsupportedFormat rather than silently retaining it or changing its type. Structural nodes also consume the field/node budget. String escapes and formatting normalize. Schema validity is not guaranteed after key/value redaction; business schema validation remains the caller's responsibility. Paths use numeric property/item indices, never sensitive property names. Comments/trailing commas are rejected. Numeric source locations are field-relative; no original-byte offset mapping is claimed.

## XML
`XmlDocumentProcessor` prohibits DTDs/external resolution and all processing instructions. Element/attribute names, namespace URIs and reserved XML attributes are inspected but cannot be renamed: a detection in them causes UnsupportedFormat. Ordinary attributes, leaf text/CDATA (coalesced), and comments are processed. Mixed-content elements containing child elements and non-whitespace direct text are rejected pending cross-run mapping support. Separate semantic leaf fields are not concatenated across elements. Output is reparsed; declarations are removed because output is UTF-8 without BOM. Namespace structure is preserved. Whitespace-only container text is retained. Paths use numeric node/attribute indices, not source names. XML signatures, application schemas and business meaning are not preserved/validated.

## Selection and unsupported formats
`DocumentProcessorRegistry.Get(format)` selects an explicitly registered format identifier, case-insensitively. Applications must supply an authoritative format; filename/MIME hints alone are not validation. There is deliberately no binary signature guessing or unknown-to-text fallback. Office, PDF, images and OCR have no processor and are unsupported. Encoded/obfuscated/fragmented sensitive data can evade detectors; output is not proof of total discovery or anonymization.

## Shared plumbing
Structured format packages depend on Formats.Text for bounded UTF-8 staging and source-free field reporting; Text itself depends only on Abstractions. This avoids duplicating the stream security boundary without introducing an empty infrastructure package. Custom processors may derive from BufferedDocumentProcessor and must preserve its staging/limits contract.

All implemented formats use established format/encoding libraries: CSV uses CsvHelper, JSON uses the built-in `System.Text.Json` DOM/writer, XML uses `System.Xml` and LINQ to XML with hardened reader settings, and TXT uses .NET UTF-8/stream APIs. Only Formats.Csv adds a new external dependency; text-only, JSON-only and XML-only consumers do not acquire CsvHelper. Redaction traversal, numeric locations and security limits remain application responsibilities rather than format-library guarantees.
