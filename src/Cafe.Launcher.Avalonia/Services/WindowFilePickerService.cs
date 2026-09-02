using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Window-backed <see cref="IFilePickerService"/>. The active window attaches itself
/// on construction (and detaches on close) so the singleton never roots a dead window.
/// Without an attached storage provider — e.g. in headless tests — pickers return null.
/// </summary>
public sealed class WindowFilePickerService : IFilePickerService
{
    private static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"];

    private readonly object attachLock = new();
    private TopLevel? owner;

    /// <summary>Registers the window whose storage provider serves picker requests.</summary>
    public void Attach(TopLevel topLevel)
    {
        lock (attachLock)
        {
            owner = topLevel;
        }
    }

    /// <summary>Removes the registration when the window closes.</summary>
    public void Detach(TopLevel topLevel)
    {
        lock (attachLock)
        {
            if (ReferenceEquals(owner, topLevel))
            {
                owner = null;
            }
        }
    }

    public async Task<string?> PickFolderAsync(string title, string? startLocation = null)
    {
        var storage = StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            return null;
        }

        var start = string.IsNullOrWhiteSpace(startLocation)
            ? null
            : await storage.TryGetFolderFromPathAsync(startLocation).ConfigureAwait(true);

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickImageFileAsync(string title)
    {
        var storage = StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Images")
                {
                    Patterns = ImagePatterns,
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private IStorageProvider? StorageProvider
    {
        get
        {
            lock (attachLock)
            {
                return owner?.StorageProvider;
            }
        }
    }
}
