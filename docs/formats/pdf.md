# PDF development profile

PDF redaction is **not supported yet**. `Pdf.Abstractions` and `Pdf.PdfiumCore` implement low-level extraction/rendering/fresh-raster writing; optional `Pdf.PdfPig` implements limited managed structural observations. Neither output mode is advertised as secure or complete.

## Experimental redaction starter (not supported format processing)

The nonpackable `NuntivusRedact.Pdf.Experimental` module exposes `ExperimentalPdfRedactor(IPdfBackend, IEditAwareTextRedactor)`. Explicitly call `RedactAsync(input, new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized }, manualRegions, cancellationToken)` with trusted synthetic/development input. It is not an IDocumentProcessor, is never registered by DI/HTTP format selection, and has no NuGet support claim. Do not use it as a security admission gate for hostile documents.

Both output modes are explicit: Rasterized maps accepted native-text spans to conservative glyph rectangles, permanently overwrites opaque pixels with black (one-pixel outward padding) and writes fresh image-only pages. Replacement strings are not painted; accepted match pixels are removed. NativePreserving requires optional IPdfNativeObjectBackend; PdfiumBackend implements experimental whole-object removal. It removes entire intersecting top-level text/image objects, regenerates page content and imports pages into a fresh document, never saving the original document as output. Unmatched native text/images/paths remain native. A match can remove extra nonmatching text in the same text object; manual regions can remove an entire image. No silent fallback or cosmetic native overlay.

Native restrictions: annotations and nested Form XObjects/shadings/unknown object types are rejected; intersecting paths are rejected rather than pretending to redact them. Direct image text detection still requires OCR/manual regions. Native output is staged and reopened with equal page geometry and no remaining configured native-text matches. This validation reruns the caller's text redactor; implementations must be deterministic/reusable. Fixture tests check raw/Flate removed-text/metadata recovery and surviving image objects/selectable text. Fresh import drops fixture document metadata, but does not prove full page-resource sanitization or arbitrary historical/hidden-content removal. General NativeRemoval capability remains false; the optional interface is experimental, not a secure support certificate.

Manual PdfManualRegion uses zero-based page indexes and displayed top-left-origin PDF-point rectangles. Rectangles/spans/UTF-16 coverage, dimensions, limits and edit-to-text reconstruction are validated; missing accepted geometry aborts. Defaults:144 DPI (valid1–600),10,000 matches,10,000 manual regions plus PdfBackendLimits. Pages/output remain staged; output reopens with equal page sizes, empty text layer and exact rendered pixel equality before returning bytes. The same backend performs validation, not an independent recovery certificate. Expected rasters are retained within aggregate pixel limits, so memory usage includes multiple pixel copies.

OCR/image text detection, logical reading-order reconstruction, complete preflight/hidden-content coverage, exact glyph/image-subregion native removal, independent-engine output recovery and isolation are **not implemented**. Scanned/image text is not automatically detected; caller-selected regions can remove it but cannot prove discovery completeness. Returned bytes remain potentially sensitive. PDF remains officially unsupported.

## Low-level adapter behavior

- `IPdfBackend` owns no native handles publicly.
- `OpenAsync` copies bounded input to disposable sessions.
- `ReadPageAsync` returns sensitive original UTF-16 characters and conservative transformed bounds at approximately 0.001 point precision in displayed top-left-origin space.
- PDFium extraction order is not a logical reading-order guarantee; missing bounds are explicit and future mapping must fail closed.
- `RenderPageAsync` returns copied opaque BGRA with pixel limits.
- Fresh raster writer enforces pages/aggregate pixels/encoded output and never copies source objects.
- `PdfiumRuntime` caller supplies one shared instance per process/no other PDFium users; bounds outstanding calls/open documents, serializes native operations, cleanup bypasses admission, dispose sessions/writers first.
- `NativeRemoval=false`; not a secure processor/preflight.

## Optional managed structural inspection

