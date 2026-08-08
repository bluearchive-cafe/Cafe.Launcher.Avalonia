using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class SettingsAppearanceViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsEditor editor;
    private bool suppressEditorUpdates;
    private bool disposed;

    public SettingsAppearanceViewModel(ISettingsEditor editor)
    {
        this.editor = editor;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
    }

    public ISettingsEditor Editor => editor;
    public Func<Bitmap?>? GetBackgroundBitmap { get; set; }

    [ObservableProperty]
    private bool isCustomBackground;

    [ObservableProperty]
    private Color selectedBackgroundFillColor = Colors.Black;

    [ObservableProperty]
    private IBrush backgroundFillColorPreviewBrush = new SolidColorBrush(Colors.Black);

    [ObservableProperty]
    private bool isCustomBackgroundSelected;

    [ObservableProperty]
    private bool isBackgroundFitSelected;

    [ObservableProperty]
    private Color selectedCustomThemeColor = Color.Parse(LauncherConstants.DefaultThemeColor);

    [ObservableProperty]
    private IBrush themeColorPreviewBrush =
        new SolidColorBrush(Color.Parse(LauncherConstants.DefaultThemeColor));

    [ObservableProperty]
    private bool isCustomThemeColorSelected;

    [ObservableProperty]
    private bool isWallpaperThemeColorSelected;

    [ObservableProperty]
    private int selectedThemeColorPaletteIndex;

    public ObservableCollection<ThemeColorPaletteItem> ThemeColorPaletteItems { get; } = [];

    public void Load(LauncherSettings settings)
    {
        var previous = suppressEditorUpdates;
        suppressEditorUpdates = true;
        try
        {
            SelectedCustomThemeColor = ParseColorOrDefault(settings.CustomThemeColor);
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            IsCustomBackground = !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
            IsBackgroundFitSelected = settings.BackgroundFit == BackgroundFits.Uniform;
            SelectedBackgroundFillColor = ParseColorOrDefault(settings.BackgroundFillColor);
            IsCustomBackgroundSelected = settings.BackgroundSource == BackgroundSources.Custom;
            ReplaceThemeColorPalette(
                settings.ThemeColorPalette,
                settings.SelectedThemeColorPaletteIndex);
        }
        finally
        {
            suppressEditorUpdates = previous;
        }
    }

    public void RefreshThemeColorPaletteFromCurrentBackground(bool markDirty)
    {
        var bitmap = GetBackgroundBitmap?.Invoke();
        if (bitmap is null)
        {
            ReplaceThemeColorPalette([], 0);
            return;
        }

        var colors = ThemeColorExtractionService.ExtractPalette(bitmap)
            .Select(ThemeColorExtractionService.ToColorHex)
            .ToArray();
        var selectedIndex = SelectedThemeColorPaletteIndex < colors.Length
            ? SelectedThemeColorPaletteIndex
            : 0;
        ReplaceThemeColorPalette(colors, selectedIndex);
        if (markDirty)
        {
            editor.Commit(settings =>
            {
                settings.ThemeColorPalette = GetThemeColorPaletteHexes();
                settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
            });
        }
    }

    public List<string> GetThemeColorPaletteHexes() =>
        ThemeColorPaletteItems.Select(item => item.ColorHex).ToList();

    public void ApplyThemeColor(string themeColorMode, Color customColor)
    {
        if (themeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var color = ResolveThemeColor(themeColorMode, customColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
        ApplyAccentBrushes(color);
    }

    [RelayCommand]
    private void RefreshThemeColorPalette()
    {
        RefreshThemeColorPaletteFromCurrentBackground(markDirty: true);
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        }
    }

    [RelayCommand]
    private void SelectThemeColorPalette(int index)
    {
        if (ThemeColorPaletteItems.Count == 0)
        {
            return;
        }

        SelectedThemeColorPaletteIndex = Math.Clamp(
            index,
            0,
            ThemeColorPaletteItems.Count - 1);
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.ThemeColorMode))
        {
            var value = editor.Current.ThemeColorMode;
            IsCustomThemeColorSelected = value == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = value == ThemeColorModes.Wallpaper;
            if (IsWallpaperThemeColorSelected && ThemeColorPaletteItems.Count == 0)
            {
                RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }

            UpdateThemeColorPreview();
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundSource))
        {
            IsCustomBackgroundSelected =
                editor.Current.BackgroundSource == BackgroundSources.Custom;
            IsCustomBackground =
                !string.IsNullOrWhiteSpace(editor.Current.CustomBackgroundPath);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.CustomBackgroundPath))
        {
            IsCustomBackground =
                !string.IsNullOrWhiteSpace(editor.Current.CustomBackgroundPath);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundFit))
        {
            IsBackgroundFitSelected = editor.Current.BackgroundFit == BackgroundFits.Uniform;
        }
    }

    partial void OnSelectedCustomThemeColorChanged(Color value)
    {
        PushToEditor(settings => settings.CustomThemeColor = ToColorHex(value));
        UpdateThemeColorPreview();
    }

    partial void OnSelectedThemeColorPaletteIndexChanged(int value)
    {
        PushToEditor(settings => settings.SelectedThemeColorPaletteIndex = value);
        UpdateThemeColorPaletteSelection();
        UpdateThemeColorPreview();
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        }
    }

    partial void OnSelectedBackgroundFillColorChanged(Color value)
    {
        PushToEditor(settings => settings.BackgroundFillColor = ToColorHex(value));
        BackgroundFillColorPreviewBrush = new SolidColorBrush(value);
    }

    private void PushToEditor(Action<LauncherSettings> apply)
    {
        if (!suppressEditorUpdates)
        {
            editor.Commit(apply);
        }
    }

    private void ReplaceThemeColorPalette(IEnumerable<string> colors, int selectedIndex)
    {
        var normalizedColors = colors
            .Select(ParseThemeColorPaletteColor)
            .OfType<Color>()
            .Select(ThemeColorExtractionService.ToColorHex)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var previous = suppressEditorUpdates;
        suppressEditorUpdates = true;
        try
        {
            ThemeColorPaletteItems.Clear();
            for (var i = 0; i < normalizedColors.Length; i++)
            {
                var color = ParseColorOrDefault(normalizedColors[i]);
                ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
                {
                    Index = i,
                    ColorHex = normalizedColors[i],
                    Brush = new SolidColorBrush(color)
                });
            }

            SelectedThemeColorPaletteIndex = normalizedColors.Length == 0
                ? 0
                : Math.Clamp(selectedIndex, 0, normalizedColors.Length - 1);
            UpdateThemeColorPaletteSelection();
        }
        finally
        {
            suppressEditorUpdates = previous;
        }

        UpdateThemeColorPreview();
    }

    private void UpdateThemeColorPaletteSelection()
    {
        for (var i = 0; i < ThemeColorPaletteItems.Count; i++)
        {
            ThemeColorPaletteItems[i].IsSelected = i == SelectedThemeColorPaletteIndex;
        }
    }

    private void UpdateThemeColorPreview()
    {
        var color = ResolveThemeColor(
            editor.Current.ThemeColorMode,
            SelectedCustomThemeColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
    }

    private Color ResolveThemeColor(string themeColorMode, Color customColor) =>
        themeColorMode switch
        {
            ThemeColorModes.System => GetSystemAccentColor(),
            ThemeColorModes.Custom => customColor,
            ThemeColorModes.Wallpaper =>
                ResolveThemeColorFromPalette()
                ?? Color.Parse(LauncherConstants.DefaultThemeColor),
            _ => Color.Parse(LauncherConstants.DefaultThemeColor)
        };

    private Color? ResolveThemeColorFromPalette()
    {
        if (ThemeColorPaletteItems.Count == 0)
        {
            return null;
        }

        var selectedIndex = Math.Clamp(
            SelectedThemeColorPaletteIndex,
            0,
            ThemeColorPaletteItems.Count - 1);
        return ParseThemeColorPaletteColor(ThemeColorPaletteItems[selectedIndex].ColorHex);
    }

    public static void ApplyTheme(string themeMode)
    {
        var themeVariant = themeMode switch
        {
            ThemeModes.Light => ThemeVariant.Light,
            ThemeModes.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = themeVariant;
        }
    }

    internal static Color GetSystemAccentColor()
    {
        if (Application.Current?.TryGetResource(
                "SystemAccentColor",
                ThemeVariant.Default,
                out var value) == true
            && value is Color color)
        {
            return color;
        }

        return Color.Parse(LauncherConstants.DefaultThemeColor);
    }

    internal static void ApplyAccentBrushes(Color color)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var accentColor = ColorUtils.NormalizeAccentColorForUi(color);

        SetBrush(application, "LauncherAccentBrush", accentColor);
        SetBrush(application, "LauncherAccentHoverBrush", ColorUtils.AdjustColor(accentColor, 1.15));
        SetBrush(application, "LauncherAccentPressedBrush", ColorUtils.AdjustColor(accentColor, 0.85));
        SetBrush(
            application,
            "LauncherAccentSoftBrush",
            Color.FromArgb(0x24, accentColor.R, accentColor.G, accentColor.B));
        SetBrush(
            application,
            "LauncherAccentBorderBrush",
            Color.FromArgb(0x80, accentColor.R, accentColor.G, accentColor.B));
        SetBrush(
            application,
            "LauncherFocusRingBrush",
            Color.FromArgb(0x99, accentColor.R, accentColor.G, accentColor.B));
        SetBrush(application, "LauncherCarouselDotActiveBrush", accentColor);
        SetBrush(application, "LauncherToastInfoBrush", accentColor);
        SetBrush(application, "LauncherOnAccentBrush", ColorUtils.GetReadableOnAccentColor(accentColor));
        SetBrush(
            application,
            "LauncherFlatHoverBrush",
            Color.FromArgb(0x14, accentColor.R, accentColor.G, accentColor.B));
        SetBrush(
            application,
            "LauncherFlatPressedBrush",
            Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B));
        SetBrush(
            application,
            "LauncherInfoBackgroundBrush",
            Color.FromArgb(0x24, accentColor.R, accentColor.G, accentColor.B));
    }

    private static void SetBrush(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(
                key,
                ThemeVariant.Default,
                out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    internal static Color NormalizeAccentColorForUi(Color color) =>
        ColorUtils.NormalizeAccentColorForUi(color);

    internal static Color GetReadableOnAccentColor(Color color) =>
        ColorUtils.GetReadableOnAccentColor(color);

    internal static Color AdjustColor(Color color, double factor) =>
        ColorUtils.AdjustColor(color, factor);

    public static string ToColorHex(Color color) =>
        ThemeColorExtractionService.ToColorHex(color);

    public static Color ParseColorOrDefault(string? value) =>
        Color.TryParse(value, out var color)
            ? color
            : Color.Parse(LauncherConstants.DefaultThemeColor);

    internal static Color? ParseThemeColorPaletteColor(string? value) =>
        Color.TryParse(value, out var color)
            ? Color.FromArgb(0xFF, color.R, color.G, color.B)
            : null;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
    }
}
