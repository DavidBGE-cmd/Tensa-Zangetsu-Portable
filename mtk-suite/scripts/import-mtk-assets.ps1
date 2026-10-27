[CmdletBinding()]
param(
    [Parameter()]
    [string]$Source = 'D:\dbge\Documents',

    [Parameter()]
    [string]$SuiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source directory does not exist: $Source"
}

$assetsRoot = Join-Path $SuiteRoot 'Assets\devices'
$manifestPath = Join-Path $SuiteRoot 'Catalog\mtk-assets.json'
New-Item -ItemType Directory -Force -Path $assetsRoot, (Split-Path $manifestPath) | Out-Null

function Get-SafeSegment([string]$Value) {
    $segment = [IO.Path]::GetFileName($Value)
    if ([string]::IsNullOrWhiteSpace($segment)) { return '_unknown' }
    $invalid = [IO.Path]::GetInvalidFileNameChars()
    foreach ($char in $invalid) { $segment = $segment.Replace([string]$char, '_') }
    return $segment.Trim()
}

function Get-AssetKind([IO.FileInfo]$File) {
    $name = $File.Name.ToLowerInvariant()
    $ext = $File.Extension.ToLowerInvariant()
    if ($name -match '(^|[-_. ])(da|download.?agent|mtk.?da)([-_. ]|$)' -or $name -match 'auth.?da') { return 'da' }
    if ($name -match 'preloader' -or $name -match 'pl\.bin') { return 'preloader' }
    if ($name -match 'emi|bloader|dram') { return 'emi' }
    if ($ext -eq '.txt' -and $name -match 'scatter') { return 'scatter' }
    if ($ext -in '.auth', '.sig', '.cert', '.pem') { return 'auth' }
    if ($ext -in '.bin', '.elf', '.mbn', '.da') { return 'binary' }
    if ($ext -in '.xml', '.json', '.txt', '.cfg', '.ini') { return 'metadata' }
    return 'unknown'
}

function Get-ModelSegment([IO.FileInfo]$File, [string]$Root) {
    $relative = [IO.Path]::GetRelativePath($Root, $File.DirectoryName)
    $parts = $relative -split '[\\/]'
    $candidates = @($parts | Where-Object {
        $_ -and $_ -notmatch '^(assets?|loaders?|preloader|da|auth|firmware|files?)$'
    })
    if ($candidates.Count -gt 0) { return Get-SafeSegment $candidates[-1] }
    return '_unassigned'
}

$files = Get-ChildItem -LiteralPath $Source -File -Recurse -Force |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' }

$entries = [Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $kind = Get-AssetKind $file
    $model = Get-ModelSegment $file $Source
    $sourceRelative = [IO.Path]::GetRelativePath($Source, $file.FullName)
    $targetModelRoot = Join-Path $assetsRoot $model

    switch ($kind) {
        'da'         { $targetFolder = 'loaders'; $targetName = 'DA_' + $file.Name }
        'preloader'  { $targetFolder = 'loaders'; $targetName = 'preloader_' + $file.Name }
        'emi'        { $targetFolder = 'loaders'; $targetName = 'emi_' + $file.Name }
        'scatter'    { $targetFolder = 'firmware'; $targetName = $file.Name }
        'auth'       { $targetFolder = 'auth'; $targetName = $file.Name }
        default      { $targetFolder = 'inventory'; $targetName = $file.Name }
    }

    $targetFolderPath = Join-Path $targetModelRoot $targetFolder
    New-Item -ItemType Directory -Force -Path $targetFolderPath | Out-Null
    $targetPath = Join-Path $targetFolderPath $targetName
    if ((Test-Path -LiteralPath $targetPath) -and -not $Rebuild) {
        $targetPath = Join-Path $targetFolderPath ($file.BaseName + '_' + $file.Length + $file.Extension)
    }

    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force:$Rebuild
    $entries.Add([ordered]@{
        model = $model
        kind = $kind
        sourceRelativePath = $sourceRelative
        importedRelativePath = [IO.Path]::GetRelativePath($SuiteRoot, $targetPath)
        length = $file.Length
        sha256 = $hash
        verificationStatus = 'IndexedUnverified'
    })
}

$models = @($entries | Group-Object model | ForEach-Object {
    [ordered]@{
        id = $_.Name
        displayName = $_.Name
        verificationStatus = 'IndexedUnverified'
        assets = @($_.Group)
    }
})

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    sourceRoot = $Source
    models = $models
    diagnostics = @(
        'Model folders are derived from source directory names and require manual review.'
        'File names and hashes do not prove DA, preloader, EMI or device compatibility.'
        'No binary was executed and no source file was modified.'
    )
}

$temp = "$manifestPath.$PID.tmp"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temp -Encoding UTF8
Move-Item -LiteralPath $temp -Destination $manifestPath -Force

Write-Host "Imported $($entries.Count) files into $assetsRoot"
Write-Host "Models/index groups: $($models.Count)"
Write-Host "Manifest: $manifestPath"
