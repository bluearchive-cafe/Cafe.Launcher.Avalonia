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
    event PropertyChangedEventHandler? CurrentPropertyChanged;
    LauncherSettings GetSnapshot();
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
        current.PropertyChanged += OnCurrentPropertyChanged;
    }

    public LauncherSettings Current => current;

    public bool IsDirty => isDirty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangedEventHandler? CurrentPropertyChanged;

    public LauncherSettings GetSnapshot() => DeepClone(current);

    public void ApplySnapshot(LauncherSettings settings)
    {
        current.PropertyChanged -= OnCurrentPropertyChanged;
        current = DeepClone(settings);
        current.PropertyChanged += OnCurrentPropertyChanged;
        snapshot = DeepClone(settings);
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
        current = DeepClone(snapshot);
        current.PropertyChanged += OnCurrentPropertyChanged;
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
