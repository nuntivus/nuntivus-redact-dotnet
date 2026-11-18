$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'This native verification profile requires Windows x64; other native deployments are not verified.'
}
$revision = '87416418657359cb625c412a48b6e1d6d41c29bd'
$expected = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
$data = Join-Path $root 'artifacts/ocr-tessdata'
New-Item $data -ItemType Directory -Force | Out-Null
$model = Join-Path $data 'eng.traineddata'
if (-not (Test-Path $model)) {
    Invoke-WebRequest "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$revision/eng.traineddata" -OutFile $model
}
if ((Get-FileHash $model -Algorithm SHA256).Hash -ne $expected) { throw 'OCR test model hash mismatch.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('nuntivusredact-ocr-' + [guid]::NewGuid().ToString('N'))
New-Item $temp -ItemType Directory | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'ocr/OcrSmoke.csproj.fixture') (Join-Path $temp 'OcrSmoke.csproj')
Copy-Item (Join-Path $PSScriptRoot 'ocr/Program.cs.fixture') (Join-Path $temp 'Program.cs')
Copy-Item (Join-Path $PSScriptRoot 'consumer/NuGet.Config.fixture') (Join-Path $temp 'NuGet.Config')
foreach ($fixture in @('OfficeFixtures.cs', 'OcrFixtures.cs')) {
    Copy-Item (Join-Path $root "tests/NuntivusRedact.Formats.Tests/$fixture") (Join-Path $temp $fixture)
}
$previousFeed = $env:NUNTIVUSREDACT_PACKAGE_FEED
try {
    $env:NUNTIVUSREDACT_PACKAGE_FEED = Join-Path $root 'artifacts/packages'
    & dotnet restore (Join-Path $temp 'OcrSmoke.csproj') --configfile (Join-Path $temp 'NuGet.Config') --packages (Join-Path $temp 'packages') -tl:off
    if ($LASTEXITCODE -ne 0) { throw "OCR package consumer restore failed; inspect $temp" }
    & dotnet run --project (Join-Path $temp 'OcrSmoke.csproj') -c Release --no-restore -tl:off -- $data
    if ($LASTEXITCODE -ne 0) { throw "OCR native recovery verification failed; inspect $temp" }
    $publish = Join-Path $temp 'published'
    & dotnet publish (Join-Path $temp 'OcrSmoke.csproj') -c Release --no-restore -o $publish -tl:off
    if ($LASTEXITCODE -ne 0) { throw "OCR consumer publish failed; inspect $temp" }
    & dotnet (Join-Path $publish 'OcrSmoke.dll') $data
    if ($LASTEXITCODE -ne 0) { throw "Published OCR native recovery verification failed; inspect $temp" }
    Write-Output "Verified native Windows x64 OCR with pinned Apache-2.0 tessdata_fast eng model $revision; consumer retained at $temp"
} finally { $env:NUNTIVUSREDACT_PACKAGE_FEED = $previousFeed }
