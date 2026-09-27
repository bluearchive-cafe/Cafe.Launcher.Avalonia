namespace Cafe.Launcher.UI.ViewModels;

/// <summary>Associates a modal kind with its presentation state.</summary>
internal sealed record ModalEntry(ModalKind Kind, IModalContentViewModel Content);
