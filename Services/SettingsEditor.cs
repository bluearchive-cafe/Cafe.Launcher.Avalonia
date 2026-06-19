using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public interface ISettingsEditor : INotifyPropertyChanged
{
    LauncherSettings Current { get; }
    bool IsDirty { get; }
    void ApplySnapshot(LauncherSettings settings);
    void Commit(Action<LauncherSettings> apply);
    void Discard();
}

public sealed class SettingsEditor : ISettingsEditor
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private LauncherSettings current;
    private LauncherSettings snapshot;
    private bool isDirty;

    public SettingsEditor()
    {
        var defaults = CreateDefaults();
        current = defaults;
        snapshot = DeepClone(defaults);
    }

    public LauncherSettings Current => current;

    public bool IsDirty => isDirty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplySnapshot(LauncherSettings settings)
    {
        current = DeepClone(settings);
        snapshot = DeepClone(settings);
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void Commit(Action<LauncherSettings> apply)
    {
        apply(current);
        isDirty = true;
        // Replace Current with a clone so XAML binding sees a new object reference
        // and re-evaluates all bound paths.
        current = DeepClone(current);
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void Discard()
    {
        if (!isDirty)
        {
            return;
        }

        current = DeepClone(snapshot);
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    private static LauncherSettings CreateDefaults()
    {
        var settings = new LauncherSettings();

        if (LauncherConstants.LauncherVersion.Contains('-'))
        {
            settings.UpdateChannel = UpdateChannels.Beta;
        }

        return settings;
    }

    private static LauncherSettings DeepClone(LauncherSettings source)
    {
        // Via JSON round-trip — simple, correct, and fast enough for a settings object
        // (~20 fields, <1KB). Avoids hand-maintained copy constructors.
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<LauncherSettings>(json, CloneOptions) ?? new LauncherSettings();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
