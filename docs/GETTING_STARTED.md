# Getting started

[Documentation](README.md) · [API reference](API_REFERENCE.md) · [Formats](formats/structured.md)

## Requirements

- .NET SDK **10.0.401**, pinned in [global.json](../global.json), or an allowed 10.0.4xx patch. The projects target `net10.0`.
- Git to clone the repository after its owner supplies the real URL, or a downloaded source archive.
- PowerShell 7 (`pwsh`) for the complete verification/package/documentation scripts. The core build/test commands also work from other shells; a POSIX verification script is available.
- Windows or Linux for the configured CI matrix. Local verification does not prove that hosted jobs or another platform have passed.

There is no Node.js, database, OCR engine or native PDF dependency for the current managed profile.

## 1. Build the source

Open a terminal in the repository root. Until the repository URL is configured, obtain the source from its owner; `OWNER/REPOSITORY` badge URLs are placeholders, not a clone URL.

```sh
dotnet restore NuntivusRedact.slnx
dotnet build NuntivusRedact.slnx -c Release --no-restore
dotnet test NuntivusRedact.slnx -c Release --no-build
```

Always name the solution: the canonical solution, preserved IDE solution and original nonpackable scaffold coexist. Do not run bare `dotnet build` at the root. Visual Studio users can open [NuntivusRedact.slnx](../NuntivusRedact.slnx) or [NuntivusRedact.Workspace.slnx](../NuntivusRedact.Workspace.slnx). If old renamed project/test paths remain cached, reopen the current solution; do not recreate deleted projects.

For restore/build/test/pack together:

```powershell
pwsh -File ./tools/verification/verify.ps1
pwsh -File ./tools/verification/verify.ps1 -Task format-check
pwsh -File ./tools/verification/docs-check.ps1
pwsh -File ./tools/verification/package-smoke.ps1
```

The alternative POSIX command is `sh tools/verification/verify.sh`. Package smoke and documentation checks require PowerShell 7. Local packages go to `artifacts/packages`; TRX results and other generated evidence belong under ignored `artifacts/`. The intentionally nonpackable Web API sample can emit a pack warning; it is not a product package.

## 2. Run an executable example

```sh
dotnet run --project samples/NuntivusRedact.Sample.Console -c Release
dotnet run --project samples/NuntivusRedact.Sample.DependencyInjection -c Release
dotnet run --project samples/NuntivusRedact.Sample.CustomDetector -c Release
```

Each example uses synthetic input and asserts expected behavior without printing the document. The console example processes a JSON string containing an email and an unrelated numeric value. The DI example registers an explicit custom keyword detector; the extension example implements a domain detector. Open their `Program.cs` files to adapt the composition.

## 3. Compose a text redactor

A consumer needs `NuntivusRedact.Core`, `NuntivusRedact.Detectors.PersonalData` and `NuntivusRedact.Strategies`. Use these explicit namespaces in a standalone console application:

```csharp
using NuntivusRedact;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Strategies;

var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build();
var engine = new RedactionEngine(
    [new DetectorRegistration(PersonalDataDetectors.Email())], policy);

var result = await engine.RedactAsync("Contact alice@example.com.");
if (result.Text != "Contact [REDACTED].")
    throw new InvalidOperationException("Synthetic example verification failed.");
Console.WriteLine($"Replacements: {result.Detections.Count}.");
```

The email is synthetic. Avoid logging real source or transformed documents. Choose detectors for your domain rather than enabling every heuristic indiscriminately. The [detection guide](DETECTION.md) explains false positives, false negatives, confidence and bounded matching.

## 4. Process structured input

Add the appropriate format package, for example `NuntivusRedact.Formats.Json`. After creating `engine` above:

```csharp
using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "{\"contact\":\"alice@example.com\",\"count\":42}"));
using var output = new MemoryStream();
var processor = new NuntivusRedact.Formats.Json.JsonDocumentProcessor(engine);
var report = await processor.ProcessAsync(input, output, CancellationToken.None);
if (report.Detections.Count != 1)
    throw new InvalidOperationException("Synthetic JSON verification failed.");
```

