[CmdletBinding()]
param(
    [Parameter()]
    [string]$SuiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    [Parameter()]
    [string]$ManifestPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $SuiteRoot 'Catalog\mtk-assets.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest does not exist: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported manifest schemaVersion: $($manifest.schemaVersion)"
}

$suiteRootFull = [IO.Path]::GetFullPath($SuiteRoot)
$suitePrefix = $suiteRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$checked = 0

foreach ($model in @($manifest.models)) {
    foreach ($asset in @($model.assets)) {
        if ($asset.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
            throw "Invalid SHA-256 for asset: $($asset.importedRelativePath)"
        }

        $relative = [string]$asset.importedRelativePath
        $candidate = [IO.Path]::GetFullPath((Join-Path $suiteRootFull $relative))
        if (-not $candidate.StartsWith($suitePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Asset path escapes suite root: $relative"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Imported asset is missing: $relative"
        }

        $file = Get-Item -LiteralPath $candidate
        if ($file.Length -ne [int64]$asset.length) {
            throw "Length mismatch for ${relative}: expected $($asset.length), got $($file.Length)"
        }

        $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
        if (-not $hash.Equals([string]$asset.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "SHA-256 mismatch for $relative"
        }

        $checked++
    }
}

Write-Host "Validated $checked MTK asset entries from $ManifestPath"
