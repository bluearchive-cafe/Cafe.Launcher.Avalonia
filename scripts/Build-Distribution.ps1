[CmdletBinding()]
param(
    [string]$Tag,
    [string[]]$Rids = @("win-x64"),
    [switch]$SkipPublish,
    [string]$AppImageToolPath,
    [string]$AppImageRuntimePath
)

$ErrorActionPreference = "Stop"

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ArtifactsDir = Join-Path $RootDir "artifacts"
$PublishRoot = Join-Path $ArtifactsDir "publish"
$DistributionDir = Join-Path $ArtifactsDir "distribution"
$BundleRoot = Join-Path $ArtifactsDir "bundle"
$MacOSAssetsDir = Join-Path $RootDir "installer/macos"
$LinuxAssetsDir = Join-Path $RootDir "installer/linux"
$DebianAssetsDir = Join-Path $LinuxAssetsDir "debian"

$version = & (Join-Path $ScriptDir "Read-LauncherVersion.ps1") -Tag $Tag
$ProjectPath = $version.ProjectPath
$Tag = $version.Tag

$allowedRids = @("win-x64", "osx-arm64", "linux-x64")
# Normalize "a,b,c" into separate RIDs: pwsh -File passes comma lists as one literal string.
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$unknownRids = @($Rids | Where-Object { $allowedRids -notcontains $_ })
if ($unknownRids.Count -gt 0) {
    throw "Unsupported RID(s): $($unknownRids -join ', '). Allowed RIDs: $($allowedRids -join ', ')."
}
$Rids = @($Rids | Select-Object -Unique)
$IsUnixHost = $IsLinux -or $IsMacOS

if ($SkipPublish) {
    foreach ($rid in $Rids) {
        $existingPublishDir = Join-Path $PublishRoot $rid
        if (-not (Test-Path -LiteralPath $existingPublishDir -PathType Container) -or
            -not (@(Get-ChildItem -LiteralPath $existingPublishDir -Force).Count -gt 0)) {
            throw "SkipPublish requires an existing publish output at '$existingPublishDir'."
        }
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Set-UnixExecutableBit {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths
    )

    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            & chmod +x $path
            if ($LASTEXITCODE -ne 0) {
                throw "chmod +x failed for '$path'."
            }
        }
    }
}

Remove-Item -LiteralPath $DistributionDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $BundleRoot -Recurse -Force -ErrorAction SilentlyContinue
[void][System.IO.Directory]::CreateDirectory($DistributionDir)
[void][System.IO.Directory]::CreateDirectory($BundleRoot)

if (-not $SkipPublish) {
    Remove-Item -LiteralPath $PublishRoot -Recurse -Force -ErrorAction SilentlyContinue

    foreach ($rid in $Rids) {
        $publishDir = Join-Path $PublishRoot $rid
        [void][System.IO.Directory]::CreateDirectory($publishDir)
        Invoke-Checked "dotnet" @("publish", $ProjectPath, "-c", "Release", "-r", $rid, "-o", $publishDir) "dotnet publish failed for RID '$rid'."
    }
}

$artifacts = @()

if ($Rids -contains "win-x64") {
    $winZipPath = Join-Path $DistributionDir "Cafe.Launcher.Avalonia_${Tag}_win-x64.zip"
    Compress-Archive -Path (Join-Path $PublishRoot "win-x64/*") -DestinationPath $winZipPath -Force
    $artifacts += [pscustomobject]@{ Rid = "win-x64"; Kind = "zip"; Path = $winZipPath }
}

