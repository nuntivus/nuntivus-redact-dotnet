# Extension and usage guide

## Standalone quick start

The complete compiling example is samples/NuntivusRedact.Sample.Console/Program.cs. Create an explicit strategy, build a RedactionPolicy, supply DetectorRegistration instances to RedactionEngine, then call RedactAsync or construct a document processor with that ITextRedactor. No DI container is required.

Only enable relevant detectors; default phone and card candidates can overlap. Use WithPriority and WithStrategy for your domain and test representative inputs. Set confidence and size budgets before Build. Policies are immutable snapshots; builders are not thread safe. Custom predicates must be pure/thread safe.

## Custom detectors and analyzers

Implement ISensitiveDataDetector and supply a unique stable Id. DetectAsync returns validated original UTF-16 spans, categories, confidence and the same detector Id. Never put source values in category/ID/exception metadata. Honor cancellation and resource limits, avoid splitting surrogate pairs, and never retain the input context. The custom detector sample implements synthetic ticket detection. A dictionary with one entry implements keyword detection; RegexDetector supports custom bounded candidate patterns and trusted post-validation.

Context-specific analyzers can implement ISensitiveDataDetector today; a separate analyzer contract is not yet designed. Custom policy and validation-rule interfaces beyond the builder predicate are deferred. Do not invent compatibility promises for unavailable interfaces.

## Custom strategies

Implement IRedactionStrategy.Redact. Input is a span of the original accepted text and immutable metadata; return a replacement. Instances must support concurrent use and must not log source. Select a default strategy with the policy constructor or a per-category strategy with WithStrategy. Strategies are applied once, not rescanned. Masking and pseudonymization are not anonymization.

## Custom document processors

Implement IDocumentProcessor with a stable Format, or derive BufferedDocumentProcessor for bounded staged UTF-8 processing. Use DocumentProcessingContext.RedactAsync with numeric structural paths, not sensitive names. Handle every semantic/hidden content surface or explicitly reject it. Preserve stream ownership, enforce input/output/depth/field/report limits, validate outputs and do not write before processing succeeds. Any exception means discard destination. Register processors explicitly with DocumentProcessorRegistry or DI.

Formatting-preserving adapters can consume IEditAwareTextRedactor.RedactWithEditsAsync. Core implements it in one pass; custom implementations must provide ordered, nonoverlapping original UTF-16 spans with replacement strings that reconstruct result.Text exactly. Do not execute a stateful detector/strategy a second time to derive replacements. Do not log edit-bearing results: replacement strings may still be sensitive. Existing ITextRedactor implementations remain compatible with text processors, but do not gain Office support automatically. The [Office guide](formats/office.md) shows standalone and explicit DI construction; binary processors must not derive the UTF-8 base.

## Optional DI

The executable sample is samples/NuntivusRedact.Sample.DependencyInjection/Program.cs. Call services.AddNuntivusRedact(policy), then AddDetector<T>(), AddStrategy<T>() and AddDocumentProcessor<T>() as needed. Nothing is auto-discovered. NuntivusRedactOptions.Policy is required when using the options callback overload. The engine is singleton and options are snapshotted at first resolution; runtime reconfiguration is not supported. Strategies in a policy are explicit instances; AddStrategy registers optional extensions but does not silently select a default.

All registered extensions must be singleton/thread safe. Direct non-singleton registrations are rejected when the engine resolves. BuildServiceProvider with ValidateScopes=true and ValidateOnBuild=true checks captive scoped dependencies. Do not mutate IServiceCollection after building the provider. Do not resolve scoped services from extension factories. Use IOptions only at the configuration boundary, not as mutable runtime state in Core.

## Versioning and packages

Current candidates are 0.1.0-alpha.1, not published. APIs may change during alpha; document all changes in CHANGELOG.md and consider downstream extensions before breaking contracts. After stable release use semantic versioning, retain compatible public contracts in minor/patch releases and use major versions for breaks. No legacy framework migration promise exists.

Build local candidates with tools/verification/verify.ps1. For a local feed use `dotnet add <consumer.csproj> package NuntivusRedact.Core --version 0.1.0-alpha.1 --source <absolute-path-to-artifacts/packages>`. Install additional detector/strategy/format/DI packages explicitly. The source solution targets net10.0 only. No packages are currently available from a public feed. See MIGRATION.md for the alpha rename.
