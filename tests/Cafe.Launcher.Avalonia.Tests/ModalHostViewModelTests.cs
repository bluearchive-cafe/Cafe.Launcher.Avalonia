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

    private sealed class TestModalContent : IModalContentViewModel;
}
