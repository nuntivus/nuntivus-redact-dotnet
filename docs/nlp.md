# Offline NLP detection

## Scope and packages

NLP is optional and complements the existing validated regex and dictionary detectors. Generic named entities are not necessarily sensitive data. Person names, organizations, locations, dates and miscellaneous model entities have configurable categories; location recognition is not complete postal-address detection and dates are not necessarily dates of birth.

| Package | Responsibility |
| --- | --- |
| NuntivusRedact.Nlp.Abstractions | `IEntityRecognitionProvider` and immutable source-free `RecognizedEntity` |
| NuntivusRedact.Detectors.Nlp | `NlpDetector`, immutable `NlpDetectorOptions` and `NlpDetectionCategories` |
| NuntivusRedact.Nlp.Onnx | Local CPU `OnnxEntityRecognitionProvider` and immutable `OnnxNerOptions` |

Core is unchanged and does not reference inference libraries. Formats use the existing redactor and need no NLP-specific implementation. No provider is registered automatically. No model weights, language resources, HTTP clients or runtime downloads are included.

## Model evaluation and recommendation

**Conditional first candidate:** `Davlan/distilbert-base-multilingual-cased-ner-hrl`, exported as a self-contained ONNX token-classification graph with its matching cased WordPiece `vocab.txt`. Its card describes Arabic, German, English, Spanish, French, Italian, Latvian, Dutch, Portuguese and Chinese. That is a training-language claim, not verified accuracy in this library. Domain-specific documents, OCR errors and unsupported languages can produce misses.

Metadata inspected during implementation:

