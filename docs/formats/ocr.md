# Optional OCR and Office pictures

## Modules and provider replacement

- `NuntivusRedact.Ocr.Abstractions`: `IOcrProvider`, `OcrResult`, `OcrWord`, `OcrBounds`, `IImageRedactor` and `ImageRedactionResult`. Depends only on product Abstractions; no native SDKs.
- `NuntivusRedact.Ocr.Tesseract`: optional adapter for the Apache-2.0 .NET `Tesseract` 5.2.0 wrapper. Native Tesseract/Leptonica and trusted language data must be deployed by the host.
- `NuntivusRedact.Formats.Images`: `RasterImageRedactor` uses MIT SkiaSharp 3.119.4 and Linux native assets to decode, redact and rebuild a restricted PNG/JPEG raster profile. It is an `IImageRedactor`, not an automatically selected `IDocumentProcessor` or HTTP format.
- `NuntivusRedact.Formats.Office`: references only neutral OCR contracts, not Images, Tesseract or their native libraries. Existing constructors keep image rejection. The additional constructor explicitly opts into pictures.

No DI container, provider registry, reflection discovery or automatic local-to-cloud fallback is required. To change OCR backend, implement `IOcrProvider` and supply it to the same `RasterImageRedactor`. It receives a canonical single-frame PNG plus its dimensions and cancellation token. Return one sensitive text string and ordered `OcrWord` spans covering every non-whitespace character. Coordinates are top-left-origin integer pixels in that exact raster; right/bottom are exclusive. Use conservative axis-aligned rectangles enclosing complete words. Do not return coordinates after independent rotation or resizing. Confidence is optional recognition confidence in [0,1], separate from detector confidence; low-confidence words are not silently discarded.

Alternatively, implement `IImageRedactor` to replace the entire image backend while leaving Office unchanged. Custom extensions are trusted application code: enforce the supplied remaining pixel budget before decoding, return metadata-free PNG with positive original raster dimensions and source-free recognized-text detections, and never return partial success. Every adapter must be safe for concurrent calls and sanitize source-bearing exceptions.

## Explicit construction

Reference Office, Formats.Images, Ocr.Tesseract, Core, Detectors.PersonalData and Strategies. Build local packages first; no public NuGet release is implied. Supply a trusted language-data directory containing `eng.traineddata`. With an existing engine, caller-owned streams and cancellation token:

```csharp
using NuntivusRedact.Formats.Images;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Ocr.Tesseract;

using var ocr = new TesseractOcrProvider(new TesseractOptions
{
    DataPath = trustedTessdataDirectory,
    Languages = "eng",
    MaximumConcurrency = 1
});
using var images = new RasterImageRedactor(ocr, new ImageLimits
{
    MaximumPixels = 10_000_000,
    PaddingPixels = 3
});
var limits = new OfficeLimits { MaximumImages = 32, MaximumImagePixels = 40_000_000 };
var word = new DocxDocumentProcessor(engine, limits, images);
var excel = new XlsxDocumentProcessor(engine, limits, images);
var report = await word.ProcessAsync(input, output, cancellationToken);
```

The caller owns provider/image-redactor lifetimes. Dispose only after active document operations finish. For optional Microsoft DI, register these instances and explicitly register processors using factories; do not capture request-scoped services in singleton providers. No Office image registration or native initialization happens automatically.

For standalone image bytes, call `images.RedactAsync(encodedImage, "image/png", engine, remainingPixelBudget, cancellationToken)` and consume `ImageRedactionResult.Png`. Accepted source types are exactly `image/png` and `image/jpeg`; output is always PNG. Do not log encoded pixels, recognition results, text or replacements.

## Restricted picture profile

PNG and JPEG embedded parts in supported DOCX/XLSX packages are fully processed, including shared media and supported recursively nested packages. Each physical image part is processed once per package, not just once per visible reference. Picture names, alternative descriptions and titles are also inspected/redacted. Image relationships must be internal and resolve to supported parts. Unreferenced archive parts remain rejected; relationships to unused image parts are still processed.

Word supports single-picture inline/anchor DrawingML; spreadsheets support picture-only one-cell, two-cell and absolute anchors. Standard rectangular geometry, transforms, stretch and crop coordinates are preserved. Word anchors are restricted to `wrapNone`; other wrap profiles are rejected. VML, text boxes, shapes, charts, SmartArt, effects, alternate blips, hyperlinks, external pictures, SVG/EMF/WMF/GIF/TIFF and unknown extensions remain rejected. This is not broad Office drawing support. Schema validation remains mandatory before and after transformation.

