$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '此样张使用 Windows 字体和 System.Drawing，请在 Windows 上导出。' }

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$previousExport = $env:CAFE_SELECT_PREVIEW_EXPORT
Push-Location -LiteralPath $repository
try {
    $env:CAFE_SELECT_PREVIEW_EXPORT = '1'
    & pwsh ./test.ps1 -Suite Headless -FilterMethod '*SelectPreview_RealSettingsRows*'
    if ($LASTEXITCODE -ne 0) { throw '真实控件样张验证失败，未更新交付图片。' }

    Add-Type -AssemblyName System.Drawing
    $captureDirectory = Join-Path $repository '.artifacts/settings-select-md3'
    $imageDirectory = Join-Path $PSScriptRoot 'images'
    New-Item -ItemType Directory -Path $imageDirectory -Force | Out-Null
    foreach ($state in @('closed', 'motion', 'source', 'source-focus', 'source-hover')) {
        $light = [System.Drawing.Image]::FromFile((Join-Path $captureDirectory "light-$state.png"))
        $dark = [System.Drawing.Image]::FromFile((Join-Path $captureDirectory "dark-$state.png"))
        $board = [System.Drawing.Bitmap]::new($light.Width + $dark.Width, $light.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($board)
        try {
            # Pixel-for-pixel contact sheet: left light, right dark; no resizing or repainting controls.
            $graphics.DrawImageUnscaled($light, 0, 0)
            $graphics.DrawImageUnscaled($dark, $light.Width, 0)
            $board.Save((Join-Path $imageDirectory "$state-light-dark.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $board.Dispose()
            $light.Dispose()
            $dark.Dispose()
        }
    }
}
finally {
    $env:CAFE_SELECT_PREVIEW_EXPORT = $previousExport
    Pop-Location
}
