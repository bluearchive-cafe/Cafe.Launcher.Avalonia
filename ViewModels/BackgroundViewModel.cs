using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class BackgroundViewModel : ViewModelBase, IDisposable
{
    private readonly ImageCacheService imageCacheService;
    private readonly LocalDiagnostics diagnostics;
    private SettingsViewModel? settings;
    private bool disposed;

    [ObservableProperty]
    private IImage? backgroundImageSource;

    [ObservableProperty]
    private Stretch backgroundStretch = Stretch.UniformToFill;

    [ObservableProperty]
    private IBrush? backgroundFillBrush;

    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }

    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }

    public string BackgroundImagePickerTitle { get; set; } = "Choose Background Image";

    public string BackgroundFolderPickerTitle { get; set; } = "Choose Background Folder";

    public BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics)
    {
        this.imageCacheService = imageCacheService;
        this.diagnostics = diagnostics;
        backgroundImageSource = LoadBundledBackground();
    }

    public void Configure(SettingsViewModel settings)
    {
        this.settings = settings;
    }

    public async Task UpdateBackgroundImageAsync(LauncherStatusSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            SetBackgroundImage(LoadBundledBackground());
            return;
        }

        BackgroundStretch = ToStretch(settings.Editor.Current.BackgroundFit);
        BackgroundFillBrush = settings.Editor.Current.BackgroundFit == BackgroundFits.Uniform
            ? new SolidColorBrush(settings.Appearance.SelectedBackgroundFillColor)
            : null;

        switch (settings.Editor.Current.BackgroundSource)
        {
            case BackgroundSources.Remote:
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                if (!string.IsNullOrWhiteSpace(bgImg) && !string.IsNullOrWhiteSpace(crc64))
                {
                    try
                    {
                        var proxyMode = snapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
                        var cachedPath = await imageCacheService.GetCachedPathAsync(crc64, cancellationToken)
                            ?? await imageCacheService.CacheImageAsync(bgImg, crc64, proxyMode, cancellationToken);
                        SetBackgroundImage(new Bitmap(cachedPath));
                        return;
                    }
                    catch (Exception ex)
                    {
                        _ = diagnostics.MessageAsync(
                            "Remote background image download failed",
                            $"url: {bgImg}\ncrc64: {crc64}\nexception: {ex.Message}",
                            CancellationToken.None);
                    }
                }
                break;

            case BackgroundSources.Custom:
                if (!string.IsNullOrWhiteSpace(settings.Editor.Current.CustomBackgroundPath))
                {
                    var customBitmap = await LoadCustomBackgroundAsync(settings.Editor.Current.CustomBackgroundPath);
                    if (customBitmap is not null)
                    {
                        SetBackgroundImage(customBitmap);
                        return;
                    }
                }
                break;
        }

        SetBackgroundImage(LoadBundledBackground());
    }

    public Bitmap? GetBackgroundBitmap()
    {
        return BackgroundImageSource as Bitmap;
    }

    public async Task<Bitmap?> LoadCustomBackgroundAsync(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return new Bitmap(path);
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background image load failed",
                    $"path: {path}\nexception: {ex.Message}");
                if (settings is not null)
                {
                    settings.Editor.Current.CustomBackgroundPath = "";
                }
                return null;
            }
        }

        if (Directory.Exists(path))
        {
            string? imagePath;
            try
            {
                imagePath = ResolveRandomBackgroundImage(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder scan failed",
                    $"path: {path}\nexception: {ex.Message}");
                return null;
            }

            if (imagePath is null)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder contains no supported images",
                    $"path: {path}");
                return null;
            }

            try
            {
                return new Bitmap(imagePath);
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder image load failed",
                    $"folder: {path}\npath: {imagePath}\nexception: {ex.Message}");
                return null;
            }
        }

        await diagnostics.MessageAsync(
            "Custom background path does not exist",
            $"path: {path}");
        return null;
    }

    public static string? ResolveRandomBackgroundImage(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var imagePaths = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedBackgroundImage)
            .ToArray();

        return imagePaths.Length == 0
            ? null
            : imagePaths[Random.Shared.Next(imagePaths.Length)];
    }

    public static bool IsSupportedBackgroundImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private void SetBackgroundImage(Bitmap? bitmap)
    {
        var old = BackgroundImageSource as IDisposable;
        BackgroundImageSource = bitmap;
        if (settings?.Editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            settings.Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            settings.Appearance.ApplyThemeColor(
                settings.Editor.Current.ThemeColorMode,
                settings.Appearance.SelectedCustomThemeColor);
        }

        if (old is not null)
        {
            Dispatcher.UIThread.Post(() => old.Dispose(), DispatcherPriority.Background);
        }
    }

    private static Bitmap? LoadBundledBackground()
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.Avalonia/Assets/bg-7b36e4e0.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(
                "LoadBundledBackground",
                $"Failed to load bundled background image: {ex.Message}");
            return null;
        }
    }

    public static Stretch ToStretch(string fit) => fit switch
    {
        BackgroundFits.Fill => Stretch.Fill,
        BackgroundFits.Uniform => Stretch.Uniform,
        _ => Stretch.UniformToFill
    };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        (BackgroundImageSource as IDisposable)?.Dispose();
    }
}
