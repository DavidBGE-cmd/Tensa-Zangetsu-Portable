[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter()]
    [string]$SuiteRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    [Parameter()]
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source directory does not exist: $Source"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $SuiteRoot 'Catalog\unlock-models.json'
}

function Get-Text($Object, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value) {
            $value = [string]$property.Value
            if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
        }
    }
    return $null
}

function Get-Brand([IO.FileInfo]$File, $Profile) {
    $value = Get-Text $Profile @('brand', 'Brand')
    if ($value) { return $value }
    $parts = [IO.Path]::GetRelativePath($Source, $File.DirectoryName) -split '[\\/]'
    return @($parts | Where-Object { $_ -and $_ -notin @('data', 'COLOKTOOL', 'PL') })[0]
}

$entries = [Collections.Generic.List[object]]::new()
$files = Get-ChildItem -LiteralPath $Source -Filter 'model.json' -File -Recurse -Force
foreach ($file in $files) {
    try {
        $profile = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $model = Get-Text $profile @('model', 'Models', 'Devices', 'deviceName')
        if (-not $model) { continue }

        $parametersProperty = $profile.PSObject.Properties['parameters']
        $parameters = if ($null -ne $parametersProperty) { $parametersProperty.Value } else { $null }
        $loader = if ($null -ne $parameters) { Get-Text $parameters @('edl', 'da', 'loader') } else { $null }
        $preloader = Get-Text $profile @('Broom', 'preloader')
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $id = (($model -replace '[^A-Za-z0-9]+', '-').Trim('-')).ToLowerInvariant()

        $entries.Add([ordered]@{
            id = $id
            brand = Get-Brand $file $profile
            model = $model
            deviceName = Get-Text $profile @('deviceName', 'Devices')
            chipset = Get-Text $profile @('chipset')
            platform = Get-Text $profile @('platform', 'Platform')
            mode = Get-Text $profile @('mode')
            connection = Get-Text $profile @('connection', 'Conn')
            loaderName = $loader
            preloaderName = $preloader
            profilePath = $relative
            profileSha256 = $hash
            verificationStatus = 'IndexedUnverified'
        })
    }
    catch {
        Write-Warning "Skipping invalid model profile: $($file.FullName)"
    }
}

$models = @(
    $entries |
        Group-Object { "$($_.brand)|$($_.model)|$($_.profilePath)" } |
        ForEach-Object { $_.Group[0] }
)
$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    sourceRoot = [IO.Path]::GetFullPath($Source)
    models = $models
    diagnostics = @(
        'This index contains descriptive metadata only; no binary was copied or executed.'
        'Model names, loader names and third-party claims do not prove compatibility.'
        'Entries remain IndexedUnverified until HW_CODE, stage, security, resource hash and hardware evidence match.'
        'FRP, account removal, security bypass and destructive operation files are intentionally excluded.'
    )
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$temp = "$OutputPath.$PID.tmp"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temp -Encoding UTF8
Move-Item -LiteralPath $temp -Destination $OutputPath -Force
Write-Host "Indexed $($models.Count) model profiles into $OutputPath"
