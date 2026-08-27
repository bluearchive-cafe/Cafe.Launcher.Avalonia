using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// 对话框家族 v2 的统一外壳（ADR-015）。两种合法形态：
/// <see cref="DialogSurfaceForm.Basic"/>（聚焦决策）与
/// <see cref="DialogSurfaceForm.Panel"/>（特性面板），由 <see cref="Form"/>
/// 切换并映射到伪类；<see cref="Status"/> 提供状态修饰。
/// 视觉契约集中在 Views/Styles/DialogSurface.axaml 的 ControlTheme 中，
/// 使用方不得自带几何或色彩值。
/// </summary>
[TemplatePart("PART_CloseButton", typeof(Button))]
[PseudoClasses(":panel", ":info", ":warning", ":danger")]
public class DialogSurface : TemplatedControl
{
    private Button? closeButton;
    private Border? headerBorder;
    private Border? basicHeadBorder;
    private ContentPresenter? badgePresenter;
    private ContentPresenter? basicIconPresenter;
    private ContentPresenter? scrollContentPresenter;
    private ContentPresenter? footerPresenter;
    private TextBlock? subtitleTextBlock;
    private TextBlock? basicSupportTextBlock;
    private ContentPresenter? leadingPresenter;
    private ContentPresenter? toolbarPresenter;
    private ScrollViewer? scrollViewer;
    private ContentPresenter? directContentPresenter;

    public static readonly StyledProperty<DialogSurfaceForm> FormProperty =
        AvaloniaProperty.Register<DialogSurface, DialogSurfaceForm>(nameof(Form));

    public static readonly StyledProperty<DialogSurfaceStatus> StatusProperty =
        AvaloniaProperty.Register<DialogSurface, DialogSurfaceStatus>(nameof(Status));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogSurface, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogSurface, string?>(nameof(Subtitle));

