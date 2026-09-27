namespace Cafe.Launcher.Tests;

/// <summary>
/// Serializes tests that temporarily replace the process-wide localization test resources.
/// </summary>
[CollectionDefinition(nameof(LocalizationServiceTestIsolation), DisableParallelization = true)]
public sealed class LocalizationServiceTestIsolation;
