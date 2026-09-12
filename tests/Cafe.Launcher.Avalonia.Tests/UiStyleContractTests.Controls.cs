using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// M3 color-role contract for base controls: the focus ring is declared opaque
// because the runtime value is the current scheme's Primary role — a
// semi-transparent accent composites below the 3:1 indicator threshold on the
// lighter surfaces it is drawn over.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void BaseControls_FocusRingFallback_RemainsOpaque()
    {
        var app = XDocument.Load(ProjectFile("App.axaml"));
        var focusRing = app.Descendants()
            .Single(element =>
                element.Name.LocalName == "SolidColorBrush"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Color.FocusRing"));

        Assert.Null(focusRing.Attribute("Opacity"));
    }
}