if ($Rids -contains "osx-arm64") {
    $bundleParent = Join-Path $BundleRoot "osx-arm64"
    $appDirectory = Join-Path $bundleParent "Cafe Launcher.app"
    $contentsDir = Join-Path $appDirectory "Contents"
    $macOsDir = Join-Path $contentsDir "MacOS"
    $resourcesDir = Join-Path $contentsDir "Resources"
    [void][System.IO.Directory]::CreateDirectory($macOsDir)
    [void][System.IO.Directory]::CreateDirectory($resourcesDir)

    Copy-Item -Path (Join-Path $PublishRoot "osx-arm64/*") -Destination $macOsDir -Recurse -Force

    $plistTemplate = Get-Content -Raw -LiteralPath (Join-Path $MacOSAssetsDir "Info.plist")
    $plist = $plistTemplate.Replace("{VERSION}", $version.VersionPrefix).Replace("{FILE_VERSION}", $version.FileVersion)
    [System.IO.File]::WriteAllText((Join-Path $contentsDir "Info.plist"), $plist, [System.Text.UTF8Encoding]::new($false))

    $icnsSource = Join-Path $MacOSAssetsDir "app-icon.icns"
    if (Test-Path -LiteralPath $icnsSource) {
        Copy-Item -LiteralPath $icnsSource -Destination (Join-Path $resourcesDir "app-icon.icns") -Force
    }
    else {
        Write-Warning "installer/macos/app-icon.icns is missing; the bundle will have no Dock icon. Run scripts/New-AppIconAssets.ps1 to generate it."
    }

    if ($IsUnixHost) {
        Set-UnixExecutableBit -Paths @(
            (Join-Path $macOsDir "Cafe.Launcher.Avalonia"),
            (Join-Path $macOsDir "createdump")
        )

        $osxZipPath = Join-Path $DistributionDir "Cafe.Launcher.Avalonia_${Tag}_osx-arm64.zip"
        Push-Location $bundleParent
        try {
            & zip -ry $osxZipPath "Cafe Launcher.app"
            if ($LASTEXITCODE -ne 0) {
                throw "zip failed for the macOS bundle."
            }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Warning "Non-Unix host: the macOS bundle is created without executable bits and falls back to Compress-Archive. Build the macOS archive from Linux/macOS for release (the release workflow does this)."
        $osxZipPath = Join-Path $DistributionDir "Cafe.Launcher.Avalonia_${Tag}_osx-arm64.zip"
        Compress-Archive -Path $appDirectory -DestinationPath $osxZipPath -Force
    }

    $artifacts += [pscustomobject]@{ Rid = "osx-arm64"; Kind = "app-bundle-zip"; Path = $osxZipPath }
}

if ($Rids -contains "linux-x64") {
    $linuxPublishDir = Join-Path $PublishRoot "linux-x64"
    $linuxTarName = "Cafe.Launcher.Avalonia_${Tag}_linux-x64.tar.gz"
    $linuxTarPath = Join-Path $DistributionDir $linuxTarName
    if (-not $IsUnixHost) {
        Write-Warning "Non-Unix host: the tar.gz may lose executable bits. Build the Linux archive from Linux for release (the release workflow does this)."
    }
    # Pack with a relative archive name: GNU tar reads "E:\...\x.tar.gz" as a remote host path.
    Push-Location $DistributionDir
    try {
        & tar -czf $linuxTarName -C $linuxPublishDir .
        if ($LASTEXITCODE -ne 0) {
            throw "tar failed for the Linux package."
        }
    }
    finally {
        Pop-Location
    }
    $artifacts += [pscustomobject]@{ Rid = "linux-x64"; Kind = "tar-gz"; Path = $linuxTarPath }

    if ($IsLinux) {
        $debRoot = Join-Path $BundleRoot "linux-x64/deb-root"
        $debControlDir = Join-Path $debRoot "DEBIAN"
        $debAppDir = Join-Path $debRoot "opt/cafe-launcher"
        $debBinDir = Join-Path $debRoot "usr/bin"
        $debApplicationsDir = Join-Path $debRoot "usr/share/applications"
        $debIcon256Dir = Join-Path $debRoot "usr/share/icons/hicolor/256x256/apps"
        $debIcon512Dir = Join-Path $debRoot "usr/share/icons/hicolor/512x512/apps"
        foreach ($directory in @(
            $debControlDir,
            $debAppDir,
            $debBinDir,
            $debApplicationsDir,
            $debIcon256Dir,
            $debIcon512Dir
        )) {
            [void][System.IO.Directory]::CreateDirectory($directory)
        }

        Copy-Item -Path (Join-Path $linuxPublishDir "*") -Destination $debAppDir -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $DebianAssetsDir "cafe-launcher") -Destination (Join-Path $debBinDir "cafe-launcher") -Force
        Copy-Item -LiteralPath (Join-Path $DebianAssetsDir "cafe-launcher.desktop") -Destination (Join-Path $debApplicationsDir "cafe-launcher.desktop") -Force

        foreach ($size in @(256, 512)) {
            $iconSource = Join-Path $LinuxAssetsDir "app-icon-$size.png"
            if (-not (Test-Path -LiteralPath $iconSource -PathType Leaf)) {
                throw "Debian packaging requires installer/linux/app-icon-$size.png. Run scripts/New-AppIconAssets.ps1 to regenerate it."
            }

            $iconDestinationDir = if ($size -eq 256) { $debIcon256Dir } else { $debIcon512Dir }
            Copy-Item -LiteralPath $iconSource -Destination (Join-Path $iconDestinationDir "cafe-launcher.png") -Force
        }

        $debianVersion = [regex]::Replace($version.VersionPrefix, "-", "~", 1)
        $controlTemplate = Get-Content -Raw -LiteralPath (Join-Path $DebianAssetsDir "control")
        $control = $controlTemplate.Replace("{VERSION}", $debianVersion)
        [System.IO.File]::WriteAllText(
            (Join-Path $debControlDir "control"),
            $control,
            [System.Text.UTF8Encoding]::new($false))

        Set-UnixExecutableBit -Paths @(
            (Join-Path $debAppDir "Cafe.Launcher.Avalonia"),
            (Join-Path $debAppDir "createdump"),
            (Join-Path $debBinDir "cafe-launcher")
        )

        $debPath = Join-Path $DistributionDir "Cafe.Launcher.Avalonia_${Tag}_linux-x64.deb"
        Invoke-Checked "dpkg-deb" @(
            "--root-owner-group",
            "--build",
            $debRoot,
            $debPath
        ) "dpkg-deb failed for the Linux Debian package."
        Invoke-Checked "dpkg-deb" @("--info", $debPath) "The generated Debian package metadata is invalid."
        $artifacts += [pscustomobject]@{ Rid = "linux-x64"; Kind = "deb"; Path = $debPath }

        if ([string]::IsNullOrWhiteSpace($AppImageToolPath)) {
            Write-Warning "AppImage packaging skipped: pass -AppImageToolPath pointing at a Linux appimagetool build to produce the AppImage."
        }
        else {
            if ([string]::IsNullOrWhiteSpace($AppImageRuntimePath) -or
                -not (Test-Path -LiteralPath $AppImageRuntimePath -PathType Leaf)) {
                throw "AppImage packaging requires -AppImageRuntimePath pointing at a pinned type2 runtime file."
            }

            $appDirRoot = Join-Path $BundleRoot "linux-x64/AppDir"
            $appBinDir = Join-Path $appDirRoot "usr/bin"
            $hicolorDir = Join-Path $appDirRoot "usr/share/icons/hicolor/256x256/apps"
            [void][System.IO.Directory]::CreateDirectory($appBinDir)
            [void][System.IO.Directory]::CreateDirectory($hicolorDir)

            Copy-Item -Path (Join-Path $linuxPublishDir "*") -Destination $appBinDir -Recurse -Force
            Set-UnixExecutableBit -Paths @(
                (Join-Path $appBinDir "Cafe.Launcher.Avalonia"),
                (Join-Path $appBinDir "createdump")
            )

            $appRunPath = Join-Path $appDirRoot "AppRun"
            Copy-Item -LiteralPath (Join-Path $LinuxAssetsDir "AppRun") -Destination $appRunPath -Force
            Copy-Item -LiteralPath (Join-Path $LinuxAssetsDir "cafe-launcher.desktop") -Destination (Join-Path $appDirRoot "cafe-launcher.desktop") -Force
            Set-UnixExecutableBit -Paths @($appRunPath)
            Invoke-Checked "test" @("-x", $appRunPath) "AppDir/AppRun is missing or is not executable."

            $iconSource = Join-Path $LinuxAssetsDir "app-icon-256.png"
            if (Test-Path -LiteralPath $iconSource) {
                Copy-Item -LiteralPath $iconSource -Destination (Join-Path $hicolorDir "cafe-launcher.png") -Force
                Copy-Item -LiteralPath $iconSource -Destination (Join-Path $appDirRoot "cafe-launcher.png") -Force
                New-Item -ItemType SymbolicLink -Path (Join-Path $appDirRoot ".DirIcon") -Target "cafe-launcher.png" -Force | Out-Null
            }
            else {
                Write-Warning "installer/linux/app-icon-256.png is missing; the AppImage will have no icon. Run scripts/New-AppIconAssets.ps1 to generate it."
            }

            $appImagePath = Join-Path $DistributionDir "Cafe.Launcher.Avalonia_${Tag}_linux-x64.AppImage"
            Invoke-Checked $AppImageToolPath @(
                "--appimage-extract-and-run",
                "--runtime-file",
                $AppImageRuntimePath,
                $appDirRoot,
                $appImagePath
            ) "appimagetool failed for the Linux AppImage."
            Set-UnixExecutableBit -Paths @($appImagePath)

            # Exercise the AppImage runtime and its root AppRun entry point. The
            # --version path exits before Avalonia starts, so this works on a
            # headless CI runner while still loading the packaged .NET apphost.
            Invoke-Checked $appImagePath @(
                "--appimage-extract-and-run",
                "--version"
            ) "The generated AppImage failed its startup smoke test."
            $artifacts += [pscustomobject]@{ Rid = "linux-x64"; Kind = "AppImage"; Path = $appImagePath }
        }
    }
}

foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf)) {
        throw "Expected artifact was not created: $($artifact.Path)"
    }
}

$artifacts
