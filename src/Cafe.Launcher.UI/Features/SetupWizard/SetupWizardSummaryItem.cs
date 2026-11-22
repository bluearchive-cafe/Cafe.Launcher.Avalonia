using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.SetupWizard;

/// <summary>复核页的一条图标摘要行：语义图标、标签/值、回跳步骤与「修改」动作。</summary>
internal sealed record SetupWizardSummaryItem(
    string Glyph,
    string Label,
    string Value,
    int EditStep,
    IRelayCommand<int> EditCommand,
    string EditLabel,
    bool HasDivider);
