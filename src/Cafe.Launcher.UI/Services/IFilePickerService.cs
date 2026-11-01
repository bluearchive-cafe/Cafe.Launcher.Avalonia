using System.Threading.Tasks;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// Storage-picker abstraction so ViewModels never depend on the window layer
/// (ADR: View→VM delegate injection replaced by a composition-root seam).
/// Returns the local file-system path of the single picked item, or null when
/// the dialog is cancelled or no window-owning storage provider is attached.
/// </summary>
internal interface IFilePickerService
{
    /// <summary>Picks a single folder, optionally starting at <paramref name="startLocation"/>.</summary>
    Task<string?> PickFolderAsync(string title, string? startLocation = null);

    /// <summary>
    /// Picks a single local background image (png/jpg/jpeg/bmp/webp) or video
    /// (mp4/mkv/webm), using the localized <paramref name="fileTypeName"/> for the combined filter.
    /// </summary>
    Task<string?> PickBackgroundFileAsync(string title, string fileTypeName);
}