The image pipeline:

1. Checks encoded size, image type, frame policy, dimensions and remaining pixel budget before raster allocation.
2. Decodes one raster, composites transparency over white, then encodes fresh PNG for recognition. Hidden RGB/alpha channels and source metadata are not passed through.
3. Validates OCR text coverage, ordered UTF-16 word spans, confidence and bounded pixel rectangles; validates detector edit reconstruction.
4. Permanently overwrites every complete word rectangle touched by an accepted edit, plus configured padding, with opaque black pixels. Text replacement strings are not drawn into the image; even a partial-mask strategy removes the entire touched word.
5. Encodes lossless PNG from the modified pixels and decodes it again to validate dimensions. Office writes a fresh media payload and updates its OPC content-type override to PNG, even if the existing part URI ends in `.jpg`.

APNG control chunks, multi-frame images, EXIF orientation other than top-left, truncated image data and unsupported types fail closed. Rotation metadata is rejected rather than guessed. Arbitrary trailing PNG bytes are rejected; original compressed payloads, EXIF, comments, profiles and thumbnails are not copied into output. Dimensions are retained, but transparency, color profiles and exact visual fidelity are not promised.

## Budgets, cancellation and native risks

Defaults per image: 8 MiB input, 16 MiB canonical/output PNG, 10 million pixels, maximum dimension 10,000, 100,000 UTF-16 characters, 20,000 words, 10,000 detections, concurrency two, padding three pixels. Tesseract separately defaults to 16 MiB PNG input, 10 million pixels, 100,000 characters, 20,000 words and one engine at a time. Configuration is immutable. Queued operations honor cancellation before allocating native rasters/engines. Each recognition owns its native engine; no shared mutable engine or language files are used.

Office additionally shares its normal archive/text limits plus 32 image parts and 40 million decoded pixels across all nested packages. Image detections count against the aggregate detection limit. Reports use numeric `/packages/0/parts/.../image/0` locations with recognized-text UTF-16 spans, not recognized strings or image filenames. They do not currently expose pixel geometry.

**Native parsing runs in-process by explicit design choice.** Tesseract, Leptonica and SkiaSharp are not sandboxed. Cancellation is cooperative between synchronous native calls; there is no hard timeout, native-memory ceiling or protection against native crashes. Managed byte/pixel/concurrency limits do not eliminate hostile-parser risks. For untrusted uploads, hosts must supply an isolated process/container with external time/memory controls. That deployment boundary is not implemented here, so OCR-001/SEC-002 isolation work remains incomplete.

The executed native deployment profile is Windows x64, using the wrapper's native binaries and the required Visual C++ runtime. Windows x86 and Linux/macOS Tesseract deployments are not verified; hosts must validate compatible native libraries, resolver behavior and language-data installation. Do not infer native-provider portability from the net10.0 target. Control native library search paths, `TESSDATA_PREFIX`, and trusted model files; never accept model paths, configuration files or language packs from document input. Production constructors never download models.

## Verification and limitations

xUnit tests cover provider replacement, PNG/JPEG pixel and metadata recovery, malformed OCR geometry/coverage, bad edits, APNG/EXIF rejection, limits, queued cancellation, concurrent use, sanitized failures, default Office rejection, alternative text and nested budgets.

After packing local candidates, execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/verification/ocr-smoke.ps1
```

This Windows x64 package-only consumer downloads an Apache-2.0 `tessdata_fast` English model from pinned revision `87416418657359cb625c412a48b6e1d6d41c29bd`, verifies SHA-256 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`, recognizes synthetic text, processes DOCX/XLSX pictures, checks every matched-region pixel and attempts OCR recovery. Source/model project: https://github.com/tesseract-ocr/tessdata_fast (Apache-2.0); production model selection and notices are the host's responsibility. The model stays in ignored verification artifacts, not product packages.

These synthetic checks are not an accuracy study, handwriting guarantee, proof of complete discovery or Microsoft Office visual-fidelity validation. OCR misses, inaccurate geometry, unsupported encodings and detector false negatives remain possible. Original files, storage history, backups and managed/native memory are not securely erased. PDF/PPTX integration and isolated native execution remain deferred. See [Office](office.md), [security](../SECURITY.md), [tasks](../TASKS.md) and [progress](../PROGRESS.md).
