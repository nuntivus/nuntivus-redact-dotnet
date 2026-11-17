# Format support matrix

| Format | Current status | Profile / limitations |
|---|---|---|
| TXT | Implemented and tested | Explicit UTF-8 bounded text; no binary fallback |
| CSV | Implemented and tested | Comma/doubled quotes/multiline fields; normalized quoting/endings; not formula sanitization |
| JSON | Implemented and tested | String keys/values; reject duplicate/colliding names and detected non-string primitives; no schema guarantee |
| XML | Implemented and tested | Leaf text/CDATA/attributes/comments; reject DTDs/PIs/mixed content and sensitive structural identifiers |
| DOCX/XLSX/PPTX | Unsupported / deferred OFF-001 | Need Open XML content inventory, hidden content/relationships/metadata handling, archive bounds and recovery tests |
| PDF | Unsupported / deferred PDF-001 | No backend or license selected; no cosmetic rectangle-based support |
| Images | Unsupported / deferred OCR-001 | Need bounded decode, permanent pixels, metadata/multi-frame sanitation, output validation and isolation assessment |
| OCR | Unsupported / deferred OCR-001 | OCR contracts/provider/coordinate mapping/Tesseract not yet implemented; no accuracy claims |

See structured.md for exact format behavior and docs/TASKS.md for acceptance criteria. No unsupported format project has been created as an empty shell. Planned package names NuntivusRedact.Formats.Office, NuntivusRedact.Formats.Pdf, NuntivusRedact.Formats.Images, NuntivusRedact.Ocr.Abstractions and NuntivusRedact.Ocr.Tesseract remain deferred until meaningful safe implementation and tests exist.

Managed parsing produces accepted-match transformations, not proof of total discovery. Formats can contain application-specific encoded content that generic detectors cannot interpret. Preserve originals according to your security policy; this library does not erase original files, memory, backups or storage history.
