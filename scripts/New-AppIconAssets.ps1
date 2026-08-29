[CmdletBinding()]
param(
    [string]$SourcePath
)

# One-shot Windows tool: regenerates the committed icon assets used by the
# macOS bundle (installer/macos/app-icon.icns) and the Linux AppImage
# (installer/linux/app-icon-*.png) from Assets/app-icon-source.jpg.
# Run it after changing the source artwork and commit the outputs.

$ErrorActionPreference = "Stop"

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $RootDir "src/Cafe.Launcher.Avalonia/Assets/app-icon-source.jpg"
}

$MacOSOutputDir = Join-Path $RootDir "installer/macos"
$LinuxOutputDir = Join-Path $RootDir "installer/linux"

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Icon source image was not found: $SourcePath"
}

Add-Type -AssemblyName System.Drawing

function Get-ResizedPngBytes {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,
        [Parameter(Mandatory)]
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.Clear([System.Drawing.Color]::Transparent)

            $side = [Math]::Min($Source.Width, $Source.Height)
            $sourceX = ($Source.Width - $side) / 2
            $sourceY = ($Source.Height - $side) / 2
            $destination = [System.Drawing.RectangleF]::new(0, 0, $Size, $Size)
            $sourceRect = [System.Drawing.RectangleF]::new($sourceX, $sourceY, $side, $side)
            $graphics.DrawImage($Source, $destination, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return , $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-BigEndianUInt32Bytes {
    param([Parameter(Mandatory)][uint32]$Value)

    $bytes = [System.BitConverter]::GetBytes($Value)
    if ([System.BitConverter]::IsLittleEndian) {
        [array]::Reverse($bytes)
    }
    return , $bytes
}

function Save-Icns {
    param(
        [Parameter(Mandatory)]
        [hashtable]$PngBytesBySize,
        [Parameter(Mandatory)]
        [string]$OutputPath
    )

    $icnsEntries = @(
        @{ Type = "ic07"; Size = 128 },
        @{ Type = "ic08"; Size = 256 },
        @{ Type = "ic09"; Size = 512 },
        @{ Type = "ic10"; Size = 1024 },
        @{ Type = "ic11"; Size = 32 },
        @{ Type = "ic12"; Size = 64 },
        @{ Type = "ic13"; Size = 256 },
        @{ Type = "ic14"; Size = 512 }
    )

    $chunks = New-Object System.Collections.Generic.List[byte[]]
    $totalLength = [uint32]8
    foreach ($entry in $icnsEntries) {
        $pngBytes = $PngBytesBySize[[int]$entry.Size]
        $chunkLength = [uint32]($pngBytes.Length + 8)
        $chunk = New-Object System.Collections.Generic.List[byte]
        $chunk.AddRange([System.Text.Encoding]::ASCII.GetBytes($entry.Type))
        $chunk.AddRange((Get-BigEndianUInt32Bytes -Value $chunkLength))
        $chunk.AddRange($pngBytes)
        $chunks.Add($chunk.ToArray())
        $totalLength += $chunkLength
    }

    $icns = New-Object System.Collections.Generic.List[byte]
    $icns.AddRange([System.Text.Encoding]::ASCII.GetBytes("icns"))
    $icns.AddRange((Get-BigEndianUInt32Bytes -Value $totalLength))
    foreach ($chunk in $chunks) {
        $icns.AddRange($chunk)
    }

    [System.IO.File]::WriteAllBytes($OutputPath, $icns.ToArray())
}

$sourceImage = [System.Drawing.Image]::FromFile($SourcePath)
try {
    $pngBytesBySize = @{}
    foreach ($size in @(32, 64, 128, 256, 512, 1024)) {
        $pngBytesBySize[$size] = Get-ResizedPngBytes -Source $sourceImage -Size $size
    }

    [void][System.IO.Directory]::CreateDirectory($MacOSOutputDir)
    [void][System.IO.Directory]::CreateDirectory($LinuxOutputDir)

    $icnsPath = Join-Path $MacOSOutputDir "app-icon.icns"
    Save-Icns -PngBytesBySize $pngBytesBySize -OutputPath $icnsPath

    foreach ($size in @(256, 512)) {
        $pngPath = Join-Path $LinuxOutputDir "app-icon-$size.png"
        [System.IO.File]::WriteAllBytes($pngPath, $pngBytesBySize[$size])
        Write-Host "Wrote $pngPath"
    }

    Write-Host "Wrote $icnsPath"
}
finally {
    $sourceImage.Dispose()
}
