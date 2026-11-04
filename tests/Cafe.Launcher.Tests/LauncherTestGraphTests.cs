using Microsoft.Extensions.DependencyInjection.Extensions;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class LauncherTestGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_SettingsWriterIsResolved_ReleasesTheTransactionLock(bool suppliedSettings)
    {
        using var directory = TestDirectory.Create();
        using var settings = new SavedSettingsTestRig(directory.DataRoot);
        var graph = LauncherTestGraph.Create(directory.DataRoot, suppliedSettings
            ? new LauncherTestGraphOptions { SavedSettings = settings } : null);
        var writer = graph.Get<ISavedSettingsWriter>();
        graph.Dispose();
        graph.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.UpdateAsync(_ => { }));
    }

    [Fact]
    public async Task Dispose_ExternalSettingsWereNeverResolved_StillReleasesTheWriter()
    {
        using var directory = TestDirectory.Create();
        using var settings = new SavedSettingsTestRig(directory.DataRoot);
        var graph = LauncherTestGraph.Create(directory.DataRoot, new LauncherTestGraphOptions { SavedSettings = settings });
        graph.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => settings.Writer.UpdateAsync(_ => { }));
    }

    [Theory]
    [InlineData("operations")]
    [InlineData("dialogs")]
    [InlineData("window")]
    [InlineData("toasts")]
    public void Create_ProductionViewModelRegistrationIsMissing_DoesNotReconstructIt(string missing)
    {
        using var directory = TestDirectory.Create();
        using var graph = LauncherTestGraph.Create(directory.DataRoot, new LauncherTestGraphOptions
        {
            DispatchToUi = action => { action(); return Task.CompletedTask; },
            OpenExternalUrl = _ => { },
            ToastDelayAsync = (_, _) => Task.CompletedTask
        }, services =>
        {
            switch (missing)
            {
                case "operations": services.RemoveAll<Cafe.Launcher.UI.Features.GameOperations.GameOperationsViewModel>(); break;
                case "dialogs": services.RemoveAll<Cafe.Launcher.UI.ViewModels.DialogsViewModel>(); break;
                case "window": services.RemoveAll<Cafe.Launcher.UI.ViewModels.WindowChromeViewModel>(); break;
                case "toasts": services.RemoveAll<Cafe.Launcher.UI.ViewModels.ToastHostViewModel>(); break;
            }
        });
        Assert.Throws<InvalidOperationException>(() => graph.Family);
    }

    [Fact]
    public void Create_PlatformEffectsAreReplaced_UsesTheProductionWindowRegistration()
    {
        using var directory = TestDirectory.Create();
        var urls = new List<string?>();
        using var graph = LauncherTestGraph.Create(directory.DataRoot,
            new LauncherTestGraphOptions { OpenExternalUrl = urls.Add });
        graph.Family.WindowChrome.OpenExternalUrl("https://example.com");
        Assert.Equal(new[] { "https://example.com" }, urls);
        Assert.Same(graph.Family.Dialogs, graph.Get<Cafe.Launcher.UI.ViewModels.DialogsViewModel>());
        Assert.Same(graph.Family.Operations, graph.Get<Cafe.Launcher.UI.Features.GameOperations.GameOperationsViewModel>());
    }
}
