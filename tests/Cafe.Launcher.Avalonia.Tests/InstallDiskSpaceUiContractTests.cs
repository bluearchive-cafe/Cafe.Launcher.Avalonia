using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallDiskSpaceUiContractTests
{
    [Fact]
    public void InstallButton_ExposesDiskSpaceBlockReasonWhileDisabled()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        // The install command appears on two buttons (detailed panel path row + compact mode
        // operation-actions area). Pick the first match, which is the primary button.
        var installButton = document
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}");

        Assert.Equal(
            "{Binding Operations.InstallButtonToolTip}",
            installButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("True", installButton.Attribute("ToolTip.ShowOnDisabled")?.Value);
        Assert.Equal(
            "{Binding Operations.InstallButtonToolTip}",
            installButton.Attribute("AutomationProperties.HelpText")?.Value);
    }

    private static string ProjectFile(string relativePath) =>
        Path.Combine(FindProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var applicationProject = Path.Combine(directory.FullName, "src", "Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(applicationProject))
            {
                return Path.GetDirectoryName(applicationProject)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
