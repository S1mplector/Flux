<#
 .SYNOPSIS
  Publishes Flux.Presentation and copies the built executable to the user's Desktop.

 .DESCRIPTION
  Convenience wrapper around publishing Flux.Presentation as a self-contained build (runtime bundled).
  Defaults to single-file output for a portable .exe, with safeguards, colored status output,
  and a Desktop copy step with optional timestamped naming.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true, # bundle runtime
    [bool]$PublishSingleFile = $true, # produce a portable single-file exe
    [bool]$EnableCompressionInSingleFile = $true,
    [bool]$IncludeNativeLibrariesForSelfExtract = $true,
    [bool]$PublishReadyToRun = $false,
    [bool]$PublishTrimmed = $false,
    [string]$OutputPath = "publish",
    [switch]$SkipCopy,
    [switch]$CopyAsZip, # if not single-file, zip the folder when copying
    [switch]$UseTimestamp,
    [switch]$OpenOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info($Message)  { Write-Host "[+] $Message" -ForegroundColor Cyan }
function Write-Warn($Message)  { Write-Host "[!] $Message" -ForegroundColor Yellow }
function Write-ErrorLine($Message) { Write-Host "[x] $Message" -ForegroundColor Red }

function Assert-CommandExists([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available on PATH."
    }
}

function Ensure-Icon {
    param(
        [Parameter(Mandatory)] [string]$PngPath,
        [Parameter(Mandatory)] [string]$IcoPath
    )

    if (Test-Path -LiteralPath $IcoPath) { return }
    if (-not (Test-Path -LiteralPath $PngPath)) {
        throw "Icon source not found at $PngPath"
    }

    Write-Info "Generating .ico from $PngPath -> $IcoPath"

    Add-Type -AssemblyName System.Drawing
    Add-Type -Namespace IconUtil -Name NativeMethods -MemberDefinition @"
using System;
using System.Runtime.InteropServices;
public static class NativeMethods {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

    $bmp = [System.Drawing.Bitmap]::FromFile($PngPath)
    # Normalize to 256x256 for a clean app icon
    if ($bmp.Width -ne 256 -or $bmp.Height -ne 256) {
        $bmp = New-Object System.Drawing.Bitmap($bmp, 256, 256)
    }

    $hIcon = $bmp.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $fs = [IO.File]::Open($IcoPath, [IO.FileMode]::Create)
        try { $icon.Save($fs) } finally { $fs.Dispose() }
        $icon.Dispose()
    } finally {
        if ($bmp) { $bmp.Dispose() }
        [IconUtil.NativeMethods]::DestroyIcon($hIcon) | Out-Null
    }
}

function Get-DesktopPath {
    $desktop = [Environment]::GetFolderPath("Desktop")
    if (-not $desktop) { throw "Unable to resolve the Desktop path for the current user." }
    return $desktop
}

function Get-ProjectRoot {
    if ($PSScriptRoot) { return $PSScriptRoot }
    return Split-Path -Parent $MyInvocation.MyCommand.Path
}

try {
    $root = Get-ProjectRoot
    Set-Location -LiteralPath $root

    Write-Info "Working directory: $root"
    Assert-CommandExists "dotnet"

    $projectPath = Join-Path $root "Flux.Presentation/Flux.Presentation.csproj"
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Project not found at $projectPath"
    }

    $pngIcon = Join-Path $root "Flux.Presentation/Resources/icon.png"
    $icoIcon = Join-Path $root "Flux.Presentation/Resources/icon.ico"
    Ensure-Icon -PngPath $pngIcon -IcoPath $icoIcon

    $publishArgs = @(
        "publish", $projectPath,
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", ($SelfContained.ToString().ToLowerInvariant()),
        "-o", $OutputPath,
        "-p:PublishSingleFile=$($PublishSingleFile.ToString().ToLowerInvariant())",
        "-p:EnableCompressionInSingleFile=$($EnableCompressionInSingleFile.ToString().ToLowerInvariant())",
        "-p:IncludeNativeLibrariesForSelfExtract=$($IncludeNativeLibrariesForSelfExtract.ToString().ToLowerInvariant())",
        "-p:PublishReadyToRun=$($PublishReadyToRun.ToString().ToLowerInvariant())",
        "-p:PublishTrimmed=$($PublishTrimmed.ToString().ToLowerInvariant())",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    Write-Info "Running: dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs

    $exeName = "Flux.Presentation.exe"
    $publishedExe = Join-Path $OutputPath $exeName
    if (-not (Test-Path -LiteralPath $publishedExe)) {
        $firstExe = Get-ChildItem -LiteralPath $OutputPath -Filter *.exe -File -ErrorAction SilentlyContinue | Sort-Object Length -Descending | Select-Object -First 1
        if ($firstExe) {
            Write-Warn "Expected $exeName, using $($firstExe.Name) instead."
            $publishedExe = $firstExe.FullName
        } else {
            throw "No executable found in '$OutputPath'."
        }
    }

    if (-not $SkipCopy) {
        $desktop = Get-DesktopPath
        if ($PublishSingleFile) {
            $copyName = if ($UseTimestamp) { "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([IO.Path]::GetFileName($publishedExe))" } else { [IO.Path]::GetFileName($publishedExe) }
            $destination = Join-Path $desktop $copyName
            Copy-Item -LiteralPath $publishedExe -Destination $destination -Force
            Write-Info "Copied single-file exe to Desktop: $destination"
        } else {
            $destFolderName = if ($UseTimestamp) { "$(Get-Date -Format 'yyyyMMdd-HHmmss')-publish" } else { "publish" }
            $destFolder = Join-Path $desktop $destFolderName
            if (Test-Path $destFolder) { Remove-Item -LiteralPath $destFolder -Recurse -Force -ErrorAction SilentlyContinue }

            if ($CopyAsZip) {
                $zipPath = "$destFolder.zip"
                if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue }
                Compress-Archive -Path (Join-Path $OutputPath '*') -DestinationPath $zipPath -Force
                Write-Info "Copied publish output as zip to Desktop: $zipPath"
            } else {
                Copy-Item -LiteralPath $OutputPath -Destination $destFolder -Recurse -Force
                Write-Info "Copied publish folder to Desktop: $destFolder"
            }
        }
    } else {
        Write-Warn "SkipCopy set; not copying to Desktop."
    }

    if ($OpenOutput) {
        Write-Info "Opening output folder..."
        Start-Process -FilePath "explorer.exe" -ArgumentList (Resolve-Path -LiteralPath $OutputPath)
    }

    Write-Info "Publish completed successfully."
}
catch {
    Write-ErrorLine $_
    exit 1
}
