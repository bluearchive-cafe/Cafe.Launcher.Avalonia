namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallerContractTests
{
    [Fact]
    public void NsisInstaller_DeclaresUtf8SourceEncoding()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.StartsWith("# -*- coding: utf-8 -*-", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NsisInstaller_UsesConfirmedMachineWideIdentity()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains("Name \"Cafe Launcher\"", script, StringComparison.Ordinal);
        Assert.Contains("!define PUBLISHER \"BlueArchive Cafe\"", script, StringComparison.Ordinal);
        Assert.Contains("RequestExecutionLevel admin", script, StringComparison.Ordinal);
        Assert.Contains("InstallDir \"$PROGRAMFILES64\\Cafe Launcher\"", script, StringComparison.Ordinal);
        Assert.Contains("SetShellVarContext all", script, StringComparison.Ordinal);
        Assert.Contains("WriteRegStr HKLM", script, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallDirRegKey", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NsisInstaller_ProvidesASelectableDesktopShortcut()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains("!insertmacro MUI_PAGE_COMPONENTS", script, StringComparison.Ordinal);
        Assert.Contains("Section /o \"Desktop shortcut\" SEC_DESKTOP", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NsisInstaller_UsesExplicitUninstallFileList()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.DoesNotContain("RMDir /r \"$INSTDIR\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("YostarGames", script, StringComparison.Ordinal);
        Assert.Contains("!include \"${UNINSTALL_INCLUDE}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NsisUninstaller_PreservesApplicationDataUnlessExplicitlySelected()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains(
            "${NSD_SetState} $DeleteApplicationDataCheckbox ${BST_UNCHECKED}",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "StrCmp $DeleteApplicationData \"1\" 0 preserveApplicationData",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "RMDir /r \"$LOCALAPPDATA\\Cafe Launcher\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NsisInstaller_BlocksFileChangesWhileLauncherIsRunning()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains("Call EnsureApplicationStopped", script, StringComparison.Ordinal);
        Assert.Contains("Call un.EnsureApplicationStopped", script, StringComparison.Ordinal);
        Assert.Contains("SetErrorLevel 1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NsisInstaller_CleansStaleRegistrationWhenOldUninstallerIsMissing()
    {
        var script = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains("IfFileExists \"$2\" 0 staleRegistration", script, StringComparison.Ordinal);
        Assert.Contains("staleRegistration:", script, StringComparison.Ordinal);
        Assert.Contains("DeleteRegKey HKLM \"${UNINSTALL_KEY}\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IfFileExists \"$2\" 0 failed", script, StringComparison.Ordinal);
        Assert.Contains("IntCmp $1 0 cleanup failed failed", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionScript_UsesConfirmedArtifactNamesAndGeneratesUninstallList()
    {
        var script = ReadProjectFile("scripts/Build-Distribution.ps1");

        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_standalone.zip",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cafe.Launcher.Avalonia_${Tag}_setup.exe",
            script,
            StringComparison.Ordinal);
        Assert.Contains("UninstallFiles.nsh", script, StringComparison.Ordinal);
        Assert.Contains("GetRelativePath", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionBuilder_UsesHostNativePublishGlobForCrossPlatformNsis()
    {
        var buildScript = ReadProjectFile("scripts/Build-Distribution.ps1");
        var nsisScript = ReadProjectFile("installer/Cafe.Launcher.Avalonia.nsi");

        Assert.Contains("$publishGlob = Join-Path $PublishDir \"*\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"${definePrefix}PUBLISH_GLOB=$publishGlob\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"${definePrefix}UNINSTALL_INCLUDE=$uninstallInclude\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"${definePrefix}OUTPUT_FILE=$setupPath\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("File /r \"${PUBLISH_GLOB}\"", nsisScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_UploadsBothArtifacts()
    {
        var workflow = ReadProjectFile(".github/workflows/release.yml");

        Assert.Contains("standalone_name=", workflow, StringComparison.Ordinal);
        Assert.Contains("setup_name=", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ env.standalone_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ env.setup_name }}", workflow, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindProjectRoot(), relativePath));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
