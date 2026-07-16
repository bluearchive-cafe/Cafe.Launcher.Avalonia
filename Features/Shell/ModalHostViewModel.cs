using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Features.Shell;

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
    }
}
