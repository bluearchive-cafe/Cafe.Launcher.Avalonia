using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallDiskSpaceUiContractTests
{
    [Fact]
    public void InstallButton_ExposesDiskSpaceBlockReasonWhileDisabled()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.axaml"));
        // The compact-mode install button is the only install command target.
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

}