Pass a real cancellation token in an application. Input and output must be distinct caller-owned streams; a seekable output must be empty and positioned at its beginning. The processor leaves streams open, so a `MemoryStream` can be rewound before reading. Use an explicit format; there is no binary-to-text fallback. Processing is memory-bounded buffering, not constant-memory streaming.

Only commit or expose output after successful processing. **Discard the destination after any exception**, including output I/O failures. Detection metadata contains numeric locations and field-relative UTF-16 offsets, not source values. Review the [strict format profile](formats/structured.md) before use: JSON duplicate/colliding keys and detected non-string primitives fail; XML DTDs/PIs, sensitive structural names and mixed content fail. CSV formulas are not neutralized.

## 5. Consume local packages

There is **no published NuGet release**. Build and verify local candidates first:

```sh
dotnet pack NuntivusRedact.slnx -c Release --no-build -o artifacts/packages
```

Then, in a separate scratch directory **outside the source checkout**, create a consumer:

```sh
dotnet new console -n RedactDemo --framework net10.0
dotnet add RedactDemo/RedactDemo.csproj package NuntivusRedact.Core --version 0.1.0-alpha.1 --source "<absolute-path-to-artifacts/packages>"
dotnet add RedactDemo/RedactDemo.csproj package NuntivusRedact.Detectors.PersonalData --version 0.1.0-alpha.1 --source "<absolute-path-to-artifacts/packages>"
dotnet add RedactDemo/RedactDemo.csproj package NuntivusRedact.Strategies --version 0.1.0-alpha.1 --source "<absolute-path-to-artifacts/packages>"
```

Replace the feed placeholder with the absolute path from your checkout, then use the text example in `RedactDemo/Program.cs`. Do not add a consumer project inside the checkout when contributing; keep it separate from the maintained solutions. For structured JSON also add `NuntivusRedact.Formats.Json` at the same version from the local feed.

For packages with external dependencies (CSV/CsvHelper, Office/Open XML SDK, DI/HTTP), configure **both** the local feed and nuget.org in the consumer's `NuGet.Config`; a local-only `--source` may not resolve external packages. The [package-only consumer fixture](../tools/verification/consumer/NuGet.Config.fixture) demonstrates the two-feed setup. Do not publish candidates or rely on NuGet.org install badges before owner approval and actual publication. Alpha APIs may change; see [migration notes](MIGRATION.md).

## 6. Try the optional local API

```sh
dotnet run --project samples/NuntivusRedact.Sample.WebApi --urls http://127.0.0.1:5187
```

Visit `http://127.0.0.1:5187/openapi/v1.json`. The [HTTP guide](HTTP.md) describes raw UTF-8 POST bodies for `/redact/text`, `/redact/csv`, `/redact/json` and `/redact/xml`, safe errors and headers. Stop the host with Ctrl+C after testing. No deployment, authentication, durable storage or production service is provided by this example. Add authentication/authorization, TLS, logging controls, request/concurrency limits and a host threat model before any nonlocal exposure.

## Next steps

- [Extensions and optional DI](EXTENSIBILITY.md)
- [Architecture](ARCHITECTURE.md) and [security model](SECURITY.md)
- [Contribute](../CONTRIBUTING.md) or [prepare a GitHub repository](GITHUB_SETUP.md)
- [Support matrix and deferred work](formats/README.md)

For DOCX/XLSX, reference NuntivusRedact.Formats.Office and follow the [Office support matrix and registration example](formats/office.md). The executable DI sample processes both formats with synthetic data. Office has independent binary budgets and requires the optional edit-aware capability implemented by Core; it does not use the UTF-8 processor base. The HTTP sample still enables only its four text formats.

For optional PNG/JPEG pixel redaction and embedded Office pictures, compose the separate image module with a replaceable OCR provider; see [OCR/images](formats/ocr.md). The Tesseract adapter uses trusted host-supplied language files and in-process native libraries, not a sandbox. Existing Office constructors still reject images.

Successful replacement does not prove complete sensitive-data discovery or anonymization. Masking and keyed pseudonymization are not equivalent to irreversible anonymization. PPTX/PDF and native-worker isolation remain unsupported.
