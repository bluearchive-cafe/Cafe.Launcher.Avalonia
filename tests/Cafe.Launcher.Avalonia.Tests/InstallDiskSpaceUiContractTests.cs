using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallDiskSpaceUiContractTests
{
    [Fact]
    public void InstallButton_ExposesDiskSpaceBlockReasonWhileDisabled()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var installButton = document
            .Descendants()
            .Single(element =>
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
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
