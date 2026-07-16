using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Features.SetupWizard;

/// <summary>Represents one navigable step in the first-launch setup workflow.</summary>
public sealed partial class SetupWizardStepItem : ObservableObject
{
    /// <summary>Gets the zero-based step index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the one-based step number shown in navigation.</summary>
    public int DisplayNumber => Index + 1;

    /// <summary>Gets the localized step title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets whether this step is current, completed, or locked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    [NotifyPropertyChangedFor(nameof(CanNavigate))]
    private SetupWizardStepState state;

    /// <summary>Gets whether this is the current step.</summary>
    public bool IsCurrent => State == SetupWizardStepState.Current;

    /// <summary>Gets whether this step has been completed.</summary>
    public bool IsCompleted => State == SetupWizardStepState.Completed;

    /// <summary>Gets whether this step is unavailable.</summary>
    public bool IsLocked => State == SetupWizardStepState.Locked;

    /// <summary>Gets whether the user can navigate to this step.</summary>
    public bool CanNavigate => !IsLocked;
}
