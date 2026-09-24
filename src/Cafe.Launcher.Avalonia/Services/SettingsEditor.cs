using System;
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// The settings state: the draft the user edits (<see cref="Current"/>) and the last saved
/// snapshot it is compared against. In-memory and I/O-free on purpose — every production write
/// to the saved settings goes through <see cref="ISavedSettingsWriter"/>, which persists and then
/// applies the persisted value back here so the draft never disagrees with disk.
/// </summary>
public sealed class SettingsEditor : INotifyPropertyChanged
{
    private LauncherSettings current;
    private LauncherSettings snapshot;
    private bool isDirty;

    public SettingsEditor()
    {
        var defaults = LauncherSettings.CreateDefaults();
        current = defaults;
        snapshot = defaults.DeepClone();
        AttachCurrentListeners();
    }

    public LauncherSettings Current => current;

    /// <summary>
    /// Whether <see cref="Current"/> differs from the last saved snapshot — the state identity
    /// defined by <see cref="LauncherSettings.HasSameSettingsState"/>. Recomputed whenever a field
    /// on <see cref="Current"/> changes; the settings page's save button tracks nothing else.
    /// </summary>
    public bool IsDirty => isDirty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires for per-field changes on the <see cref="Current"/> settings object.
    /// Distinct from <see cref="INotifyPropertyChanged.PropertyChanged"/>, which fires
    /// for editor-level state changes (<see cref="Current"/> reference replacement,
    /// <see cref="IsDirty"/> transitions).
    /// </summary>
    public event PropertyChangedEventHandler? CurrentPropertyChanged;

    public LauncherSettings GetSnapshot() => current.DeepClone();

    public LauncherSettings GetSavedSnapshot() => snapshot.DeepClone();

    public void ApplySnapshot(LauncherSettings settings)
    {
        DetachCurrentListeners();
        current = settings.DeepClone();
        AttachCurrentListeners();
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

        DetachCurrentListeners();
        current = snapshot.DeepClone();
        AttachCurrentListeners();
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    // GameRuntime is a nested ObservableObject: edits like Current.GameRuntime.RunnerPath
    // never fire on LauncherSettings itself, so the editor must listen on the child too
    // or those changes would neither mark the session dirty nor notify CurrentPropertyChanged.
    private void AttachCurrentListeners()
    {
        current.PropertyChanged += OnCurrentPropertyChanged;
        current.GameRuntime.PropertyChanged += OnCurrentPropertyChanged;
    }

    private void DetachCurrentListeners()
    {
        current.PropertyChanged -= OnCurrentPropertyChanged;
        current.GameRuntime.PropertyChanged -= OnCurrentPropertyChanged;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnCurrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CurrentPropertyChanged?.Invoke(this, e);
        OnPropertyChanged(nameof(Current));
        var newIsDirty = !current.HasSameSettingsState(snapshot);
        if (isDirty != newIsDirty)
        {
            isDirty = newIsDirty;
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
