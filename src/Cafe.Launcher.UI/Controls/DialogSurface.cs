using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;

namespace Cafe.Launcher.UI.Controls;

/// <summary>Composes modal content and commands in the launcher's existing modal stack.</summary>
internal sealed partial class DialogSurface : Border
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

    public static readonly StyledProperty<bool> CanCloseProperty =
        AvaloniaProperty.Register<DialogSurface, bool>(nameof(CanClose), true);

    public bool CanClose
    {
        get => GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

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

    public DialogSurface()
    {
        InitializeComponent();

        closeButton = this.FindControl<Button>("PART_CloseButton");
        headerBorder = this.FindControl<Border>("PART_PanelHead");
        basicHeadBorder = this.FindControl<Border>("PART_BasicHead");
        footerBand = this.FindControl<Border>("PART_FooterBand");
        scrollContentPresenter = this.FindControl<ContentPresenter>("PART_ScrollContentPresenter");
        footerPresenter = this.FindControl<ContentPresenter>("PART_FooterPresenter");
        leadingPresenter = this.FindControl<ContentPresenter>("PART_FooterLeadingPresenter");
        toolbarPresenter = this.FindControl<ContentPresenter>("PART_ToolbarPresenter");
        scrollViewer = this.FindControl<ScrollViewer>("PART_ScrollViewer");
        directContentPresenter = this.FindControl<ContentPresenter>("PART_DirectContentPresenter");

        SyncSlotContents();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FormProperty)
        {
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

        var hasLeadingFooter = FooterLeading is not null;
        if (footerPresenter is not null)
        {
            footerPresenter.Content = Footer;
            // Span the whole grid when there is no leading content so the wrap panel gets a finite width.
            Grid.SetColumn(footerPresenter, hasLeadingFooter ? 1 : 0);
            Grid.SetColumnSpan(footerPresenter, hasLeadingFooter ? 1 : 2);
        }

        if (leadingPresenter is not null)
        {
            leadingPresenter.Content = FooterLeading;
            leadingPresenter.IsVisible = hasLeadingFooter;
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
