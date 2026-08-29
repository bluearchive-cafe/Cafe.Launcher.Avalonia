namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallerContractTests
{
    [Fact]
    public void IssInstaller_IsUtf8WithBomForLocalizedStrings()
    {
        // The installer script and every per-language file hold localized text.
        foreach (var relativePath in new[]
        {
            "installer/Cafe.Launcher.Avalonia.iss",
            "installer/lang/CustomMessages.en.isl",
            "installer/lang/CustomMessages.zh.isl",
            "installer/lang/CustomMessages.ja.isl",
        })
        {
            var bytes = File.ReadAllBytes(GetProjectFilePath(relativePath));

            Assert.True(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{relativePath} must be UTF-8 with BOM because it contains localized strings.");
        }
    }

    [Fact]
    public void IssInstaller_DeclaresRequiredDefines()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("#ifndef APP_VERSION", script, StringComparison.Ordinal);
        Assert.Contains("#error \"APP_VERSION is required.\"", script, StringComparison.Ordinal);
        Assert.Contains("#ifndef APP_FILE_VERSION", script, StringComparison.Ordinal);
        Assert.Contains("#error \"APP_FILE_VERSION is required.\"", script, StringComparison.Ordinal);
        Assert.Contains("#ifndef PUBLISH_GLOB", script, StringComparison.Ordinal);
        Assert.Contains("#error \"PUBLISH_GLOB is required.\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_LocalizedMessagesLiveInPerLanguageTranslationFiles()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // Script-level [CustomMessages] is language-independent global text where
        // the LAST entry wins for every language; no localized text may live in
        // the script, and "Languages:" is not a supported [CustomMessages] scope.
        Assert.DoesNotContain("[CustomMessages]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("; Languages:", script, StringComparison.Ordinal);
        foreach (var rawLine in script.Split('\n'))
        {
            Assert.False(
                ContainsCjk(rawLine.Trim()),
                "The installer script must not contain localized text; use installer/lang/CustomMessages.*.isl.");
        }

        // The [Languages] section wires in the per-language translation files.
        Assert.Contains("lang\\CustomMessages.en.isl", script, StringComparison.Ordinal);
        Assert.Contains("lang\\CustomMessages.zh.isl", script, StringComparison.Ordinal);
        Assert.Contains("lang\\CustomMessages.ja.isl", script, StringComparison.Ordinal);

        foreach (var file in new[]
        {
            "installer/lang/CustomMessages.en.isl",
            "installer/lang/CustomMessages.zh.isl",
            "installer/lang/CustomMessages.ja.isl",
        })
        {
            var content = ReadProjectFile(file);

            Assert.Contains("DeleteDataQuestion=", content, StringComparison.Ordinal);
            Assert.Contains("InvalidInstallLocation=", content, StringComparison.Ordinal);
            Assert.Contains("PreviousUninstallFailed=", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IssInstaller_UsesConfirmedMachineWideIdentity()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("AppName=Cafe Launcher", script, StringComparison.Ordinal);
        Assert.Contains("AppPublisher=BlueArchive Cafe", script, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=admin", script, StringComparison.Ordinal);
        // Per-user data deletion on uninstall is intended (with explicit consent);
        // UsedUserAreasWarning=no documents that choice and keeps ISCC output clean.
        Assert.Contains("UsedUserAreasWarning=no", script, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={code:ResolveDefaultDir}", script, StringComparison.Ordinal);
        // The default directory is the detected previous install, so the
        // "folder already exists" confirmation would fire on every upgrade.
        Assert.Contains("DirExistsWarning=no", script, StringComparison.Ordinal);
        Assert.Contains("Result := ExpandConstant('{autopf}\\Cafe Launcher')", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", script, StringComparison.Ordinal);
        Assert.Contains("UninstallDisplayName=Cafe Launcher", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivilegesRequired=lowest", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_UsesStableGuidAppId()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Matches(@"AppId=\{\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}", script);
        Assert.Contains("NEVER change AppId", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_ProvidesASelectableDesktopShortcut()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("[Tasks]", script, StringComparison.Ordinal);
        Assert.Contains("Name: \"desktopicon\"", script, StringComparison.Ordinal);
        Assert.Contains("Flags: unchecked", script, StringComparison.Ordinal);
        Assert.Contains("Tasks: desktopicon", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_AlwaysOverwritesPublishedFilesOnUpgrade()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains(
            "Source: \"{#PUBLISH_GLOB}\"; DestDir: \"{app}\"; Flags: recursesubdirs ignoreversion",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("uninsneveruninstall", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IssInstaller_CannotTouchSiblingGameDirectory()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // The uninstaller only removes files it installed plus the ownership
        // marker; there must be no [UninstallDelete] entry above {app} level.
        Assert.DoesNotContain("YostarGames", script, StringComparison.Ordinal);
        Assert.DoesNotContain("RMDir", script, StringComparison.Ordinal);
        Assert.Contains(
            "Type: files; Name: \"{app}\\.cafe-launcher-install\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IssUninstaller_PreservesApplicationDataUnlessExplicitlySelected()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("ShouldDeleteUserData", script, StringComparison.Ordinal);
        Assert.Contains(
            "Type: filesandordirs; Name: \"{localappdata}\\Cafe Launcher\"; Check: ShouldDeleteUserData",
            script,
            StringComparison.Ordinal);
        Assert.Contains("DeleteApplicationData := False", script, StringComparison.Ordinal);
        Assert.Contains("UninstallSilent", script, StringComparison.Ordinal);
        Assert.Contains("MB_YESNO", script, StringComparison.Ordinal);

        var checkStart = script.IndexOf("function ShouldDeleteUserData", StringComparison.Ordinal);
        var checkEnd = script.IndexOf("end;", checkStart, StringComparison.Ordinal);
        var checkFunction = script[checkStart..(checkEnd + "end;".Length)];
        Assert.DoesNotContain("UninstallSilent", checkFunction, StringComparison.Ordinal);
        Assert.Contains("Result := DeleteApplicationData", checkFunction, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_BlocksFileChangesWhileLauncherIsRunning()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("Local\\Cafe_Launcher_SI", script, StringComparison.Ordinal);
        Assert.Contains("AppMutex={#APP_MUTEX}", script, StringComparison.Ordinal);
        Assert.Contains("CloseApplications=no", script, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CloseApplications=yes", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_CleansStaleRegistrationWhenOldUninstallerIsMissing()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Cafe.Launcher.Avalonia",
            script,
            StringComparison.Ordinal);
        Assert.Contains("RegQueryStringValue(HKLM", script, StringComparison.Ordinal);
        Assert.Contains("RegDeleteKeyIncludingSubkeys", script, StringComparison.Ordinal);
        Assert.Contains("/S _?=", script, StringComparison.Ordinal);
        // The retired NSIS upgrade path passes the _?= directory unquoted; the
        // legacy uninstaller consumes the rest of its command line and would
        // compare any quotes against the registry InstallLocation.
        Assert.Contains("Format('/S _?=%s'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/S _?=\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_AdoptsExistingInstallPathOnUpgrade()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // The directory page must default to the existing installation instead
        // of falling back to Program Files: previous Inno Setup records are
        // read across registry views, and the validated legacy NSIS bridge
        // contributes its InstallLocation as a fallback.
        Assert.Contains("procedure ResolveInitialInstallDir", script, StringComparison.Ordinal);
        Assert.Contains("function ResolveDefaultDir(const Param: String): String", script, StringComparison.Ordinal);
        Assert.Contains("TryReadInstallLocation(HKLM64, '{#INNO_UNINSTALL_KEY}')", script, StringComparison.Ordinal);
        Assert.Contains("TryReadInstallLocation(HKLM32, '{#INNO_UNINSTALL_KEY}')", script, StringComparison.Ordinal);
        Assert.Contains("TryReadInstallLocation(HKCU, '{#INNO_UNINSTALL_KEY}')", script, StringComparison.Ordinal);
        Assert.Contains("TryReadInstallLocation(HKCU32, '{#INNO_UNINSTALL_KEY}')", script, StringComparison.Ordinal);
        Assert.Contains("#define INNO_UNINSTALL_KEY", script, StringComparison.Ordinal);
        Assert.Contains(
            "if InitialInstallDir = '' then\r\n    InitialInstallDir := TryGetValidatedLegacyInstall(LegacyUninstallerPath);",
            script,
            StringComparison.Ordinal);
        // The default must be resolved before the legacy bridge runs, because
        // RemoveLegacyInstallation deletes the legacy registration it validates.
        Assert.True(
            script.IndexOf("  ResolveInitialInstallDir;", StringComparison.Ordinal)
                < script.IndexOf("if not RemoveLegacyInstallation() then", StringComparison.Ordinal),
            "InitializeSetup must capture the previous install dir before the legacy bridge removes it.");
    }

    [Fact]
    public void IssInstaller_UninstallsLegacyVersionOnlyAfterUserConfirms()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // InitializeSetup runs before the wizard is shown; uninstalling there
        // would remove the old version even when the user cancels setup. The
        // legacy bridge must therefore run from PrepareToInstall, which fires
        // only after the user chose to install.
        var initializeStart = script.IndexOf("function InitializeSetup", StringComparison.Ordinal);
        Assert.True(initializeStart >= 0, "InitializeSetup must exist.");
        var initializeEnd = script.IndexOf("\nfunction ", initializeStart + 1, StringComparison.Ordinal);
        var initializeSetup = script[initializeStart..initializeEnd];

        Assert.DoesNotContain("RemoveLegacyInstallation", initializeSetup, StringComparison.Ordinal);
        Assert.DoesNotContain("Exec(", initializeSetup, StringComparison.Ordinal);
        Assert.Contains("TryGetValidatedLegacyInstall", initializeSetup, StringComparison.Ordinal);

        Assert.Contains("function PrepareToInstall(var NeedsRestart: Boolean): String", script, StringComparison.Ordinal);
        var prepareStart = script.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        var prepareEnd = script.IndexOf("\nfunction ", prepareStart + 1, StringComparison.Ordinal);
        var prepareToInstall = script[prepareStart..prepareEnd];
        Assert.Contains("if not RemoveLegacyInstallation() then", prepareToInstall, StringComparison.Ordinal);
        // A failed legacy uninstall aborts the install with the localized message.
        Assert.Contains("Result := ExpandConstant('{cm:PreviousUninstallFailed}')", prepareToInstall, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_ValidatesLegacyUninstallerBeforeExecutingIt()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // The upgrade bridge must never run an unverified path read from the
        // registry: it requires the known NSIS uninstaller name and a matching
        // InstallLocation, and otherwise removes the stale registration.
        Assert.Contains(
            "CompareText(ExtractFileName(UninstallerPath), 'Uninstall.exe')",
            script,
            StringComparison.Ordinal);
        Assert.Contains("LegacyInstallLocationMatches(InstallDir)", script, StringComparison.Ordinal);
        Assert.Contains(
            "RegQueryStringValue(HKLM, '{#LEGACY_NSIS_UNINSTALL_KEY}', 'InstallLocation'",
            script,
            StringComparison.Ordinal);
        Assert.Contains("CompareText(Location, Expected) = 0", script, StringComparison.Ordinal);
        // Pascal Script resolves identifiers only after their definition; the
        // helper must therefore precede the upgrade bridge that calls it.
        Assert.True(
            script.IndexOf("function LegacyInstallLocationMatches", StringComparison.Ordinal)
                < script.IndexOf("LegacyInstallLocationMatches(InstallDir)", StringComparison.Ordinal),
            "LegacyInstallLocationMatches must be defined before RemoveLegacyInstallation calls it.");
        // Only one execution site exists, and it sits behind the checks above.
        Assert.Equal(1, CountOccurrences(script, "Exec(UninstallerPath"));
        // A mismatched registration must be removed, not executed.
        Assert.True(
            script.IndexOf("RegDeleteKeyIncludingSubkeys", StringComparison.Ordinal)
                < script.IndexOf("Exec(UninstallerPath", StringComparison.Ordinal),
            "The stale-registration cleanup must precede the execution site.");
    }

    [Fact]
    public void IssInstaller_UninstallMarkerIsClaimedInBothInstallAndUninstallPaths()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // The ownership marker must be written at install time and removed at
        // uninstall time under the same name so a rename can never strand it.
        Assert.Contains(
            "Type: files; Name: \"{app}\\.cafe-launcher-install\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExpandConstant('{app}\\.cafe-launcher-install')",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionScript_UsesConfirmedPerPlatformArtifactNames()
    {
        var script = ReadProjectFile("scripts/Build-Distribution.ps1");

        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_win-x64.zip",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_osx-arm64.zip",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_linux-x64.tar.gz",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_linux-x64.AppImage",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UninstallFiles.nsh", script, StringComparison.Ordinal);
        Assert.DoesNotContain("makensis", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinuxAppImage_HasStandardAppRunAndStartupSmokeTest()
    {
        var script = ReadProjectFile("scripts/Build-Distribution.ps1");
        var appRun = ReadProjectFile("installer/linux/AppRun");
        var workflow = ReadProjectFile(".github/workflows/release.yml");

        Assert.StartsWith("#!/bin/sh", appRun, StringComparison.Ordinal);
        Assert.Contains(
            "exec \"$APPDIR/usr/bin/Cafe.Launcher.Avalonia\" \"$@\"",
            appRun,
            StringComparison.Ordinal);
        Assert.Contains("$appDirRoot \"AppRun\"", script, StringComparison.Ordinal);
        Assert.Contains("\"--runtime-file\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "\"--appimage-extract-and-run\",\n                \"--version\"",
            script.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "AppImage/appimagetool/releases/download/1.9.1/",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "AppImage/type2-runtime/releases/download/20251108/",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("AppImageRuntimePath = './runtime-x86_64'", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "timeout --kill-after=5s 10s xvfb-run",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AppImage/appimagetool/releases/download/continuous/",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsInstallerScript_UsesConfirmedArtifactNameAndCompilesWithIscc()
    {
        var script = ReadProjectFile("scripts/New-WindowsInstaller.ps1");

        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_setup.exe",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Resolve-Iscc", script, StringComparison.Ordinal);
        Assert.Contains("Inno Setup 7\\ISCC.exe", script, StringComparison.Ordinal);
        Assert.Contains("[version]\"7.0\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("makensis", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsInstallerScript_PassesPublishGlobAndOutputsToIscc()
    {
        var installerScript = ReadProjectFile("scripts/New-WindowsInstaller.ps1");
        var issScript = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        Assert.Contains("$publishGlob = Join-Path $publishRoot \"*\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"-dPUBLISH_GLOB=$publishGlob\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"-dAPP_VERSION=$($version.VersionPrefix)\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"-dAPP_FILE_VERSION=$($version.FileVersion)\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"-o$OutputDir\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"-f$setupBaseName\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("\"installer/Cafe.Launcher.Avalonia.iss\"", installerScript, StringComparison.Ordinal);
        Assert.Contains("Source: \"{#PUBLISH_GLOB}\"", issScript, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsInstallerScript_DetectsIsccVersionOnBothMajorVersions()
    {
        var script = ReadProjectFile("scripts/New-WindowsInstaller.ps1");

        // ISCC 7+ answers --version with a bare version number; ISCC 6 does not
        // support the flag (banner + non-zero exit), so the script must fall back
        // to the DisplayVersion of the installer's uninstall registration.
        Assert.Contains("$IsccPath --version", script, StringComparison.Ordinal);
        Assert.Contains(
            "HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            script,
            StringComparison.Ordinal);
        Assert.Contains("DisplayVersion", script, StringComparison.Ordinal);
        Assert.Contains("Inno Setup 7.0 or newer is required", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsInstallerScript_PrefersInstalledInnoSetupSevenOverPath()
    {
        var script = ReadProjectFile("scripts/New-WindowsInstaller.ps1");

        var innoSevenLookup = script.IndexOf("$innoSevenCandidates", StringComparison.Ordinal);
        var pathLookup = script.IndexOf("Get-Command ISCC.exe", StringComparison.Ordinal);
        var innoSixLookup = script.IndexOf("$innoSixCandidates", StringComparison.Ordinal);

        Assert.True(innoSevenLookup >= 0, "The standard Inno Setup 7 locations must be checked.");
        Assert.True(pathLookup > innoSevenLookup, "A preinstalled Inno Setup 6 on PATH must not override Inno Setup 7.");
        Assert.True(innoSixLookup > pathLookup, "Inno Setup 6 remains the final local fallback.");
    }

    [Fact]
    public void IssInstaller_ChineseLanguageFileIsVendored()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // Vendoring keeps compilation independent of the translations bundled
        // with a particular Inno Setup release.
        Assert.Contains("lang\\ChineseSimplified.isl", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "compiler:Languages\\ChineseSimplified.isl",
            script,
            StringComparison.Ordinal);

        var chineseMessages = ReadProjectFile("installer/lang/ChineseSimplified.isl");
        Assert.Contains("Chinese Simplified messages", chineseMessages, StringComparison.Ordinal);
        // The vendored translation is redistributed under the Inno Setup license;
        // its attribution header must be retained.
        Assert.Contains(
            "https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation",
            chineseMessages,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MacOsBundle_AssetsArePresentAndVersioned()
    {
        var plist = ReadProjectFile("installer/macos/Info.plist");

        // The bundle identity is user-visible; it must stay aligned with the
        // published .app name and the .NET assembly name it executes.
        Assert.Contains("CFBundleName", plist, StringComparison.Ordinal);
        Assert.Contains("Cafe Launcher", plist, StringComparison.Ordinal);
        Assert.Contains("cafe.bluearchive.Cafe-Launcher-Avalonia", plist, StringComparison.Ordinal);
        Assert.Contains("<string>Cafe.Launcher.Avalonia</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>app-icon</string>", plist, StringComparison.Ordinal);
        Assert.Contains("{VERSION}", plist, StringComparison.Ordinal);
        Assert.Contains("{FILE_VERSION}", plist, StringComparison.Ordinal);
        Assert.Contains("NSHighResolutionCapable", plist, StringComparison.Ordinal);

        var icns = File.ReadAllBytes(GetProjectFilePath("installer/macos/app-icon.icns"));
        Assert.True(icns.Length > 8 && icns.AsSpan(0, 4).SequenceEqual("icns"u8), "app-icon.icns must be a valid icns container.");

        foreach (var size in new[] { 256, 512 })
        {
            var png = File.ReadAllBytes(GetProjectFilePath($"installer/linux/app-icon-{size}.png"));
            Assert.True(png.Length > 4 && png.AsSpan(0, 4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), $"app-icon-{size}.png must be a valid PNG.");
        }

        var desktop = ReadProjectFile("installer/linux/cafe-launcher.desktop");
        Assert.Contains("Exec=Cafe.Launcher.Avalonia", desktop, StringComparison.Ordinal);
        Assert.Contains("Icon=cafe-launcher", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_UsesWindowsRunnerAndInstallsVerifiedInnoSetupSeven()
    {
        var workflow = ReadProjectFile(".github/workflows/release.yml");
        var installerJobStart = workflow.IndexOf("  installer:", StringComparison.Ordinal);
        var installerJobEnd = workflow.IndexOf("\n  release:", installerJobStart, StringComparison.Ordinal);
        var installerJob = workflow[installerJobStart..installerJobEnd];

        Assert.Contains("runs-on: windows-latest", installerJob, StringComparison.Ordinal);
        Assert.Contains("INNO_SETUP_VERSION: 7.1.0", installerJob, StringComparison.Ordinal);
        Assert.Contains("innosetup-$env:INNO_SETUP_VERSION-x64.exe", installerJob, StringComparison.Ordinal);
        Assert.Contains("--repo jrsoftware/issrc", installerJob, StringComparison.Ordinal);
        Assert.Contains(
            "gh release verify-asset $releaseTag $installerPath",
            installerJob,
            StringComparison.Ordinal);
        Assert.Contains("$installedVersion -notmatch '^7\\.'", installerJob, StringComparison.Ordinal);
        Assert.DoesNotContain("choco install innosetup", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$tagArguments = @{}", installerJob, StringComparison.Ordinal);
        Assert.Contains("$tagArguments.Tag = '${{ github.ref_name }}'", installerJob, StringComparison.Ordinal);
        Assert.DoesNotContain("@('-Tag', '${{ github.ref_name }}')", installerJob, StringComparison.Ordinal);
        Assert.DoesNotContain("makensis", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apt-get", installerJob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseWorkflow_AttachesPackagesAndKeepsBannerInSourceRepository()
    {
        var workflow = ReadProjectFile(".github/workflows/release.yml");

        // Every release must carry the full cross-platform artifact set, and
        // both release targets (source + distribution repository) stay in sync.
        foreach (var artifactName in new[]
        {
            "Cafe.Launcher.Avalonia_${{ github.ref_name }}_win-x64.zip",
            "Cafe.Launcher.Avalonia_${{ github.ref_name }}_setup.exe",
            "Cafe.Launcher.Avalonia_${{ github.ref_name }}_osx-arm64.zip",
            "Cafe.Launcher.Avalonia_${{ github.ref_name }}_linux-x64.tar.gz",
            "Cafe.Launcher.Avalonia_${{ github.ref_name }}_linux-x64.AppImage",
        })
        {
            Assert.Equal(2, CountOccurrences(workflow, artifactName));
        }

        const string releaseBannerName = "cafe-launcher-${{ github.ref_name }}-release-banner.png";
        Assert.Contains(
            $"$bannerPath = \"docs/assets/release-banners/{releaseBannerName}\"",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("Test-Path $bannerPath -PathType Leaf", workflow, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workflow, releaseBannerName));
        Assert.DoesNotContain("name: release-banner", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/release-banner", workflow, StringComparison.Ordinal);

        Assert.Contains(
            "repository: bluearchive-cafe/Cafe.Launcher.Avalonia_Release",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("secrets.RELEASE_REPOSITORY_TOKEN", workflow, StringComparison.Ordinal);
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var character in text)
        {
            if ((character >= '\u4E00' && character <= '\u9FFF') ||
                (character >= '\u3040' && character <= '\u30FF'))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadProjectFile(string relativePath) =>
        File.ReadAllText(GetProjectFilePath(relativePath));

    private static string GetProjectFilePath(string relativePath) =>
        Path.Combine(FindProjectRoot(), relativePath);

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.slnx was not found.");
    }
}
