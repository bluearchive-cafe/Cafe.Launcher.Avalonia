using Cafe.Launcher.Avalonia.Features.Shell;

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

    private sealed class TestModalContent : IModalContentViewModel;
}
