using System;
using System.ComponentModel;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.UI.Features.Settings;

/// <summary>A single binding surface for the background fields of a fixed settings scope.</summary>
internal sealed class BackgroundSettingsEditor : ObservableObject, IDisposable
{
    private readonly SettingsEditor editor;
    private string? gameId;

    public BackgroundSettingsEditor(SettingsEditor editor)
    {
        this.editor = editor;
        editor.PropertyChanged += OnEditorChanged;
    }

    private GameSettings? Game => gameId is null ? null : editor.Current.GetGameSettings(gameId);
    public bool IsGameSpecific => gameId is not null;
    public bool CanEdit => !IsGameSpecific || UseBackgroundOverride;
    public bool UseBackgroundOverride
    {
        get => Game?.UseBackgroundOverride ?? false;
        set
        {
            if (Game is { } game)
            {
                game.UseBackgroundOverride = value;
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    public string BackgroundSource
    {
        get => Game is { } game ? game.BackgroundSource : editor.Current.BackgroundSource;
        set
        {
            if (Game is { } game) game.BackgroundSource = value;
            else editor.Current.BackgroundSource = value;
        }
    }

    public string CustomBackgroundPath
    {
        get => Game is { } game ? game.CustomBackgroundPath : editor.Current.CustomBackgroundPath;
        set
        {
            if (Game is { } game) game.CustomBackgroundPath = value;
            else editor.Current.CustomBackgroundPath = value;
        }
    }

    public string BackgroundFit
    {
        get => Game is { } game ? game.BackgroundFit : editor.Current.BackgroundFit;
        set
        {
            if (Game is { } game) game.BackgroundFit = value;
            else editor.Current.BackgroundFit = value;
        }
    }

    public string BackgroundFillColor
    {
        get => Game is { } game ? game.BackgroundFillColor : editor.Current.BackgroundFillColor;
        set
        {
            if (Game is { } game) game.BackgroundFillColor = value;
            else editor.Current.BackgroundFillColor = value;
        }
    }

    public int BackgroundVideoVolume
    {
        get => Game is { } game ? game.BackgroundVideoVolume : editor.Current.BackgroundVideoVolume;
        set
        {
            if (Game is { } game) game.BackgroundVideoVolume = value;
            else editor.Current.BackgroundVideoVolume = value;
        }
    }

    public bool BackgroundVideoMuted
    {
        get => Game is { } game ? game.BackgroundVideoMuted : editor.Current.BackgroundVideoMuted;
        set
        {
            if (Game is { } game) game.BackgroundVideoMuted = value;
            else editor.Current.BackgroundVideoMuted = value;
        }
    }

    /// <summary>Rebinds without moving values between global and game settings.</summary>
    public void SetScope(string? targetGameId)
    {
        gameId = targetGameId;
        NotifyFields();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsEditor.Current))
        {
            NotifyFields();
        }
    }

    private void NotifyFields()
    {
        OnPropertyChanged(nameof(IsGameSpecific));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(UseBackgroundOverride));
        OnPropertyChanged(nameof(BackgroundSource));
        OnPropertyChanged(nameof(CustomBackgroundPath));
        OnPropertyChanged(nameof(BackgroundFit));
        OnPropertyChanged(nameof(BackgroundFillColor));
        OnPropertyChanged(nameof(BackgroundVideoVolume));
        OnPropertyChanged(nameof(BackgroundVideoMuted));
    }

    public void Dispose() => editor.PropertyChanged -= OnEditorChanged;
}
