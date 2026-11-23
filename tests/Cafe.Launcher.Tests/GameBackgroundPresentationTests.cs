using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.Tests;

public sealed class GameBackgroundPresentationTests
{
    [Fact]
    public void Resolve_UsesWholeGameOverrideAndPreservesDefaultsAndOtherGame()
    {
        var settings = new LauncherSettings { BackgroundSource = BackgroundSources.Remote };
        var ba = settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
        ba.UseBackgroundOverride = true;
        ba.BackgroundSource = BackgroundSources.Custom;
        ba.CustomBackgroundPath = "wallpapers";
        ba.BackgroundFit = BackgroundFits.Uniform;
        ba.BackgroundFillColor = "#FF123456";
        ba.BackgroundVideoVolume = 35;
        ba.BackgroundVideoMuted = true;

        var resolved = GameBackgroundPresentation.Resolve(settings, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        Assert.Equal(BackgroundSources.Custom, resolved.BackgroundSource);
        Assert.Equal("wallpapers", resolved.CustomBackgroundPath);
        Assert.Equal(BackgroundFits.Uniform, resolved.BackgroundFit);
        Assert.Equal("#FF123456", resolved.BackgroundFillColor);
        Assert.Equal(35, resolved.BackgroundVideoVolume);
        Assert.True(resolved.BackgroundVideoMuted);
        Assert.Equal(BackgroundSources.Remote, settings.BackgroundSource);
        Assert.Equal("", settings.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Remote, GameBackgroundPresentation.Resolve(settings, LauncherProfiles.StellaSoraChina.RuntimeId).BackgroundSource);

        var preview = GameBackgroundPresentation.Resolve(settings, LauncherProfiles.BlueArchiveJapan.RuntimeId, previewGlobal: true);
        Assert.Equal(BackgroundSources.Remote, preview.BackgroundSource);
        Assert.Equal("", preview.CustomBackgroundPath);
        Assert.True(ba.UseBackgroundOverride);
        Assert.Equal("wallpapers", ba.CustomBackgroundPath);
        ba.UseBackgroundOverride = false;
        Assert.Equal(BackgroundSources.Remote, GameBackgroundPresentation.Resolve(settings, LauncherProfiles.BlueArchiveJapan.RuntimeId).BackgroundSource);
        ba.UseBackgroundOverride = true;
        Assert.Equal("wallpapers", GameBackgroundPresentation.Resolve(settings, LauncherProfiles.BlueArchiveJapan.RuntimeId).CustomBackgroundPath);
    }

    [Fact]
    public void NewsCategory_CollapsesToThreeAndNotifiesWhenContentChanges()
    {
        var category = new NewsCategory();
        var changes = new List<string?>();
        category.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        for (var i = 0; i < 5; i++) category.Items.Add(new RemoteContentItem { Title = $"News {i}" });
        Assert.True(category.CanExpand);
        Assert.Equal(3, category.VisibleItems.Count());
        category.IsExpanded = true;
        Assert.Equal(5, category.VisibleItems.Count());
        category.Items.RemoveAt(4);
        Assert.Equal(4, category.VisibleItems.Count());
        category.IsExpanded = false;
        category.Items.Clear();
        Assert.False(category.CanExpand);
        Assert.Empty(category.VisibleItems);
        Assert.Contains(nameof(NewsCategory.CanExpand), changes);
        Assert.Contains(nameof(NewsCategory.VisibleItems), changes);
    }
}