`IPdfStructuralInspector` is independent of `IPdfBackend`; explicitly supply `PdfPigStructuralInspector` and immutable `PdfInspectionLimits`. Call `InspectAsync(input, limits, cancellationToken)` to receive a source-free `PdfStructuralInventory`, never an admission certificate. This adapter uses centrally pinned PdfPig 0.1.16 and requires no PDFium runtime.

The current profile resolves current file-resident and supported compressed indirect objects from classic tables or bounded Flate cross-reference streams with matching declared references/locations, observes dictionaries and counts encoded opaque streams. It reports known metadata, annotations, forms, attachments, actions, signatures, optional content, form XObjects, image masks, embedded fonts, images and unknown object types. Reachability starts from current catalog and document-information roots; other trailer extension roots are not covered. Multiple revisions are reported, but historical/free objects and raw unreachable bytes are not completely inventoried. Missing references, parser failures and invalid offsets abort.

No page/image/font/metadata stream payload is decoded or searched. `OpaqueStreams` is explicit; absence of other flags does **not** prove safe content. Direct FlateDecode (or Fl) cross-reference streams support Predictor 1 (none) and PNG predictors 10–15 with Colors=1, BitsPerComponent=8 and Columns equal to the xref row width. DecodeParms may be a direct dictionary or a single-element array; null means no predictor. Unknown/indirect parameters and other pixel layouts remain refused. PNG rows require filter tags 0–4; fixed predictors require the matching tag, while predictor 15 may mix filters. Both inflated tagged rows and reconstructed rows charge the cumulative decoded budget. Cross-reference rows must have exactly the declared byte length; widths and declared row counts are checked before parser enumeration. `CrossReferenceStreams` and `DecodedStructuralStreams` record the distinction without certifying admission. Encryption, hybrid xrefs, unfiltered structural streams, other filter pipelines and TIFF predictors remain unsupported. A requested unsupported decoder is refused, never replaced with a permissive fallback. A future supported-profile gate must reject or validate every unresolved feature before secure orchestration.

Direct-Flate object streams with no predictor parameters or Extends are supported under the same decoded budgets. Before PdfPig parses embedded tokens, /N is bounded by MaximumObjects and half MaximumTokens; /First is bounded by the per-stream decoded limit. Headers contain exactly N unsigned object-number/offset pairs with whitespace only, unique nonzero numbers, first offset zero and strictly increasing in-range offsets. Every header member must have a current generation-zero compressed xref entry with the exact container/index, and every compressed entry must match its header. Unfiltered streams, indirect filter/count declarations, superseded or unindexed members, noncanonical headers and other object-stream features fail closed. ObjectStreams reports this syntax; embedded token parsing remains delegated to PdfPig with its nesting bound and is not complete secure admission or hard CPU/memory containment.

Default limits: input 16 MiB, cumulative reads 128 MiB, one million stream operations, 10,000 current objects, 200,000 token visits, depth 64, 16 cross-reference sections, 100 declared pages, encoded stream 16 MiB and aggregate encoded streams 32 MiB. MaximumDecodedStructuralStreamBytes defaults to 4 MiB; MaximumTotalDecodedStructuralStreamBytes defaults to 16 MiB including repeated decoding. Decoding checks bounds before retaining each chunk. Requested cancellation is checked at I/O/traversal/decoding boundaries. These limits do not impose hard total parser CPU/memory deadlines; hostile-input containment still requires external isolation. Exceptions expose safe codes without parser source-bearing inner exceptions.

PdfPig declares Apache-2.0 at source revision `a7bb35662bbbf405efddad50aedc9bcdcf515afc`. Reviewed archive SHA-256: `D67171846EA8C28F50359137065FEC4514266D7A32B23EAE6C5F2EBED8FFCFC4`. The adapter packages upstream attribution and supplemental PDFBox/FontBox/Adobe terms in `NOTICE.txt` and `THIRD-PARTY-LICENSES.txt`, alongside the Apache license. This review does not close PDFium native redistribution/provenance gates.

