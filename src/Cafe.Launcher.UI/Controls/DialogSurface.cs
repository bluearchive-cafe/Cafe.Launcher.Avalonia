using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace Cafe.Launcher.UI.Controls;

/// <summary>
/// 对话框家族 v2 的统一外壳（ADR-015）。两种合法形态：
/// <see cref="DialogSurfaceForm.Basic"/>（聚焦决策）与
/// <see cref="DialogSurfaceForm.Panel"/>（特性面板），由 <see cref="Form"/>
/// 切换并映射到伪类。
/// 视觉契约集中在 Views/Styles/DialogSurface.axaml 中，
/// 使用方不得自带几何或色彩值。
/// </summary>
[TemplatePart("PART_CloseButton", typeof(Button))]
[PseudoClasses(":panel")]
internal class DialogSurface : TemplatedControl
{
    private Button? closeButton;
    private Border? headerBorder;
    private Border? basicHeadBorder;
    private Border? footerBand;
    private ContentPresenter? scrollContentPresenter;
    private ContentPresenter? footerPresenter;
    private ContentPresenter? leadingPresenter;
    private ContentPresenter? toolbarPresenter;
    private ScrollViewer? scrollViewer;
    private ContentPresenter? directContentPresenter;

    public static readonly StyledProperty<DialogSurfaceForm> FormProperty =
        AvaloniaProperty.Register<DialogSurface, DialogSurfaceForm>(nameof(Form));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogSurface, string?>(nameof(Title));

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

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
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
        footerBand = e.NameScope.Find<Border>("PART_FooterBand");
        scrollContentPresenter = e.NameScope.Find<ContentPresenter>("PART_ScrollContentPresenter");
        footerPresenter = e.NameScope.Find<ContentPresenter>("PART_FooterPresenter");
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
            PseudoClasses.Set(":panel", Form == DialogSurfaceForm.Panel);
            SyncSlotContents();
            return;
        }

        if (change.Property == CloseCommandProperty ||
            change.Property == TitleProperty ||
            change.Property == FooterProperty ||
            change.Property == FooterLeadingProperty ||
            change.Property == ToolbarProperty ||
            change.Property == ContentProperty ||
            change.Property == ContentTemplateProperty)
        {
            SyncSlotContents();
        }
    }

    /// <summary>
    /// 对象型插槽（正文、工具行、动作组）不依赖模板绑定而由代码同步：
    /// 模板重应用或属性变化时保持一致，规避无类型转换器的绑定路径。
    /// </summary>
    private void SyncSlotContents()
    {
        // 正文按当前模式单路投递：同一可视元素不能同时挂到两个展示器。
        var isPanel = Form == DialogSurfaceForm.Panel;
        var hasTitle = !string.IsNullOrEmpty(Title);
        var isShellMode = isPanel && !hasTitle;

        if (isShellMode)
        {
            SetContent(scrollContentPresenter, null, null);
            SetContent(directContentPresenter, Content, ContentTemplate);
        }
        else
        {
            SetContent(directContentPresenter, null, null);
            SetContent(scrollContentPresenter, Content, ContentTemplate);
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

        RefreshChrome(isPanel, hasTitle, isShellMode);
    }

    private static void SetContent(ContentPresenter? presenter, object? content, IDataTemplate? template)
    {
        if (presenter is not null)
        {
            presenter.ContentTemplate = template;
            presenter.Content = content;
        }
    }

    private void RefreshChrome(bool isPanel, bool hasTitle, bool isShellMode)
    {
        if (closeButton is not null)
        {
            // 关闭钮仅属于 Panel 头带，且必须存在合法出口才有意义；
            // Basic 形态的动作即出口，永不渲染 ✕。
            closeButton.IsVisible = isPanel && CloseCommand is not null && hasTitle;
        }

        // 无标题的 Panel 整行折叠，
        // 正文边距同步归零：供设置/向导这类自带留白体系的外壳场景复用表面档案。
        if (headerBorder is not null)
        {
            headerBorder.IsVisible = isPanel && hasTitle;
        }

        if (basicHeadBorder is not null)
        {
            basicHeadBorder.IsVisible = !isPanel;
        }

        if (scrollViewer is not null)
        {
            // 常规形态经滚动脚手架并按形态取边距；外壳模式整体绕过，
            // 让内容获得模板行高的有界高度，内部滚动区才能正常工作。
            scrollViewer.IsVisible = !isShellMode;
        }

        if (directContentPresenter is not null)
        {
            directContentPresenter.IsVisible = isShellMode;
        }

        if (footerBand is not null)
        {
            // 动作带两个插槽全空时整体折叠：外壳模式（设置/向导）自绘动作区，
            // 不应在表面底部残留一条空发丝带。
            footerBand.IsVisible = Footer is not null || FooterLeading is not null;
        }
    }
}
