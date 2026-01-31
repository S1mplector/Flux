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
    [bool]$UseFriendlyName = $true,
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

function Ensure-DotnetOnPath {
    $userDotnetRoot = Join-Path $env:USERPROFILE ".dotnet"
    $userDotnetExe = Join-Path $userDotnetRoot "dotnet.exe"
    if (Test-Path -LiteralPath $userDotnetExe) {
        $env:DOTNET_ROOT = $userDotnetRoot
        if ($env:PATH -notmatch [regex]::Escape($userDotnetRoot)) {
            $env:PATH = "$userDotnetRoot;$env:PATH"
        }
    }
}

function Ensure-Icon {
    param(
        [Parameter(Mandatory)] [string]$PngPath,
        [Parameter(Mandatory)] [string]$IcoPath
    )

    if (-not (Test-Path -LiteralPath $PngPath)) {
        throw "Icon source not found at $PngPath"
    }

    Write-Info "Generating multi-size .ico from $PngPath -> $IcoPath"

    Add-Type -AssemblyName System.Drawing
    $sizes = 16,24,32,48,64,128,256
    $bitmaps = @()
    $pngStreams = @()

    $src = [System.Drawing.Bitmap]::FromFile($PngPath)
    $maxDim = [Math]::Max($src.Width, $src.Height)
    $baseBitmap = New-Object System.Drawing.Bitmap($maxDim, $maxDim, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $baseGraphics = [System.Drawing.Graphics]::FromImage($baseBitmap)
    $baseGraphics.Clear([System.Drawing.Color]::Transparent)
    $baseGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $baseGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $baseGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

    $scale = [Math]::Min($maxDim / $src.Width, $maxDim / $src.Height)
    $drawW = [int][Math]::Round($src.Width * $scale)
    $drawH = [int][Math]::Round($src.Height * $scale)
    $offsetX = [int](($maxDim - $drawW) / 2)
    $offsetY = [int](($maxDim - $drawH) / 2)
    $baseGraphics.DrawImage($src, $offsetX, $offsetY, $drawW, $drawH)
    $baseGraphics.Dispose()
    $src.Dispose()

    try {
        foreach ($size in $sizes) {
            $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.DrawImage($baseBitmap, 0, 0, $size, $size)
            $g.Dispose()

            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngStreams += [PSCustomObject]@{ Size = $size; Bytes = $ms.ToArray() }

            $ms.Dispose()
            $bmp.Dispose()
        }

        $fs = [IO.File]::Open($IcoPath, [IO.FileMode]::Create)
        $bw = New-Object System.IO.BinaryWriter($fs)

        $bw.Write([UInt16]0)      # reserved
        $bw.Write([UInt16]1)      # type: icon
        $bw.Write([UInt16]$pngStreams.Count) # count

        $offset = 6 + (16 * $pngStreams.Count)
        foreach ($entry in $pngStreams) {
            $s = $entry.Size
            $bytes = $entry.Bytes
            $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s })) # width
            $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s })) # height
            $bw.Write([byte]0) # color count
            $bw.Write([byte]0) # reserved
            $bw.Write([UInt16]1) # planes
            $bw.Write([UInt16]32) # bit count
            $bw.Write([UInt32]$bytes.Length) # size
            $bw.Write([UInt32]$offset) # offset
            $offset += $bytes.Length
        }

        foreach ($entry in $pngStreams) {
            $bw.Write($entry.Bytes)
        }

        $bw.Flush()
        $bw.Dispose()
        $fs.Dispose()
    }
    finally {
        if ($baseBitmap) { $baseBitmap.Dispose() }
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
        [Parameter(Mandatory)] [string]$ProjectPath
    )

    $product = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "Product"
    if (-not $product) { $product = "Flux" }

    $version = $null
    try {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($PublishedExe)
        if ($info -and $info.ProductVersion) {
            $version = $info.ProductVersion.Split('+')[0]
        }
    } catch { }

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

try {
    $root = Get-ProjectRoot
    Set-Location -LiteralPath $root

    Write-Info "Working directory: $root"
    Ensure-DotnetOnPath
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
            $baseName = [IO.Path]::GetFileName($publishedExe)
            if ($UseFriendlyName) {
                $baseName = Get-FriendlyExeName -PublishedExe $publishedExe -ProjectPath $projectPath
            }
            $copyName = if ($UseTimestamp) { "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$baseName" } else { $baseName }
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