| Candidate | Observed revision | Assessment |
| --- | --- | --- |
| [Davlan DistilBERT](https://huggingface.co/Davlan/distilbert-base-multilingual-cased-ner-hrl/tree/d421f57d5b1d36b375408588669e9340f9b11a89) | `d421f57d5b1d36b375408588669e9340f9b11a89` | Smaller multilingual cased BERT-family candidate; declared **AFL-3.0**, not Apache/MIT |
| [Xenova ONNX conversion](https://huggingface.co/Xenova/distilbert-base-multilingual-cased-ner-hrl/tree/c2a4dbf593c57f47004c5bc2d3770d311aee9c43) | `c2a4dbf593c57f47004c5bc2d3770d311aee9c43` | Existing ONNX export to evaluate; absent license metadata does not remove upstream obligations or establish compatibility |
| [WikiNEuRal](https://huggingface.co/Babelscape/wikineural-multilingual-ner) | Model card reviewed | Explicit CC-BY-NC-SA-4.0/non-commercial research restriction; not recommended for unrestricted commercial deployment |

A deployment owner must review model, training-data and conversion terms before provisioning. **No third-party trained weights were downloaded, redistributed or executed for this implementation.** Microsoft.ML.OnnxRuntime 1.32.0 and Microsoft.ML.Tokenizers 3.0.0 are centrally pinned and declare MIT; those licenses do not relicense any model. The alternative full mBERT Davlan candidate also declares AFL-3.0 and is not a license escape hatch.

The inspected DistilBERT configuration declares 119,547 vocabulary entries, 512 positions and this output ordering: `O, B-DATE, I-DATE, B-PER, I-PER, B-ORG, I-ORG, B-LOC, I-LOC`. The card describes three entity types, so **configuration, not prose, must determine the output label axis**. Recheck all metadata against the actual pinned/exported artifact. Do not copy this ordering to a different model.

## Provisioning and supported model interface

Provision assets during a trusted deployment step, not while processing documents:

1. Review licenses and select/pin the source revision, tokenizer settings, export tool versions and model configuration.
2. Export locally or review an existing ONNX conversion. Only a **self-contained** graph is supported; external tensor files and custom native operators are not configured.
3. Supply the matching UTF-8 cased WordPiece vocabulary and ordered BIO label list. Compute SHA-256 values in the trusted provisioning process and store them in immutable deployment configuration. Do not compute expected digests from untrusted files on every startup.
4. Exercise `CreateAsync` and representative inputs on the target OS/architecture. A matching tensor interface alone does not establish semantic/model compatibility.
5. Measure precision, recall and redacted-span coverage per category/language/domain on a labeled evaluation set before production decisions. No accuracy or performance baseline is asserted here.

The provider accepts exactly `input_ids` and `attention_mask`, optionally `token_type_ids`, as Int64 `[1, sequence]` tensors. Exactly one Float32 output named `logits` must have shape `[1, sequence, labelCount]`. Batch/sequence axes may be dynamic or match the configured padded sequence length. Labels must be unique `O` or paired `B-TYPE`/`I-TYPE` entries with fixed ASCII identifiers. A mismatched profile fails closed. BERT/DistilBERT-compatible exports can fit this interface; XLM-R/SentencePiece, byte-level BPE, arbitrary tokenizer JSON, generative models, multi-output graphs and arbitrary ONNX graphs are **not** advertised as supported profiles.

## Standalone usage

Reference Core, Strategies, Detectors.Nlp and Nlp.Onnx. This is a standalone program using four deployment arguments: model path, vocabulary path, expected model SHA-256 and expected vocabulary SHA-256. It uses synthetic text and logs only a count. Supply the exact label order for your reviewed model.

```csharp
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.Nlp;
using NuntivusRedact.Nlp.Onnx;
using NuntivusRedact.Strategies;

if (args.Length != 4)
    throw new ArgumentException("Supply the four local model deployment arguments.");

string[] labels = ["O", "B-DATE", "I-DATE", "B-PER", "I-PER",
    "B-ORG", "I-ORG", "B-LOC", "I-LOC"];
var options = new OnnxNerOptions(args[0], args[1], args[2], args[3], labels);
using var provider = await OnnxEntityRecognitionProvider.CreateAsync(options);
var detector = new NlpDetector("local-ner", provider);
var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy())
    .Include(NlpDetectionCategories.Person).Build();
var engine = new RedactionEngine([new(detector)], policy);
var result = await engine.RedactAsync("Mario Rossi works at Contoso.");
Console.WriteLine($"Accepted matches: {result.Detections.Count}.");
```

Keep the provider alive for all engine calls; `NlpDetector` does not own it. The standalone example filters to person names by policy; organization/location/date candidates remain unredacted. Add existing regex/dictionary detectors explicitly for structured values. Entity scores are model heuristics, not calibrated probabilities comparable with regex scores. Use category/provider-specific `Where` predicates if thresholds differ; do not assume one universal threshold is meaningful.

Optional DI needs no additional product dependency: create the provider asynchronously at application startup, register the resulting `ISensitiveDataDetector` instance with `AddSingleton<ISensitiveDataDetector>(detector)`, and use existing `AddNuntivusRedact(policy)`. The host must dispose an externally created provider after requests finish. Validate scopes and avoid captive scoped dependencies. No synchronous blocking factory, service locator or automatic model download is needed.

## Replacing the provider

Implement `NuntivusRedact.Nlp.IEntityRecognitionProvider.RecognizeAsync(string, CancellationToken)` in a concurrency-safe application service. Return immutable `RecognizedEntity` instances containing original UTF-16 spans, fixed labels without BIO prefixes and finite scores between zero and one. Return no source values. Pass the implementation to `new NlpDetector("my-provider", provider, options)`; the adapter has no ONNX dependency. Existing tests use a deterministic fake provider as an executable extension example.

`NlpDetectorOptions` defensively copies up to 64 label/category mappings. Defaults map PER, ORG, LOC, DATE and MISC to person, organization, location, date and miscellaneous. Every returned label must have a mapping; unknown labels are errors, not silently discarded candidates. Default adapter bounds are 100,000 UTF-16 input units and 1,000 entities. Filter categories through Core policy rather than making unmapped labels appear harmless.

## Offsets, tokenization and window semantics

Cased BERT basic preprocessing preserves case and combining marks, cleans NUL/replacement/control/format characters, converts BERT whitespace to spaces, and splits punctuation/CJK characters before library WordPiece segmentation. It retains an explicit original-character mapping. Special BERT markers have their vocabulary IDs. The vocabulary must contain the standard PAD/UNK/CLS/SEP/MASK tokens, have unique nonempty lines, at most 256,000 entries and at most 256 UTF-16 units per entry. WordPiece uses `##` continuation and a 100-character basic-word limit; longer/unknown words become UNK, not proof of recognition. Other tokenizer normalization settings require another provider/profile.

Input containing malformed UTF-16 fails. Offsets refer to original text, including emoji and any removed characters between matched endpoints. Basic-word expansion covers an entire word if only some subwords are labeled, favoring coverage over exact model boundaries. It does not imply grapheme-aware entity recognition.

All tokens are processed in bounded overlapping windows. For each token, the prediction furthest from a window edge wins; ties use the earlier window. The provider then decodes the complete token sequence: matching I-labels continue an entity, orphan I-labels start one conservatively, and O ends one. Repeated same-label subwords in one word are combined; duplicate span/label results are removed deterministically. Entity confidence is the minimum selected-token score within that entity. This is a deterministic heuristic, not an ensemble or an accuracy claim. Entities longer than the overlap may still be misclassified.

Core's existing **winner-takes-overlap** policy is unchanged. A higher-priority narrow match can discard a broader NLP entity and leave part of the latter untouched. Regression tests explicitly demonstrate this tradeoff. Evaluate coverage before assigning priorities; no union-of-sensitive-spans behavior is implied.

Formats supply bounded segments: CSV/JSON generally supply individual fields; supported Office runs are joined within paragraph groups, not across arbitrary fields/paragraphs. A detector cannot recover context it never receives. This implementation adds no cross-field context contract or new format support.

## Budgets, failures and native security

Default ONNX limits: 100,000 UTF-16 input units, 20,000 content tokens, 256 windows, 128 padded positions including CLS/SEP, 32 overlapping content tokens, 1,000 entities, four admitted requests including waiters, 1 GB model bytes and 8 MB vocabulary bytes. These limits are configurable and are not estimates of native peak memory. Larger graphs can require substantial additional managed/native memory during loading and inference. Window/token limits fail before inference rather than silently truncating the tail. Sequence lengths must be 4–512 and overlap must be less than content capacity.

Each provider serializes inference and bounds its admission queue. CPU sessions use one intra-op/inter-op thread; cancellation is checked during managed work, waiting and between windows and requests ONNX termination during inference. It is **best effort, not a hard deadline**. Dispose rejects subsequent work and waits for active inference; do not dispose from a provider callback. Session initialization also has no hard native cancellation guarantee.

Use only trusted reviewed graphs. A hash proves identity, not safety. ONNX Runtime parses/executes native code **in-process**, not in a sandbox. File byte limits do not contain native crashes, allocation explosions or malicious operators. Hosts handling hostile inputs or requiring hard CPU/memory/time limits must use an externally isolated worker, apply OS resource limits and bound aggregate concurrency across provider instances. No native-worker isolation is delivered here.

Invalid assets, incompatible profiles, unknown labels, invalid spans, nonfinite logits and exceeded limits fail with sanitized `RedactionException` codes and no inner exception. Cancellation exposes only the caller token. There is no automatic fallback to regex-only success if NLP fails. Do not log model exceptions, source strings, output strings, token strings or tensors; managed buffers are not securely erased. Reports contain only spans/categories/IDs/scores, never matched text. Successful processing means accepted matches were transformed, not that all sensitive data was found.

## Verification boundaries

The xUnit fixture constructs a tiny synthetic ONNX Gather graph and vocabulary, with no third-party weights, to execute native inference and verify offset mapping, tokenization, BIO decoding, window tails/overlaps, finite logits, budgets, cancellation, admission, repeated/concurrent reuse, asset/profile failures, policy integration and recovery. Synthetic multilingual strings test coordinates, **not multilingual model quality**. The isolated package smoke uses the same synthetic graph to verify transitive native deployment and package-only consumption. Exact executed counts and commands are in [Progress](PROGRESS.md).

Before advertising a trained model deployment, separately verify its licensing, tokenizer parity, source/export provenance, native RID deployment, precision/recall, span coverage, OCR/domain robustness and overlap policy on representative data. Model accuracy, all-language coverage, platform certification and secure memory erasure are not claimed. A local generative LLM was not selected: generated text and approximate locations are harder to validate than token classification against original offsets.
