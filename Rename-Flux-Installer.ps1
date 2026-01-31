<#
 .SYNOPSIS
  Renames a Flux installer/exe to "Flux vX.X.X.exe" using project metadata.

 .DESCRIPTION
  Convenience utility for renaming an existing Flux.Presentation.exe on disk.
  It prefers the file's ProductVersion, then falls back to BaseVersion/Version from the csproj.
#>
[CmdletBinding()]
param(
    [string]$ExePath = "$env:USERPROFILE\\Desktop\\Flux.Presentation.exe",
    [string]$ProjectPath = "Flux.Presentation\\Flux.Presentation.csproj",
    [string]$VersionOverride,
    [switch]$UseTimestamp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ProjectPropertyValue {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$PropertyName
    )

    if (-not (Test-Path -LiteralPath $ProjectPath)) { return $null }
    try {
        [xml]$proj = Get-Content -LiteralPath $ProjectPath -Raw
        foreach ($pg in $proj.Project.PropertyGroup) {
            $value = $pg.$PropertyName
            if ($value) { return [string]$value }
        }
    } catch { }
    return $null
}

function Get-FriendlyExeName {
    param(
        [Parameter(Mandatory)] [string]$PublishedExe,
        [Parameter(Mandatory)] [string]$ProjectPath,
        [string]$VersionOverride
    )

    $product = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "Product"
    if (-not $product) { $product = "Flux" }

    $version = $VersionOverride
    if (-not $version) {
        try {
            $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($PublishedExe)
            if ($info -and $info.ProductVersion) {
                $version = $info.ProductVersion.Split('+')[0]
            }
        } catch { }
    }
    if (-not $version) {
        $version = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "BaseVersion"
        if (-not $version) {
            $version = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "Version"
        }
        if ($version -and $version -match '^\$\(') { $version = $null }
    }
    if (-not $version) { $version = "0.0.0" }

    return "$product v$version.exe"
}

$exe = Resolve-Path -LiteralPath $ExePath -ErrorAction Stop
$projectFull = Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop

$baseName = Get-FriendlyExeName -PublishedExe $exe -ProjectPath $projectFull -VersionOverride $VersionOverride
if ($UseTimestamp) {
    $baseName = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$baseName"
}

$dest = Join-Path (Split-Path -Parent $exe) $baseName
Move-Item -LiteralPath $exe -Destination $dest -Force
Write-Host "[+] Renamed to: $dest" -ForegroundColor Cyan
