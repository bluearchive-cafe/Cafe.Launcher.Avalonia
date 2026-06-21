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
        if (!isDirty)
        {
            isDirty = true;
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
