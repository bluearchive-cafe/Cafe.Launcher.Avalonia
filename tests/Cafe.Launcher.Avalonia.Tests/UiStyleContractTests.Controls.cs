using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// M3 base-control contract (Material 3 audit P2 #7): every ComboBox opts into one
// of the two launcher select forms so no plain Fluent square-corner select
// remains. The checked-state RadioButton/CheckBox glyph theming is pinned by
// headless rendering tests (MainWindowHeadlessTests.MaterialDesign).
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

    [Fact]
    public void BaseControls_ProductionComboBoxes_OptIntoLauncherSelectForm()
    {
        var files = Directory.GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectFile("Controls"), "*.axaml", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var document = XDocument.Load(file);
            foreach (var comboBox in document.Descendants().Where(element => element.Name.LocalName == "ComboBox"))
            {
                // Two sanctioned forms: the settings/form select (`setting-control`) and the
                // gallery's variant showcase select (`gallery-select`). The gallery file is
                // scanned on purpose — it is the only place gallery-select appears, so
                // exempting it here would make that allowance unreachable.
                Assert.True(
                    HasClass(comboBox, "setting-control") || HasClass(comboBox, "gallery-select"),
                    $"{file}: ComboBox must opt into setting-control or gallery-select form.");
            }
        }
    }

    [Fact]
    public void BaseControls_RadioButtonGlyph_FollowsM3ComponentTokens()
    {
        // material-web radio v0_192 组件 token 表：未选中环 onSurfaceVariant、
        // hover/press/focus 图标转 onSurface、选中态 Primary 全程不变、禁用
        // onSurface 38%；几何 2px 环 + 10dp 内点；选中盘透明（环+点形态）。
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var outerEllipse = GetStyleSetters(styles, "RadioButton:not(:checked) /template/ Ellipse#OuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Text.Secondary}", outerEllipse["Stroke"]);
        Assert.Equal(
            "{StaticResource Launcher.Control.Stroke.Radio}",
            outerEllipse["StrokeThickness"]);

        var checkRing = GetStyleSetters(styles, "RadioButton:checked /template/ Ellipse#CheckOuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", checkRing["Stroke"]);
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", checkRing["Fill"]);
        Assert.Equal(
            "{StaticResource Launcher.Control.Stroke.Radio}",
            checkRing["StrokeThickness"]);

        var glyph = GetStyleSetters(styles, "RadioButton:checked /template/ Ellipse#CheckGlyph");
        Assert.Equal("{StaticResource Launcher.Control.Size.Radio.Glyph}", glyph["Width"]);
        Assert.Equal("{StaticResource Launcher.Control.Size.Radio.Glyph}", glyph["Height"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", glyph["Fill"]);

        var hoverRing = GetStyleSetters(styles, "RadioButton:pointerover /template/ Ellipse#OuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Text.Primary}", hoverRing["Stroke"]);
        Assert.Equal("{DynamicResource Launcher.Color.StateLayer.OnSurface.Hover}", hoverRing["Fill"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Button.Flat.Pressed}",
            GetStyleSetters(styles, "RadioButton:pressed /template/ Ellipse#OuterEllipse")["Fill"]);

        // 选中态图标 Primary 不漂移：hover/press 只换状态层盘。
        var hoverCheck = GetStyleSetters(styles, "RadioButton:pointerover /template/ Ellipse#CheckOuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", hoverCheck["Stroke"]);
        Assert.Equal("{DynamicResource Launcher.Color.Button.Flat.Hover}", hoverCheck["Fill"]);
        var pressedCheck = GetStyleSetters(styles, "RadioButton:pressed /template/ Ellipse#CheckOuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", pressedCheck["Stroke"]);
        Assert.Equal("{DynamicResource Launcher.Color.StateLayer.OnSurface.Pressed}", pressedCheck["Fill"]);

        var disabledRing = GetStyleSetters(styles, "RadioButton:disabled /template/ Ellipse#OuterEllipse");
        Assert.Equal("{DynamicResource Launcher.Text.Primary}", disabledRing["Stroke"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            disabledRing["Opacity"]);
        var disabledLabel = GetStyleSetters(styles, "RadioButton:disabled /template/ ContentPresenter#PART_ContentPresenter");
        Assert.Equal("{DynamicResource Launcher.Text.Primary}", disabledLabel["Foreground"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            disabledLabel["Opacity"]);
    }

    [Fact]
    public void BaseControls_RadioButtonIconGrid_VerticallyCentersWithContent()
    {
        // Fluent 模板把图标栅格钉在行顶（Height=32、Top），多行内容时图标偏高；
        // 两种激活器分支共同覆盖全部状态，把栅格改为随行居中。
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        foreach (var selector in new[]
                 {
                     "RadioButton:not(:checked) /template/ Grid > Grid",
                     "RadioButton:checked /template/ Grid > Grid"
                 })
        {
            Assert.Equal("Center", GetStyleSetters(styles, selector)["VerticalAlignment"]);
        }
    }
}
