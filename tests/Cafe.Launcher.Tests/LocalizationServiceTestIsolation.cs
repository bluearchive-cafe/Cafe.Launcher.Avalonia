namespace Cafe.Launcher.Tests;

/// <summary>
/// Serializes tests that change process-wide culture settings through
/// <see cref="Cafe.Launcher.UI.Services.LocalizationService.SetLanguage"/>.
/// </summary>
[CollectionDefinition(nameof(LocalizationServiceTestIsolation), DisableParallelization = true)]
public sealed class LocalizationServiceTestIsolation;
