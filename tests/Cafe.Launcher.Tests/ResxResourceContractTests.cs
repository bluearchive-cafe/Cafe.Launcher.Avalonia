using System.Globalization;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Resources;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>Runtime integration of the generated catalog and embedded satellites. Source validation belongs to the generator.</summary>
public sealed class ResxResourceContractTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("zh-Hans", ".zh-Hans")]
    [InlineData("zh-Hant", ".zh-Hant")]
    [InlineData("ja", ".ja")]
    public void ResourceManager_EmbeddedCatalog_ResolvesEveryTypedKeyWithoutParentFallback(string cultureName, string suffix)
    {
        LauncherResourceCatalog.EnsureValid();
        var expected = TestRepository.ReadResx(Path.Combine(TestRepository.ResourcesPath, $"LauncherStrings{suffix}.resx"));
        var resources = LauncherStrings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), true, false);
        Assert.NotNull(resources);
        foreach (var key in Enum.GetValues<LocalizationKeys>())
        {
            var name = LauncherResourceCatalog.Name(key);
            Assert.Equal(expected[name].Replace("\r\n", "\n"), resources.GetString(name)?.Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public void Catalog_TypedKeysRoundTripToEveryNeutralResource()
    {
        var expected = TestRepository.ReadResx(Path.Combine(TestRepository.ResourcesPath, "LauncherStrings.resx"));
        var names = Enum.GetValues<LocalizationKeys>().Select(LauncherResourceCatalog.Name).ToArray();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        foreach (var key in Enum.GetValues<LocalizationKeys>())
            Assert.Equal(key, LauncherResourceCatalog.Parse(LauncherResourceCatalog.Name(key)));
        Assert.Throws<ArgumentException>(() => LauncherResourceCatalog.Parse("not-a-resource"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LauncherResourceCatalog.Name((LocalizationKeys)(-1)));
    }

    [Theory]
    [InlineData("en-GB", "", "languageAuto")]
    [InlineData("zh-HK", ".zh-Hant", "languageAuto")]
    public void ResourceManager_RegionalCulture_UsesExpectedFallback(string cultureName, string suffix, string key)
    {
        var expected = TestRepository.ReadResx(Path.Combine(TestRepository.ResourcesPath, $"LauncherStrings{suffix}.resx"));
        Assert.Equal(expected[key], LauncherStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void ResourceManager_ContainsExpectedSatelliteAssemblies()
    {
        var directory = Path.GetDirectoryName(typeof(LauncherStrings).Assembly.Location)!;
        var assembly = typeof(LauncherStrings).Assembly.GetName().Name + ".resources.dll";
        foreach (var culture in new[] { "ja", "zh-Hans", "zh-Hant" })
            Assert.True(File.Exists(Path.Combine(directory, culture, assembly)), $"Satellite assembly missing: {culture}");
    }
}
