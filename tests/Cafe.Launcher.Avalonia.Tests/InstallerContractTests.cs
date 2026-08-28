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
        Assert.Contains("DefaultDirName={autopf}\\Cafe Launcher", script, StringComparison.Ordinal);
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
    public void WindowsInstallerScript_UsesConfirmedArtifactNameAndCompilesWithIscc()
    {
        var script = ReadProjectFile("scripts/New-WindowsInstaller.ps1");

        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_setup.exe",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Resolve-Iscc", script, StringComparison.Ordinal);
        Assert.Contains("Inno Setup 7\\ISCC.exe", script, StringComparison.Ordinal);
        Assert.Contains("[version]\"6.3\"", script, StringComparison.Ordinal);
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
        Assert.Contains("Inno Setup 6.3 or newer is required", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IssInstaller_ChineseLanguageFileIsVendoredForInnoSetupSix()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.iss");

        // Official Inno Setup ships Chinese translations only in 7.x, while the
        // release workflow installs the Chocolatey package (Inno Setup 6.x), so
        // the base Chinese messages must be referenced repo-relative.
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
    public void ReleaseWorkflow_UsesWindowsRunnerAndInstallsInnoSetup()
    {
        var workflow = ReadProjectFile(".github/workflows/release.yml");

        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("choco install innosetup", workflow, StringComparison.Ordinal);
        Assert.Contains("$tagArguments = @{}", workflow, StringComparison.Ordinal);
        Assert.Contains("$tagArguments.Tag = '${{ github.ref_name }}'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("@('-Tag', '${{ github.ref_name }}')", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("makensis", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apt-get", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseWorkflow_AttachesAllPlatformPackagesToBothReleases()
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
            $"path: docs/assets/release-banners/{releaseBannerName}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(workflow, $"artifacts/release-banner/{releaseBannerName}"));

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
