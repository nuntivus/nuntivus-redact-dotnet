param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [switch]$RequireRepositoryIdentity
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Xml.Linq
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$script:localTargets = 0

function Test-LocalTarget([string]$Target, [string]$Source) {
    $target = [Net.WebUtility]::HtmlDecode($Target.Trim([char[]]'<>'))
    if ($target -match '^(https?://|mailto:|//|#)') { return }
    if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { throw "Unsupported link scheme in $Source" }
    $target = [Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
    if ([string]::IsNullOrWhiteSpace($target)) { return }
    if ($target.StartsWith('/')) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $root $target.TrimStart('/')))
    } else {
        $resolved = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($Source)) $target))
    }
    if ($resolved -ne $root -and -not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Link escapes the repository in $Source"
    }
    if (-not (Test-Path -LiteralPath $resolved)) { throw "Missing local link target '$target' in $Source" }
    $script:localTargets++
}

$markdown = @(Get-ChildItem -LiteralPath $root -Filter '*.md' -File)
foreach ($directory in @('docs', '.github')) {
    $path = Join-Path $root $directory
    if (Test-Path -LiteralPath $path) { $markdown += @(Get-ChildItem -LiteralPath $path -Filter '*.md' -File -Recurse) }
}
foreach ($file in $markdown) {
    if ($file.Length -gt 4MB) { throw "Markdown exceeds the documentation budget: $($file.FullName)" }
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $text = [regex]::Replace($text, '(?ms)^```[^\r\n]*\r?\n.*?^```[ \t]*\r?$', '')
    foreach ($match in [regex]::Matches($text, '!?\[[^\]\r\n]*\]\(\s*(?<target><[^>\r\n]+>|[^\s)]+)(?:\s+[^)]*)?\)')) {
        Test-LocalTarget $match.Groups['target'].Value $file.FullName
    }
    foreach ($match in [regex]::Matches($text, '(?i)\b(?:href|src)\s*=\s*["''](?<target>[^"'']+)["'']')) {
        Test-LocalTarget $match.Groups['target'].Value $file.FullName
    }
}

$readmePath = Join-Path $root 'README.md'
$readme = Get-Content -LiteralPath $readmePath -Raw -Encoding UTF8
if ($readme -match '(?:github\.com|img\.shields\.io)/[^\s"<>]*OWNER/REPOSITORY') {
    if ($RequireRepositoryIdentity) { throw 'README repository badge/link placeholders must be replaced before launch.' }
    Write-Warning 'README uses intentional OWNER/REPOSITORY placeholders. Hosted badge status is not configured.'
}
$packageReadme = Join-Path $root 'docs/PACKAGE_README.md'
if (Test-Path -LiteralPath $packageReadme) {
    $packageText = Get-Content -LiteralPath $packageReadme -Raw -Encoding UTF8
    if ($packageText -match '(?i)<img\b|!?\[[^\]\r\n]*\]\(|OWNER/REPOSITORY') {
        throw 'Package README must remain self-contained without images, Markdown links or repository placeholders.'
    }
}

$assets = Join-Path $root 'docs/assets'
$svgs = @()
if (Test-Path -LiteralPath $assets) { $svgs = @(Get-ChildItem -LiteralPath $assets -Filter '*.svg' -File -Recurse) }
$allowedElements = @('svg', 'title', 'desc', 'defs', 'linearGradient', 'radialGradient', 'stop', 'g', 'rect', 'path', 'ellipse', 'circle', 'text', 'tspan')
foreach ($file in $svgs) {
    if ($file.Length -gt 256KB) { throw "SVG exceeds the artwork budget: $($file.FullName)" }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 262144
    $reader = [Xml.XmlReader]::Create($file.FullName, $settings)
    try { $document = [Xml.Linq.XDocument]::Load($reader) } finally { $reader.Dispose() }
    if (@($document.DescendantNodes() | Where-Object { $_ -is [Xml.Linq.XProcessingInstruction] }).Count -ne 0) {
        throw "SVG processing instructions are prohibited: $($file.FullName)"
    }
    $svg = $document.Root
    if ($svg.Name.LocalName -ne 'svg' -or $svg.Name.NamespaceName -ne 'http://www.w3.org/2000/svg') {
        throw "Invalid SVG root: $($file.FullName)"
    }
    if ($svg.Attribute('role').Value -ne 'img' -or [string]::IsNullOrWhiteSpace($svg.Attribute('aria-labelledby').Value)) {
        throw "SVG requires an accessible image role and label references: $($file.FullName)"
    }
    if ($svg.Attribute('width').Value -notmatch '^[1-9][0-9]*$' -or $svg.Attribute('height').Value -notmatch '^[1-9][0-9]*$' -or
        $svg.Attribute('viewBox').Value -notmatch '^0 0 [1-9][0-9]* [1-9][0-9]*$') {
        throw "SVG requires positive dimensions and an explicit viewBox: $($file.FullName)"
    }
    $ids = @{}
    foreach ($element in $svg.DescendantsAndSelf()) {
        if ($element.Name.NamespaceName -ne 'http://www.w3.org/2000/svg' -or $allowedElements -cnotcontains $element.Name.LocalName) {
            throw "Unsupported SVG element: $($file.FullName)"
        }
        if ($element.Attribute('id')) {
            $id = $element.Attribute('id').Value
            if ($ids.ContainsKey($id)) { throw "Duplicate SVG id: $($file.FullName)" }
            $ids[$id] = $element
        }
        foreach ($attribute in $element.Attributes()) {
            if ($attribute.IsNamespaceDeclaration) { continue }
            if ($attribute.Name.LocalName -match '^(on|href$|style$)' -or $attribute.Value -match '(?i)(https?:|data:|javascript:|file:|@import)') {
                throw "Active or external SVG content is prohibited: $($file.FullName)"
            }
            if ($attribute.Value -match '(?i)url\(' -and $attribute.Value -notmatch '^url\(#[A-Za-z_][\w.-]*\)$') {
                throw "Only local SVG paint references are supported: $($file.FullName)"
            }
        }
    }
    foreach ($id in ($svg.Attribute('aria-labelledby').Value -split '\s+')) {
        if (-not $ids.ContainsKey($id) -or $ids[$id].Name.LocalName -cnotin @('title', 'desc') -or [string]::IsNullOrWhiteSpace($ids[$id].Value)) {
            throw "Invalid SVG accessible label: $($file.FullName)"
        }
    }
    foreach ($attribute in $svg.DescendantsAndSelf().Attributes()) {
        if ($attribute.Value -match '^url\(#(?<id>[A-Za-z_][\w.-]*)\)$' -and -not $ids.ContainsKey($Matches['id'])) {
            throw "Unresolved SVG paint reference: $($file.FullName)"
        }
    }
}
Write-Output "Verified $($markdown.Count) Markdown files, $script:localTargets local link targets and $($svgs.Count) self-contained SVG assets."
Write-Output 'Remote URLs, hosted workflow results, Markdown fragment anchors and visual rendering are not validated by this check.'
