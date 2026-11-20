$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$feed = Join-Path $root 'artifacts/packages'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = @(Get-ChildItem $feed -Filter '*.nupkg')
if ($packages.Count -ne 21) { throw "Expected 21 candidate packages, found $($packages.Count). Reconcile implemented projects." }
foreach ($package in $packages) {
    if ($package.Name -notlike 'NuntivusRedact.*.nupkg') { throw "Unexpected package identity: $($package.Name)" }
    $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = @($zip.Entries | ForEach-Object FullName)
        foreach ($required in @('README.md', 'LICENSE')) { if ($entries -notcontains $required) { throw "Missing $required in $($package.Name)" } }
        if (-not ($entries | Where-Object { $_ -like 'lib/net10.0/*.dll' })) { throw "Missing assembly in $($package.Name)" }
        if (-not ($entries | Where-Object { $_ -like 'lib/net10.0/*.xml' })) { throw "Missing XML documentation in $($package.Name)" }
        $nuspec = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        $reader = [IO.StreamReader]::new($nuspec.Open())
        try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($metadata.package.metadata.license.InnerText -ne 'Apache-2.0') { throw "Unexpected license in $($package.Name)" }
        $id = [string]$metadata.package.metadata.id
        if ($id -eq 'NuntivusRedact.Pdf.PdfPig') {
            foreach ($notice in @('NOTICE.txt', 'THIRD-PARTY-LICENSES.txt')) {
                if ($entries -notcontains $notice) { throw "Missing $notice in $id" }
                $noticeReader = [IO.StreamReader]::new($zip.GetEntry($notice).Open())
                try { $noticeText = $noticeReader.ReadToEnd() } finally { $noticeReader.Dispose() }
                if ($noticeText -notmatch 'Adobe' -or $noticeText -notmatch 'pdfbox') { throw "Incomplete attribution in $notice for $id" }
            }
        }
        if (-not $id.StartsWith('NuntivusRedact.', [StringComparison]::Ordinal)) { throw "Unexpected package id: $id" }
        if ($entries -notcontains "lib/net10.0/$id.dll") { throw "Assembly identity does not match package id: $id" }
        foreach ($dependency in $metadata.package.metadata.dependencies.group.dependency) {
            if ([string]$dependency.id -like 'Redact.*') { throw "Legacy product dependency in $id" }
        }
    } finally { $zip.Dispose() }
    $symbol = [IO.Path]::ChangeExtension($package.FullName, '.snupkg')
    if (-not (Test-Path $symbol)) { throw "Missing symbols for $($package.Name)" }
    $zip = [IO.Compression.ZipFile]::OpenRead($symbol)
    try { if (-not ($zip.Entries | Where-Object { $_.FullName -like '*.pdb' })) { throw "Missing PDB in $symbol" } } finally { $zip.Dispose() }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('nuntivusredact-consumer-' + [guid]::NewGuid().ToString('N'))
New-Item $temp -ItemType Directory | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'consumer/ConsumerSmoke.csproj.fixture') (Join-Path $temp 'ConsumerSmoke.csproj')
Copy-Item (Join-Path $PSScriptRoot 'consumer/Program.cs.fixture') (Join-Path $temp 'Program.cs')
Copy-Item (Join-Path $PSScriptRoot 'consumer/NuGet.Config.fixture') (Join-Path $temp 'NuGet.Config')
Copy-Item (Join-Path $root 'tests/NuntivusRedact.Nlp.Tests/OnnxFixture.cs') (Join-Path $temp 'OnnxFixture.cs')
$previousFeed = $env:NUNTIVUSREDACT_PACKAGE_FEED
try {
    $env:NUNTIVUSREDACT_PACKAGE_FEED = $feed
    & dotnet restore (Join-Path $temp 'ConsumerSmoke.csproj') --configfile (Join-Path $temp 'NuGet.Config') --packages (Join-Path $temp 'packages') -tl:off
    if ($LASTEXITCODE -ne 0) { throw "Consumer restore failed; inspect $temp" }
    & dotnet run --project (Join-Path $temp 'ConsumerSmoke.csproj') -c Release --no-restore -tl:off
    if ($LASTEXITCODE -ne 0) { throw "Consumer execution failed; inspect $temp" }
    $example = $temp + '-nlp-example'
    New-Item $example -ItemType Directory | Out-Null
    Copy-Item (Join-Path $temp 'ConsumerSmoke.csproj') (Join-Path $example 'NlpExample.csproj')
    Copy-Item (Join-Path $temp 'NuGet.Config') (Join-Path $example 'NuGet.Config')
    $markdown = [IO.File]::ReadAllText((Join-Path $root 'docs/nlp.md'))
    $snippets = [regex]::Matches($markdown, '(?s)```csharp\r?\n(.*?)```')
    if ($snippets.Count -ne 1) { throw 'Expected one standalone NLP example.' }
    [IO.File]::WriteAllText((Join-Path $example 'Program.cs'), $snippets[0].Groups[1].Value)
    & dotnet restore (Join-Path $example 'NlpExample.csproj') --configfile (Join-Path $example 'NuGet.Config') --packages (Join-Path $temp 'packages') -tl:off
    if ($LASTEXITCODE -ne 0) { throw "NLP example restore failed; inspect $temp" }
    & dotnet build (Join-Path $example 'NlpExample.csproj') -c Release --no-restore -tl:off
    if ($LASTEXITCODE -ne 0) { throw "NLP example compilation failed; inspect $temp" }
    $pdf = $temp + '-pdf'
    New-Item $pdf -ItemType Directory | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'consumer/PdfSmoke.csproj.fixture') (Join-Path $pdf 'PdfSmoke.csproj')
    Copy-Item (Join-Path $PSScriptRoot 'consumer/PdfProgram.cs.fixture') (Join-Path $pdf 'Program.cs')
    Copy-Item (Join-Path $temp 'NuGet.Config') (Join-Path $pdf 'NuGet.Config')
    Copy-Item (Join-Path $root 'tests/NuntivusRedact.Pdf.Tests/PdfFixtures.cs') (Join-Path $pdf 'PdfFixtures.cs')
    Copy-Item (Join-Path $root 'tests/NuntivusRedact.Pdf.Tests/StructuralPdfFixtures.cs') (Join-Path $pdf 'StructuralPdfFixtures.cs')
    & dotnet restore (Join-Path $pdf 'PdfSmoke.csproj') --configfile (Join-Path $pdf 'NuGet.Config') --packages (Join-Path $temp 'packages') -tl:off
    if ($LASTEXITCODE -ne 0) { throw "PDF consumer restore failed; inspect $pdf" }
    & dotnet run --project (Join-Path $pdf 'PdfSmoke.csproj') -c Release --no-restore -tl:off
    if ($LASTEXITCODE -ne 0) { throw "PDF consumer execution failed; inspect $pdf" }
    & dotnet publish (Join-Path $pdf 'PdfSmoke.csproj') -c Release --no-restore --no-self-contained -o (Join-Path $pdf 'publish') -tl:off
    if ($LASTEXITCODE -ne 0) { throw "PDF consumer publish preparation failed; inspect $pdf" }
    & dotnet (Join-Path $pdf 'publish/PdfSmoke.dll')
    if ($LASTEXITCODE -ne 0) { throw "Published-output PDF consumer failed; inspect $pdf" }
    Write-Output "Verified $($packages.Count) packages and symbols; package-only consumer retained at $temp"
    Write-Output "PDF low-level NuGet consumer retained at $pdf; no PDF redaction support implied."
} finally { $env:NUNTIVUSREDACT_PACKAGE_FEED = $previousFeed }
