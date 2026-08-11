using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Presents localized strings to Avalonia bindings through a single key lookup.
/// The indexer is the interface: resource keys stay in the resx implementation.
/// </summary>
public sealed class LocalizedTextCatalog : INotifyPropertyChanged, IDisposable
{
    private readonly LocalizationService localizer;
    private bool disposed;

    public LocalizedTextCatalog(LocalizationService localizer)
    {
        this.localizer = localizer;
        localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets the localized text associated with a resource key.</summary>
    public string this[string key] => localizer.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localizer.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Payload raised when a resource lookup or format operation cannot produce UI text.</summary>
public sealed class LocalizationFailureEventArgs : EventArgs
{
    /// <summary>Initializes the payload with the diagnostic exception.</summary>
    public LocalizationFailureEventArgs(Exception exception)
    {
        Exception = exception;
    }

    /// <summary>Gets the failure that should be logged without being displayed verbatim.</summary>
    public Exception Exception { get; }
}

/// <summary>Resolves localized UI strings and applies the selected process culture.</summary>
public sealed class LocalizationService
{
    /// <summary>
    /// Test-only resource override. When non-null, <see cref="T"/> uses only
    /// the current language dictionary. Populated by <see cref="InitializeForTesting"/>.
    /// </summary>
    private static volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? testResources;

    private readonly SystemCultureSnapshot cultureSnapshot;
    private readonly LocalDiagnostics diagnostics;
    private string currentAutoResolvedLanguage = LauncherLanguages.English;

    /// <summary>
    /// DI constructor. Captures the OS culture snapshot and records the
    /// auto-resolved language at construction time so "auto" can restore the
    /// genuine startup culture even after a manual language selection.
    /// </summary>
    public LocalizationService(SystemCultureSnapshot cultureSnapshot, LocalDiagnostics diagnostics)
    {
        this.cultureSnapshot = cultureSnapshot;
        this.diagnostics = diagnostics;
        CaptureStartupCulture();
    }

    /// <summary>Parameterless constructor for tests and static factory methods.</summary>
    internal LocalizationService()
        : this(new SystemCultureSnapshot(), new LocalDiagnostics())
    {
    }

    /// <summary>Pre-populates resources for unit testing.</summary>
    internal static void InitializeForTesting(Dictionary<string, Dictionary<string, string>> resources)
    {
        var copy = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (locale, dict) in resources)
        {
            copy[locale] = new Dictionary<string, string>(dict, StringComparer.Ordinal);
        }

        testResources = copy;
    }

    public string CurrentLanguage { get; private set; } = LauncherLanguages.English;

    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Raised for a resource lookup or format failure so the application-level error
    /// handler can inform the user without exposing a resource key or format template.
    /// </summary>
    public event EventHandler<LocalizationFailureEventArgs>? LocalizationFailure;

    public string SetLanguage(string language)
    {
        if (language == LauncherLanguages.Auto)
        {
            cultureSnapshot.Restore();
            CultureInfo.DefaultThreadCurrentCulture = cultureSnapshot.Culture;
            CultureInfo.DefaultThreadCurrentUICulture = cultureSnapshot.UiCulture;
            CurrentLanguage = currentAutoResolvedLanguage;
        }
        else
        {
            CurrentLanguage = LauncherCultureResolver.ResolveEffectiveLanguage(language);
            ApplyCulture(CurrentLanguage);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return CurrentLanguage;
    }

    /// <summary>Captures the OS culture snapshot before manual language selection.</summary>
    public void CaptureStartupCulture()
    {
        cultureSnapshot.Capture();
        currentAutoResolvedLanguage = LauncherCultureResolver.ResolveSystemLanguage(
            cultureSnapshot.UiCulture.Name);
    }

    /// <summary>Looks up a single resource key in the active UI culture.</summary>
    public string T(string key)
    {
        var resources = testResources;
        if (resources is not null)
        {
            if (resources.TryGetValue(CurrentLanguage, out var dictionary)
                && dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            return ReportFailure($"Missing test resource key '{key}' for language '{CurrentLanguage}'.");
        }

        var result = Resources.LauncherStrings.ResourceManager.GetString(
            key, CultureInfo.CurrentUICulture);
        return result ?? ReportFailure($"Missing key '{key}' for language '{CurrentLanguage}'.");
    }

    /// <summary>Looks up and formats a resource key using the active culture.</summary>
    public string F(string key, params object?[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return ReportFailure($"Format exception for key '{key}'.");
        }
    }

    public static IReadOnlyList<LanguageOption> GetLanguageOptions() =>
        GetLanguageOptions(new LocalizationService());

    public static IReadOnlyList<LanguageOption> GetLanguageOptions(LocalizationService localizer) =>
    [
        new LanguageOption { Code = LauncherLanguages.Auto, DisplayName = localizer.T("languageAuto") },
        new LanguageOption { Code = LauncherLanguages.English, DisplayName = "English" },
        new LanguageOption { Code = LauncherLanguages.SimplifiedChinese, DisplayName = "简体中文" },
        new LanguageOption { Code = LauncherLanguages.TraditionalChinese, DisplayName = "繁體中文" },
        new LanguageOption { Code = LauncherLanguages.Japanese, DisplayName = "日本語" }
    ];

    public static string ResolveLanguage(string? language) =>
        LauncherCultureResolver.ResolveEffectiveLanguage(language);

    private void ApplyCulture(string effectiveLanguage)
    {
        var culture = LauncherCultureResolver.GetCultureFor(effectiveLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private string ReportFailure(string message)
    {
        var exception = new MissingManifestResourceException(message);
        var failureHandler = LocalizationFailure;
        if (failureHandler is null)
        {
            _ = diagnostics.ErrorAsync("Localization", exception);
        }
        else
        {
            failureHandler(this, new LocalizationFailureEventArgs(exception));
        }

        return "Localization unavailable.";
    }
}
