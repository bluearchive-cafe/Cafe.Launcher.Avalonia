namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>Associates a modal kind with its presentation state.</summary>
public sealed record ModalEntry(ModalKind Kind, IModalContentViewModel Content);
