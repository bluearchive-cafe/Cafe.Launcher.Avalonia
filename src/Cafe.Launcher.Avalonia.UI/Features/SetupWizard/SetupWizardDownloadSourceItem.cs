namespace Cafe.Launcher.Avalonia.Features.SetupWizard;

/// <summary>Represents a download source option shown by the setup wizard.</summary>
internal sealed record SetupWizardDownloadSourceItem(
    string Code,
    string DisplayName,
    bool IsRecommended,
    string RecommendationReason);
