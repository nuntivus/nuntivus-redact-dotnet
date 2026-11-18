# Format support matrix

| Format | Current status | Profile / limitations |
|---|---|---|
| TXT | Implemented and tested | Explicit UTF-8 bounded text; no binary fallback |
| CSV | Implemented and tested | Comma/doubled quotes/multiline fields; normalized quoting/endings; not formula sanitization |
| JSON | Implemented and tested | String keys/values; reject duplicate/colliding names and detected non-string primitives; no schema guarantee |
| XML | Implemented and tested | Leaf text/CDATA/attributes/comments; reject DTDs/PIs/mixed content and sensitive structural identifiers |
| DOCX/XLSX | Restricted implemented profile | Run-preserving edits, bounded ZIP/XML, supported nested packages, metadata/recovery checks; explicit feature rejection; see [Office](office.md) |
| PPTX | Unsupported / deferred OFF-001 | Slide/notes/master content inventory and recovery checks remain unfinished |
| PDF | Unsupported / deferred PDF-001 | No backend or license selected; no cosmetic rectangle-based support |
| Images | Unsupported / deferred OCR-001 | Need bounded decode, permanent pixels, metadata/multi-frame sanitation, output validation and isolation assessment |
| OCR | Unsupported / deferred OCR-001 | OCR contracts/provider/coordinate mapping/Tesseract not yet implemented; no accuracy claims |

See [structured text](structured.md), [Office](office.md) and [tasks](../TASKS.md) for exact behavior/acceptance criteria. No unsupported format project has been created as an empty shell. Planned PDF/Images/OCR packages remain deferred until meaningful safe implementations and tests exist. Office is an independent implemented package, not general support for all Office features.

Managed parsing produces accepted-match transformations, not proof of total discovery. Formats can contain application-specific encoded content that generic detectors cannot interpret. Preserve originals according to your security policy; this library does not erase original files, memory, backups or storage history.