## Intended modes

- `Rasterized`: render bounded pages, combine native text detection with OCR and caller-selected regions, permanently overwrite selected pixels, and write a fresh image-only PDF. Preserve supported page geometry and visual layout at the configured resolution; selectable source text, vector graphics and accessibility are not retained.
- `NativePreserving`: permanently remove supported text/image content while retaining unaffected selectable text and vector positioning. This mode is gated on inventory, sanitization, coordinate mapping and recovery checks. It must never silently fall back to rasterized output.

PDFiumCore belongs only behind a backend-neutral adapter. Core, detectors, OCR and public neutral contracts must not reference its generated types. Alternative backends must satisfy the same capability and conformance tests; equivalent behavior is not assumed automatically.

## Executed feasibility

On 2026-11-20, .NET SDK 10.0.401 / Windows x64, the nonpackable `NuntivusRedact.Pdf.Tests` project passed **10/10 tests, zero skipped** against PDFiumCore 157.0.8100:

- Load synthetic PDF text and page geometry for four page rotations.
- Obtain native character bounds and render embedded text/image content.
- Delete whole text/image objects and reopen fresh serialized output.
- Demonstrate that nonincremental saving **retains hidden metadata** even after visible content deletion. This is a characterization of an unsafe shortcut, not a redaction feature.
- Build a separate document from manually cleaned opaque pixels; verify dimensions, pixel equality after reopening, no selectable text, and absence of fixture secrets from raw/Flate-decoded streams.

The probe intentionally handles trusted synthetic fixtures only. Its stream recovery scanner is a test utility, not a general PDF parser or production security validator. Whole-object deletion is not glyph-level redaction; visible image removal is not proof of underlying image-resource removal. No OCR accuracy, arbitrary PDF fidelity, independent-engine recovery, resource isolation or end-to-end redaction claim is established.

Repeat:

`dotnet test tests/NuntivusRedact.Pdf.Tests/NuntivusRedact.Pdf.Tests.csproj -c Release --logger "trx;LogFileName=pdf-feasibility.trx" --results-directory artifacts/test-results -tl:off`

Evidence: `artifacts/pdf-feasibility.log`, `artifacts/test-results/pdf-feasibility.trx`.

## Foundation verification

Current managed-inspection increment: Release restore/build/test/pack passed **314/314 tests, zero skipped**, including **47 PDF**; IDE PDF/architecture **51/51**. All 21 package/symbol inspections, bundled parser notices and isolated managed/native PDF consumer ordinary/published output passed. Logs `artifacts/pdf-inspection-verification.log`, `artifacts/pdf-inspection-packages.log`. Workspace Release build, scoped formatting and parser vulnerability audit passed. These checks do not establish secure format support.

Final Release verification passed 293/293 tests zero skipped (including 26 PDF), 20 local package/symbol pairs and isolated low-level Windows x64 PDF NuGet consumer normal/published output; logs `artifacts/pdf-verification-final.log`, `artifacts/pdf-packages-final.log`.

See [Synthetic package consumer example](../../tools/verification/consumer/PdfProgram.cs.fixture); it uses the repository's trusted synthetic PdfFixtures helper and demonstrates low-level extraction/rendering/fresh raster writing, not detection or redaction.

## Remaining work

Complete supported-profile preflight beyond current structural observations; crop/font/image-feature profiles; mapping/OCR/manual regions/orchestration/native sanitization/staged validation/DI/examples; Linux/native attribution/isolation and independent recovery.

Linux execution is unverified: Docker Linux daemon was unavailable and Ubuntu WSL had no `dotnet` command. Repeat the test command inside a provisioned Linux x64 .NET 10 environment before claiming that deployment profile. In-process native parsing is not sandboxed; managed cancellation cannot promise hard native deadlines or memory bounds.
