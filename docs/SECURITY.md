# Threat model and security limits

## Assets and trust boundaries
Source documents, unredacted fields, pseudonymization keys and outputs are sensitive. Input documents are untrusted; extension implementations, regex patterns, policies and destination management are trusted application configuration. Core/format libraries do not log. Caller logging, backups, source files, swap/crash dumps and managed heap retention are outside secure-removal guarantees.

## Threats and mitigations
| Threat | Mitigation | Evidence |
|---|---|---|
| ReDoS | finite timeout, pattern/input/candidate bounds | DetectorTests.RegexTimeoutNeverLeaksInput |
| Invalid offsets/surrogate splitting | validate engine candidates before edits | EngineTests.InvalidRangesAndSurrogateSplitsAreRejected |
| Detector/strategy/source leakage | safe codes, no inner exception | EngineTests.ExtensionFailuresNeverExposeSourceOrInnerException; SecurityTests.StrategyExceptionsCannotLeakSourceOrWritePartialOutput |
| Partial success | stage before write, errors abort, empty destination | format malformed-input tests; NonemptyOutputIsRejectedRatherThanLeavingSensitiveTrailingBytes |
| XXE/entity bombs | DTD prohibited, null resolver, bounded preflight | SecurityTests.XmlEntityExpansionFailsWithoutOutput |
| Deep/large documents | byte, depth, field, output, report limits | FormatTests.BudgetsDepthCancellationAndInvalidUtf8FailClosed |
| Duplicate JSON names/key collisions | reject before output | FormatTests.JsonRejectsAmbiguousAndUnsafeNumericValues |
| Encoded JSON source recovery | decode, replace keys/strings, serialize/reparse | SecurityTests.OutputReparseCannotRecoverAcceptedMatchesFromSupportedJsonSurface |
| XML text/CDATA splitting | coalesce leaf text; reject mixed content | FormatTests.XmlCoalescesTextAndCdataBeforeDetection |
| Cross-request state | immutable settings; local request state | concurrent engine/processor tests |
| Output I/O detail leakage | sanitize failures; require discard | SecurityTests.OutputIoFailuresAreSanitizedAndNeverReportedAsSuccess |
| Zip bombs, PDF revisions, native parser exploits | no supported binary/native processor | absent registrations, explicit unsupported format error |

## Residual risks
Detection is heuristic and can miss obfuscated/encoded/fragmented/context-specific data. Overlap resolution deliberately selects ranked nonoverlapping spans and can leave unmatched portions of rejected candidates. Partial masking retains source; keyed pseudonyms remain linkable. Schema/business validity, XML signatures and spreadsheet formula safety are not guaranteed. Caller must validate their domain.

Input/output staging is bounded but not streaming transformation and may allocate multiple copies; applications must cap concurrent requests. Cancellation is cooperative; regex can run until its finite timeout. Managed strings are not securely erased. Output stream failure can leave partial bytes: discard destination. No original file is modified/deleted. No secure-erasure claim applies to storage or memory.

No archive parsing, temporary files, OCR native dependency, binary metadata sanitation or isolated worker exists yet. Before binary support, design zip ratio/entry/count/size controls, reject external relationships and encrypted payloads, inventory all hidden content, isolate risky/native code in constrained workers, and implement output recovery checks. Container isolation is not currently delivered.

Dependency audit commands: `dotnet restore NuntivusRedact.slnx -p:NuGetAudit=true -p:NuGetAuditMode=all -p:TreatWarningsAsErrors=true`; `dotnet list NuntivusRedact.slnx package --vulnerable --include-transitive`. CI audits on changes and weekly. Passing an audit is a point-in-time feed result, not a vulnerability guarantee. Record actual audit output in PROGRESS.md.