    /// <summary>Panel 头带徽章内的内容；为空时徽章不渲染。</summary>
    public static readonly StyledProperty<object?> HeaderIconProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(HeaderIcon));

    /// <summary>Basic 形态标题上方的裸图标（无容器）；为空时整行不渲染。</summary>
    public static readonly StyledProperty<object?> BasicIconProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(BasicIcon));

    /// <summary>共享滚动内容区承载的主体内容。</summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(Content));

    /// <summary>表面阴影；默认值来自 Launcher.Elevation.Shadow.Dialog（ADR-015 表面档案）。</summary>
    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.Register<DialogSurface, BoxShadows>(nameof(BoxShadow));

    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<DialogSurface, IDataTemplate?>(nameof(ContentTemplate));

    /// <summary>右侧主动作组（Panel 渲染于发丝底带，Basic 渲染于内容流末端的动作带）。</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(Footer));

    /// <summary>左侧辅助动作槽，仅 Panel 的发丝底带消费。</summary>
    public static readonly StyledProperty<object?> FooterLeadingProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(FooterLeading));

    /// <summary>头带与滚动区之间的固定工具行（如日志过滤栏）；为空时不渲染。</summary>
    public static readonly StyledProperty<object?> ToolbarProperty =
        AvaloniaProperty.Register<DialogSurface, object?>(nameof(Toolbar));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DialogSurface, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<string?> CloseAutomationNameProperty =
        AvaloniaProperty.Register<DialogSurface, string?>(nameof(CloseAutomationName));

    public static readonly StyledProperty<string?> CloseToolTipProperty =
        AvaloniaProperty.Register<DialogSurface, string?>(nameof(CloseToolTip));

    public DialogSurfaceForm Form
    {
        get => GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    public DialogSurfaceStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? HeaderIcon
    {
        get => GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    public object? BasicIcon
    {
        get => GetValue(BasicIconProperty);
        set => SetValue(BasicIconProperty, value);
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public object? FooterLeading
    {
        get => GetValue(FooterLeadingProperty);
        set => SetValue(FooterLeadingProperty, value);
    }

    public object? Toolbar
    {
        get => GetValue(ToolbarProperty);
        set => SetValue(ToolbarProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public string? CloseAutomationName
    {
        get => GetValue(CloseAutomationNameProperty);
        set => SetValue(CloseAutomationNameProperty, value);
    }

    public string? CloseToolTip
    {
        get => GetValue(CloseToolTipProperty);
        set => SetValue(CloseToolTipProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        headerBorder = e.NameScope.Find<Border>("PART_PanelHead");
        basicHeadBorder = e.NameScope.Find<Border>("PART_BasicHead");
        badgePresenter = e.NameScope.Find<ContentPresenter>("PART_BadgePresenter");
        basicIconPresenter = e.NameScope.Find<ContentPresenter>("PART_BasicIconPresenter");
        scrollContentPresenter = e.NameScope.Find<ContentPresenter>("PART_ScrollContentPresenter");
        footerPresenter = e.NameScope.Find<ContentPresenter>("PART_FooterPresenter");
        subtitleTextBlock = e.NameScope.Find<TextBlock>("PART_SubtitleTextBlock");
        basicSupportTextBlock = e.NameScope.Find<TextBlock>("PART_BasicSupportTextBlock");
        leadingPresenter = e.NameScope.Find<ContentPresenter>("PART_FooterLeadingPresenter");
        toolbarPresenter = e.NameScope.Find<ContentPresenter>("PART_ToolbarPresenter");
        scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        directContentPresenter = e.NameScope.Find<ContentPresenter>("PART_DirectContentPresenter");

        SyncSlotContents();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FormProperty)
        {
            ApplyPseudoClasses();
            RefreshChrome();
            return;
        }

        if (change.Property == StatusProperty)
        {
            ApplyPseudoClasses();
            return;
        }

        if (change.Property == CloseCommandProperty ||
            change.Property == TitleProperty ||
            change.Property == HeaderIconProperty ||
            change.Property == BasicIconProperty ||
            change.Property == FooterProperty ||
            change.Property == FooterLeadingProperty ||
            change.Property == ToolbarProperty ||
            change.Property == ContentProperty ||
            change.Property == ContentTemplateProperty)
        {
            SyncSlotContents();
            return;
        }

        if (change.Property == SubtitleProperty)
        {
            RefreshChrome();
        }
    }

    /// <summary>
    /// 对象型插槽（图标、正文、动作组）不依赖模板绑定而由代码同步：
    /// 模板重应用或属性变化时保持一致，规避无类型转换器的绑定路径。
    /// </summary>
    private void SyncSlotContents()
    {
        // 正文按当前模式单路投递：同一可视元素不能同时挂到两个展示器。
        var syncIsPanel = Form == DialogSurfaceForm.Panel;
        var syncHasHeaderIdentity =
            !string.IsNullOrEmpty(Title)
            || !string.IsNullOrEmpty(Subtitle)
            || HeaderIcon is not null;
        var syncIsShellMode = !syncIsPanel || !syncHasHeaderIdentity;

        if (badgePresenter is not null)
        {
            badgePresenter.Content = HeaderIcon;
        }

        if (basicIconPresenter is not null)
        {
            basicIconPresenter.Content = BasicIcon;
        }

        if (scrollContentPresenter is not null)
        {
            scrollContentPresenter.Content = syncIsShellMode ? null : Content;
            scrollContentPresenter.ContentTemplate = syncIsShellMode ? null : ContentTemplate;
        }

        if (directContentPresenter is not null)
        {
            directContentPresenter.Content = syncIsShellMode ? Content : null;
            directContentPresenter.ContentTemplate = syncIsShellMode ? ContentTemplate : null;
        }

        if (footerPresenter is not null)
        {
            footerPresenter.Content = Footer;
        }

        if (leadingPresenter is not null)
        {
            leadingPresenter.Content = FooterLeading;
            leadingPresenter.IsVisible = FooterLeading is not null;
        }

        if (toolbarPresenter is not null)
        {
            toolbarPresenter.Content = Toolbar;
            toolbarPresenter.IsVisible = Toolbar is not null;
        }

        RefreshChrome();
    }

    private Thickness GetThicknessToken(string key) =>
        TryGetResource(key, ActualThemeVariant, out var value) && value is Thickness thickness
            ? thickness
            : default;

    private void ApplyPseudoClasses()
    {
        PseudoClasses.Set(":panel", Form == DialogSurfaceForm.Panel);
        PseudoClasses.Set(":info", Status == DialogSurfaceStatus.Info);
        PseudoClasses.Set(":warning", Status == DialogSurfaceStatus.Warning);
        PseudoClasses.Set(":danger", Status == DialogSurfaceStatus.Danger);
    }

    private void RefreshChrome()
    {
        var isPanel = Form == DialogSurfaceForm.Panel;
        var hasHeaderIdentity =
            !string.IsNullOrEmpty(Title)
            || !string.IsNullOrEmpty(Subtitle)
            || HeaderIcon is not null;

        if (closeButton is not null)
        {
            // 关闭钮仅属于 Panel 头带，且必须存在合法出口才有意义；
            // Basic 形态的动作即出口，永不渲染 ✕。
            closeButton.IsVisible = isPanel && CloseCommand is not null && hasHeaderIdentity;
        }

        // Panel 头带在没有任何身份内容（标题/副标题/徽章）时整行折叠，
        // 正文边距同步归零：供设置/向导这类自带留白体系的外壳场景复用表面档案。
        if (headerBorder is not null)
        {
            headerBorder.IsVisible = isPanel && hasHeaderIdentity;
        }

        if (basicHeadBorder is not null)
        {
            basicHeadBorder.IsVisible = !isPanel;
        }

        var isShellMode = !isPanel || !hasHeaderIdentity;

        if (scrollViewer is not null)
        {
            // 常规形态经滚动脚手架并按形态取边距；外壳模式整体绕过，
            // 让内容获得模板行高的有界高度，内部滚动区才能正常工作。
            scrollViewer.IsVisible = !isShellMode;
            if (!isShellMode)
            {
                scrollViewer.Padding = GetThicknessToken(
                    isPanel
                        ? "Launcher.Component.Dialog.Panel.Body.Padding"
                        : "Launcher.Component.Dialog.Basic.Content.Padding");
            }
        }

        if (directContentPresenter is not null)
        {
            directContentPresenter.IsVisible = isShellMode;
        }

        if (badgePresenter is not null)
        {
            badgePresenter.IsVisible = HeaderIcon is not null;
        }

        if (basicIconPresenter is not null)
        {
            basicIconPresenter.IsVisible = !isPanel && BasicIcon is not null;
        }

        if (subtitleTextBlock is not null)
        {
            subtitleTextBlock.IsVisible = !string.IsNullOrEmpty(Subtitle);
        }

        if (basicSupportTextBlock is not null)
        {
            basicSupportTextBlock.IsVisible = !string.IsNullOrEmpty(Subtitle);
        }

        if (leadingPresenter is not null)
        {
            leadingPresenter.IsVisible = FooterLeading is not null;
        }
    }
}
