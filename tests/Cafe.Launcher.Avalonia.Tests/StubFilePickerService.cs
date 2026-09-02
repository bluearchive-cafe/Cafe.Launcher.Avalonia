using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Test seam for <see cref="IFilePickerService"/>. Configure the per-call delegates
/// to simulate picked paths; unset delegates simulate a cancelled dialog (null).
/// </summary>
public sealed class StubFilePickerService : IFilePickerService
{
    public Func<string, string?, Task<string?>>? FolderPicker { get; set; }

    public Func<string, Task<string?>>? ImagePicker { get; set; }

    public Task<string?> PickFolderAsync(string title, string? startLocation = null) =>
        FolderPicker?.Invoke(title, startLocation) ?? Task.FromResult<string?>(null);

    public Task<string?> PickImageFileAsync(string title) =>
        ImagePicker?.Invoke(title) ?? Task.FromResult<string?>(null);
}
