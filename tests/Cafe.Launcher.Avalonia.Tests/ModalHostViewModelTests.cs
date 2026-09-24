using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ModalHostViewModelTests
{
    [Fact]
    public void Open_WhenNested_PutsMostRecentlyOpenedModalOnTop()
    {
        var host = new ModalHostViewModel();
        var settings = new TestModalContent();
        var confirmation = new TestModalContent();

        host.Open(ModalKind.Settings, settings);
        host.Open(ModalKind.UnsavedSettingsConfirmation, confirmation);

        Assert.Equal(ModalKind.UnsavedSettingsConfirmation, host.Top?.Kind);
        Assert.Equal(2, host.Entries.Count);
    }

    [Fact]
    public void Close_WhenTopCloses_RevealsPreviousModal()
    {
        var host = new ModalHostViewModel();
        var content = new TestModalContent();
        host.Open(ModalKind.ResourcePanel, content);
        host.Open(ModalKind.ResourcePanelSourceConfirmation, content);

        host.Close(ModalKind.ResourcePanelSourceConfirmation);

        Assert.Equal(ModalKind.ResourcePanel, host.Top?.Kind);
    }

    [Fact]
    public void Open_WhenKindAlreadyExists_MovesItToTopWithoutDuplicatingIt()
    {
        var host = new ModalHostViewModel();
        var content = new TestModalContent();
        host.Open(ModalKind.Settings, content);
        host.Open(ModalKind.RepairConfirmation, content);

        host.Open(ModalKind.Settings, content);

        Assert.Equal(ModalKind.Settings, host.Top?.Kind);
        Assert.Single(host.Entries, entry => entry.Kind == ModalKind.Settings);
    }

    [Fact]
    public void Close_WhenKindIsAbsent_LeavesStackUnchanged()
    {
        var host = new ModalHostViewModel();

        host.Close(ModalKind.Settings);

        Assert.False(host.HasEntries);
        Assert.Null(host.Top);
    }

    [Fact]
    public void InteractionState_WhenNestedDialogOpens_MarksUnderlyingLayersNonInteractive()
    {
        var host = new ModalHostViewModel();
        var content = new TestModalContent();

        host.Open(ModalKind.ResourcePanel, content);
        host.Open(ModalKind.ResourcePanelSourceConfirmation, content);

        // 对话框类没有对应的 Is*Interactive 属性：它自身的输入拦截由全屏遮罩承担，
        // ModalHost 只需保证下层主叠层全部让出交互权（见 AGENTS.md 的模态隔离条款）。
        Assert.False(host.IsBaseLayerInteractive);
        Assert.False(host.IsResourcePanelInteractive);
        Assert.Equal(ModalKind.ResourcePanelSourceConfirmation, host.Top?.Kind);
    }

    [Fact]
    public void InteractionState_WhenTopModalCloses_RestoresUnderlyingLayer()
    {
        var host = new ModalHostViewModel();
        var content = new TestModalContent();
        host.Open(ModalKind.Settings, content);
        host.Open(ModalKind.UnsavedSettingsConfirmation, content);

        host.Close(ModalKind.UnsavedSettingsConfirmation);

        Assert.True(host.IsSettingsInteractive);
        Assert.False(host.IsBaseLayerInteractive);
    }

    [Fact]
    public void InteractionState_WhenNoModalIsOpen_OnlyBaseLayerIsInteractive()
    {
        var host = new ModalHostViewModel();

        Assert.True(host.IsBaseLayerInteractive);
        Assert.False(host.IsSettingsInteractive);
        Assert.False(host.IsResourcePanelInteractive);
        Assert.False(host.IsLogViewerInteractive);
        Assert.False(host.IsSetupWizardInteractive);
    }

    [Fact]
    public void InteractionState_WhenExistingKindMovesToTop_UpdatesInteractiveLayer()
    {
        var host = new ModalHostViewModel();
        var content = new TestModalContent();
        host.Open(ModalKind.Settings, content);
        host.Open(ModalKind.RepairConfirmation, content);

        host.Open(ModalKind.Settings, content);

        Assert.True(host.IsSettingsInteractive);
    }

    // 模态闸口守卫：NotifyStackChanged 是手工通知清单，新增闸口时漏改其中一行
    // 会留下「表面可见但点不动」的永久过期闸口（R2-c08）。
    private static readonly IReadOnlyDictionary<ModalKind, string> GatedKinds = new Dictionary<ModalKind, string>
    {
        [ModalKind.Settings] = nameof(ModalHostViewModel.IsSettingsInteractive),
        [ModalKind.ResourcePanel] = nameof(ModalHostViewModel.IsResourcePanelInteractive),
        [ModalKind.LogViewer] = nameof(ModalHostViewModel.IsLogViewerInteractive),
        [ModalKind.LogExport] = nameof(ModalHostViewModel.IsLogExportInteractive),
        [ModalKind.Debug] = nameof(ModalHostViewModel.IsDebugInteractive),
        [ModalKind.DesignGallery] = nameof(ModalHostViewModel.IsDesignGalleryInteractive),
        [ModalKind.SetupWizard] = nameof(ModalHostViewModel.IsSetupWizardInteractive),
    };

    [Fact]
    public void NotifyStackChanged_WhenGatedKindOpensAndCloses_NotifiesEachGateProperty()
    {
        foreach (var (kind, gateProperty) in GatedKinds)
        {
            var host = new ModalHostViewModel();
            var gate = typeof(ModalHostViewModel).GetProperty(gateProperty)!;
            var notified = new List<string>();
            host.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            host.Open(kind, new TestModalContent());

            Assert.Contains(gateProperty, notified);
            Assert.Contains(nameof(ModalHostViewModel.IsBaseLayerInteractive), notified);
            Assert.True((bool)gate.GetValue(host)!);

            notified.Clear();
            host.Close(kind);

            Assert.Contains(gateProperty, notified);
            Assert.Contains(nameof(ModalHostViewModel.IsBaseLayerInteractive), notified);
            Assert.False((bool)gate.GetValue(host)!);
        }
    }

    [Fact]
    public void ModalKind_Classification_CoversEveryKindExactlyOnce()
    {
        var classified = GatedKinds.Keys.Concat(DialogKinds).ToList();

        var allKinds = Enum.GetValues<ModalKind>();
        Assert.Empty(allKinds.Except(classified));
        Assert.Equal(allKinds.Length, classified.Distinct().Count());
        foreach (var gateProperty in GatedKinds.Values)
        {
            Assert.NotNull(typeof(ModalHostViewModel).GetProperty(gateProperty));
        }
    }

    private static readonly IReadOnlyCollection<ModalKind> DialogKinds =
    [
        ModalKind.DebugResetConfirmation,
        ModalKind.Notice,
        ModalKind.Update,
        ModalKind.Error,
        ModalKind.SetupWizardExitConfirmation,
        ModalKind.UnsavedSettingsConfirmation,
        ModalKind.SettingsResetConfirmation,
        ModalKind.RepairConfirmation,
        ModalKind.ResourcePanelSourceConfirmation,
        ModalKind.UninstallConfirmation,
        ModalKind.StopConfirmation,
        ModalKind.DownloadRunningCloseConfirmation,
    ];

    private sealed class TestModalContent : IModalContentViewModel;
}
