# Security policy

This project is pre-release and not a complete document sanitization system. Only the implemented UTF-8 profile is available; PDF, Office, images and OCR are unsupported. No version currently has a production security support commitment.

Do not submit sensitive documents, keys, or exploit payloads containing real personal data in public issues. Use the repository host's private vulnerability reporting channel when the owner enables it; if unavailable, request a private contact without disclosing details publicly. A dedicated reporting address and response SLA have not yet been established.

See docs/SECURITY.md for the threat model, tested mitigations and limitations. A successful response means configured matches were transformed, not that every sensitive value was discovered. Discard output after any error. Never log document contents.
