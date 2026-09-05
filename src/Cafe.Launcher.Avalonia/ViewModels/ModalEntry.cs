namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Associates a modal kind with its presentation state.</summary>
public sealed record ModalEntry(ModalKind Kind, IModalContentViewModel Content);
