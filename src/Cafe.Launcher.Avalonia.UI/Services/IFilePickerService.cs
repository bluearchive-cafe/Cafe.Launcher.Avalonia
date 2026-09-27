using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Storage-picker abstraction so ViewModels never depend on the window layer
/// (ADR: View→VM delegate injection replaced by a composition-root seam).
/// Returns the local file-system path of the single picked item, or null when
/// the dialog is cancelled or no window-owning storage provider is attached.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Picks a single folder, optionally starting at <paramref name="startLocation"/>.</summary>
    Task<string?> PickFolderAsync(string title, string? startLocation = null);

    /// <summary>Picks a single image file (png/jpg/jpeg/bmp/webp).</summary>
    Task<string?> PickImageFileAsync(string title);
}
