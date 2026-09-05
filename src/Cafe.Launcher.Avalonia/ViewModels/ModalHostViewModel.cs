using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Maintains open modal entries in their actual opening order.</summary>
public sealed partial class ModalHostViewModel : ObservableObject
{
    private readonly ObservableCollection<ModalEntry> entries = [];

    /// <summary>Gets the open modal entries from bottom to top.</summary>
    public IReadOnlyList<ModalEntry> Entries => entries;

    /// <summary>Gets the most recently opened modal entry.</summary>
    public ModalEntry? Top => entries.Count == 0 ? null : entries[^1];

    /// <summary>Gets whether at least one modal is open.</summary>
    public bool HasEntries => entries.Count != 0;

    /// <summary>Gets whether the main window content is the active interaction layer.</summary>
    public bool IsBaseLayerInteractive => Top is null;

    /// <summary>Gets whether the settings overlay is the active interaction layer.</summary>
    public bool IsSettingsInteractive => Top?.Kind == ModalKind.Settings;

    /// <summary>Gets whether the resource panel is the active interaction layer.</summary>
    public bool IsResourcePanelInteractive => Top?.Kind == ModalKind.ResourcePanel;

    /// <summary>Gets whether the log viewer is the active interaction layer.</summary>
    public bool IsLogViewerInteractive => Top?.Kind == ModalKind.LogViewer;

    /// <summary>Gets whether the debug panel is the active interaction layer.</summary>
    public bool IsDebugInteractive => Top?.Kind == ModalKind.Debug;

    /// <summary>Gets whether the design gallery is the active interaction layer.</summary>
    public bool IsDesignGalleryInteractive => Top?.Kind == ModalKind.DesignGallery;

    /// <summary>Gets whether the setup wizard is the active interaction layer.</summary>
    public bool IsSetupWizardInteractive => Top?.Kind == ModalKind.SetupWizard;

    /// <summary>Gets whether a dialog above the primary overlays is interactive.</summary>
    public bool IsDialogLayerInteractive => Top is not null
        && Top.Kind is not ModalKind.Settings
        && Top.Kind is not ModalKind.ResourcePanel
        && Top.Kind is not ModalKind.LogViewer
        && Top.Kind is not ModalKind.Debug
        && Top.Kind is not ModalKind.DesignGallery
        && Top.Kind is not ModalKind.SetupWizard;

    /// <summary>Opens a modal or moves an already open modal kind to the top.</summary>
    public void Open(ModalKind kind, IModalContentViewModel content)
    {
        Close(kind);
        entries.Add(new ModalEntry(kind, content));
        NotifyStackChanged();
    }

    /// <summary>Closes the entry with the specified kind when it is open.</summary>
    public void Close(ModalKind kind)
    {
        var entry = entries.FirstOrDefault(item => item.Kind == kind);
        if (entry is null)
        {
            return;
        }

        entries.Remove(entry);
        NotifyStackChanged();
    }

    private void NotifyStackChanged()
    {
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsBaseLayerInteractive));
        OnPropertyChanged(nameof(IsSettingsInteractive));
        OnPropertyChanged(nameof(IsResourcePanelInteractive));
        OnPropertyChanged(nameof(IsLogViewerInteractive));
        OnPropertyChanged(nameof(IsDebugInteractive));
        OnPropertyChanged(nameof(IsDesignGalleryInteractive));
        OnPropertyChanged(nameof(IsSetupWizardInteractive));
        OnPropertyChanged(nameof(IsDialogLayerInteractive));
    }
}
