# API reference

XML documentation is built into each library and included in NuGet candidates. Public types use namespace `NuntivusRedact`, with implementation namespaces matching their packages.

| Contract/type | Purpose |
|---|---|
| DetectionSpan | Validated nonempty UTF-16 range; default struct is rejected by Detection |
| Detection | Immutable source-free category/confidence/detector/span |
| DetectionContext | Sensitive original segment; do not retain/log |
| ISensitiveDataDetector | Thread-safe cancellable candidate extension |
| IRedactionStrategy | Synchronous trusted transformation of an accepted source span |
| ITextRedactor / RedactionResult | Cancellable text execution and sensitive output with source-free matches |
| IEditAwareTextRedactor / RedactionEdit / EditRedactionResult | Optional single-pass original-coordinate replacements and final text; sensitive transient results, never reports/logs |
| RedactionException / RedactionError | Sanitized errors without inner exceptions |
| IDocumentProcessor | Bounded staged processing of distinct caller-owned streams |
| DocumentDetection / DocumentRedactionResult | Numeric structural location and field-relative span reports |
| DetectorRegistration / RedactionEngine | Explicit composition, strict immutable configuration |
| RedactionPolicyBuilder / RedactionPolicy | Validated snapshot with category/rule/priority/strategy filters |
| RegexDetector / DictionaryDetector | Bounded custom regex and keyword/dictionary extensions |
| PersonalDataDetectors | Seven documented heuristic candidate detectors |
| IEntityRecognitionProvider / RecognizedEntity | Replaceable local NLP with source-free original UTF-16 entity spans |
| NlpDetector / NlpDetectorOptions / NlpDetectionCategories | Optional detector adapter, immutable fixed-label mapping and budgets |
| OnnxEntityRecognitionProvider / OnnxNerOptions | Disposable local CPU WordPiece/BIO inference with hash-verified assets, bounded windows and admission |
| FixedReplacementStrategy / CategoryReplacementStrategy | Fixed and category-based replacements |
| FullMaskingStrategy / PartialMaskingStrategy | Grapheme-aware masks, with intentional disclosure semantics |
| HmacPseudonymizationStrategy | Category-separated deterministic keyed pseudonyms |
| DocumentLimits / BufferedDocumentProcessor | Shared bounded UTF-8 staging and extension base |
| DocumentProcessingContext | Per-document field and report budgets |
| TextDocumentProcessor / CsvDocumentProcessor / JsonDocumentProcessor / XmlDocumentProcessor | Supported structured-text profiles |
| DocxDocumentProcessor / XlsxDocumentProcessor / OfficeLimits | Restricted binary Office profiles, edit-aware dependency and immutable aggregate archive/XML budgets |
| IOcrProvider / OcrResult / OcrWord / OcrBounds | Replaceable recognition, sensitive UTF-16 text and validated pixel word rectangles |
| IImageRedactor / ImageRedactionResult | Optional raster pipeline and rebuilt PNG with source-free detections |
| RasterImageRedactor / ImageLimits | Bounded PNG/JPEG word-region pixel replacement and metadata-free PNG encoding |
| TesseractOcrProvider / TesseractOptions | Optional in-process .NET Tesseract adapter with trusted host-supplied language data |
| DocumentProcessorRegistry | Explicit registered-format selection without unsafe fallback |
| IPdfBackend / IPdfDocumentSession / IPdfRasterWriter | Replaceable bounded low-level PDF sessions and fresh image-only writing; no secure processor |
| PdfPageText / PdfTextCharacter / PdfPageSize / PdfQuadrilateral | Sensitive UTF-16 character data with displayed top-left-origin PDF-point geometry |
| PdfRaster / PdfBackendLimits / PdfBackendCapabilities / PdfOutputMode | Opaque sensitive BGRA, immutable limits and explicit planned fidelity modes |
| PdfiumBackend / PdfiumRuntime | Optional serialized caller-owned native runtime; NativeRemoval=false |
| IPdfNativeObjectBackend / PdfObjectRemovalRegion | Experimental whole intersecting top-level text/image removal with fresh page import; no secure admission |
| ExperimentalPdfRedactor / ExperimentalPdfOptions / PdfManualRegion | Nonpackable development-only explicit rasterized/native whole-object orchestration; not registered format support |
| IPdfStructuralInspector / PdfPigStructuralInspector | Optional managed current-xref observations with bounded Flate xref decoding; no content-payload decoding or secure admission |
| PdfStructuralInventory / PdfStructuralFeatures / PdfInspectionLimits | Source-free partial observations, explicit opaque/unknown features and immutable managed bounds |
| NuntivusRedactOptions / NuntivusRedactBuilder / ServiceCollectionExtensions | Optional DI composition and lifetime validation |
| NuntivusRedactHttpOptions / NuntivusRedactHttpHandler / NuntivusRedactHttpExtensions | Optional bounded HTTP registration and handling |

See DETECTION.md, formats/structured.md, EXTENSIBILITY.md and SECURITY.md for normative behavior and limitations. Methods validate configuration and arguments; processing failures use safe error codes. Source/output strings are sensitive even when some matches were replaced. No non-strict incomplete-success API exists.
See [Office](formats/office.md) for constructor/registration examples and the exact binary support/rejection matrix. Edit lists are copied, ordered and nonoverlapping; producers must reconstruct final text from original UTF-16 offsets without splitting surrogate pairs. Office verifies that invariant before committing output.
See [OCR/images](formats/ocr.md) for provider replacement, coordinate/text coverage rules, explicit Office image constructors, budgets, disposal and native deployment limitations. OCR results and image bytes remain sensitive; native cancellation is cooperative, not a hard interruption.
See [offline NLP](nlp.md) for the exact tensor/tokenizer profile, local model provisioning, category mappings, provider replacement, disposal, native limits and unverified trained-model accuracy gates.
See [PDF](formats/pdf.md) for low-level PDF API details. PDF format processing, native redaction, and hostile-input safety remain unsupported; source text/pixels are sensitive, one shared runtime per process and no other PDFium users, dispose sessions/writers first.
