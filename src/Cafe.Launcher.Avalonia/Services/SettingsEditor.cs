using System;
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public interface ISettingsEditor : INotifyPropertyChanged
{
    LauncherSettings Current { get; }
    bool IsDirty { get; }

    /// <summary>
    /// Fires for per-field changes on the <see cref="Current"/> settings object.
    /// Distinct from <see cref="INotifyPropertyChanged.PropertyChanged"/>, which fires
    /// for editor-level state changes (<see cref="Current"/> reference replacement,
    /// <see cref="IsDirty"/> transitions).
    /// </summary>
    event PropertyChangedEventHandler? CurrentPropertyChanged;

    LauncherSettings GetSnapshot();
    LauncherSettings GetSavedSnapshot();
    void ApplySnapshot(LauncherSettings settings);
    void Commit(Action<LauncherSettings> apply);
    void Discard();
}

public sealed class SettingsEditor : ISettingsEditor
{
    private LauncherSettings current;
    private LauncherSettings snapshot;
    private bool isDirty;

    public SettingsEditor()
    {
        var defaults = LauncherSettings.CreateDefaults();
        current = defaults;
        snapshot = defaults.DeepClone();
        current.PropertyChanged += OnCurrentPropertyChanged;
    }

    public LauncherSettings Current => current;

    public bool IsDirty => isDirty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangedEventHandler? CurrentPropertyChanged;

    public LauncherSettings GetSnapshot() => current.DeepClone();

    public LauncherSettings GetSavedSnapshot() => snapshot.DeepClone();

    public void ApplySnapshot(LauncherSettings settings)
    {
        current.PropertyChanged -= OnCurrentPropertyChanged;
        current = settings.DeepClone();
        current.PropertyChanged += OnCurrentPropertyChanged;
        snapshot = settings.DeepClone();
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void Commit(Action<LauncherSettings> apply)
    {
        apply(current);
    }

    public void Discard()
    {
        if (!isDirty)
        {
            return;
        }

        current.PropertyChanged -= OnCurrentPropertyChanged;
        current = snapshot.DeepClone();
        current.PropertyChanged += OnCurrentPropertyChanged;
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnCurrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CurrentPropertyChanged?.Invoke(this, e);
        OnPropertyChanged(nameof(Current));
        var newIsDirty = !SettingsMatch(current, snapshot);
        if (isDirty != newIsDirty)
        {
            isDirty = newIsDirty;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    private static bool SettingsMatch(LauncherSettings left, LauncherSettings right)
    {
        if (left.ThemeColorPalette.Count != right.ThemeColorPalette.Count)
        {
            return false;
        }

        for (var index = 0; index < left.ThemeColorPalette.Count; index++)
        {
            if (!string.Equals(
                    left.ThemeColorPalette[index],
                    right.ThemeColorPalette[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return string.Equals(left.GamePath, right.GamePath, StringComparison.Ordinal)
            && string.Equals(left.LaunchCheckMode, right.LaunchCheckMode, StringComparison.Ordinal)
            && string.Equals(left.ProxyMode, right.ProxyMode, StringComparison.Ordinal)
            && string.Equals(left.CloseBehavior, right.CloseBehavior, StringComparison.Ordinal)
            && string.Equals(left.Language, right.Language, StringComparison.Ordinal)
            && string.Equals(left.ThemeMode, right.ThemeMode, StringComparison.Ordinal)
            && string.Equals(left.MotionMode, right.MotionMode, StringComparison.Ordinal)
            && string.Equals(left.ThemeColorMode, right.ThemeColorMode, StringComparison.Ordinal)
            && string.Equals(left.CustomThemeColor, right.CustomThemeColor, StringComparison.Ordinal)
            && left.SelectedThemeColorPaletteIndex == right.SelectedThemeColorPaletteIndex
            && string.Equals(left.DownloadSpeedLimit, right.DownloadSpeedLimit, StringComparison.Ordinal)
            && left.EnableStartupUpdateCheck == right.EnableStartupUpdateCheck
            && left.ShowRemoteContentCard == right.ShowRemoteContentCard
            && string.Equals(left.PatchUrlGroup, right.PatchUrlGroup, StringComparison.Ordinal)
            && string.Equals(left.CustomBackgroundPath, right.CustomBackgroundPath, StringComparison.Ordinal)
            && string.Equals(left.BackgroundSource, right.BackgroundSource, StringComparison.Ordinal)
            && string.Equals(left.BackgroundFit, right.BackgroundFit, StringComparison.Ordinal)
            && string.Equals(left.BackgroundFillColor, right.BackgroundFillColor, StringComparison.Ordinal)
            && string.Equals(left.ResourcePanelUid, right.ResourcePanelUid, StringComparison.Ordinal)
            && string.Equals(left.ResourcePanelUidSource, right.ResourcePanelUidSource, StringComparison.Ordinal)
            && string.Equals(left.StatusDetailMode, right.StatusDetailMode, StringComparison.Ordinal)
            && string.Equals(left.UpdateChannel, right.UpdateChannel, StringComparison.Ordinal)
            && string.Equals(left.LogLevel, right.LogLevel, StringComparison.Ordinal);
    }
}
