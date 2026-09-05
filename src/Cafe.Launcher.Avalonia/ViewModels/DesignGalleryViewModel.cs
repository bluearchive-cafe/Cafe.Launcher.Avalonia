using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Cafe.Launcher.Avalonia.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// Debug-only design gallery (spec §9 Q11): enumerates the live
/// <c>/Application.Resources</c> token dictionary and groups it by family using
/// <see cref="DesignTokenGrouping"/>, so the gallery never drifts from the token
/// set. Visibility is gated like the debug panel (Debug builds only).
/// </summary>
public sealed partial class DesignGalleryViewModel : ViewModelBase, IModalContentViewModel
{
    private readonly Func<string, string> localize;

    public DesignGalleryViewModel(Func<string, string> localize)
    {
        this.localize = localize;
    }

    [ObservableProperty]
    private bool isVisible;

    public ObservableCollection<DesignTokenGroup> Groups { get; } = [];

    [RelayCommand]
    private void Open()
    {
        Reload();
        IsVisible = true;
    }

    [RelayCommand]
    private void Close() => IsVisible = false;

    /// <summary>Re-enumerates the application resource dictionary into groups.</summary>
    public void Reload()
    {
        Groups.Clear();
        if (Application.Current is not { } application)
        {
            return;
        }

        var tokenKeys = application.Resources.Keys
            .OfType<string>()
            .Where(key => key.StartsWith("Launcher.", StringComparison.Ordinal))
            .Concat(application.Resources.ThemeDictionaries.Values
                .OfType<ResourceDictionary>()
                .SelectMany(dictionary => dictionary.Keys.OfType<string>())
                .Where(key => key.StartsWith("Launcher.", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal);
        var pairs = tokenKeys.Select(key =>
        {
            application.TryGetResource(key, application.ActualThemeVariant, out var value);
            return (key, value);
        });
        foreach (var group in DesignTokenGrouping.BuildGroups(pairs, localize))
        {
            Groups.Add(group);
        }
    }
}
