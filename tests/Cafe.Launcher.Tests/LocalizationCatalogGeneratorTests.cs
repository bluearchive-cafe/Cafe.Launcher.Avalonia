using System.Diagnostics;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class LocalizationCatalogGeneratorTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("format")]
    [InlineData("xaml")]
    [InlineData("xaml-invalid")]
    [InlineData("malformed-format")]
    public async Task Generate_InvalidInput_FailsBeforeProducingACatalog(string defect)
    {
        using var directory = TestDirectory.Create();
        var resources = directory.Sub("Resources");
        Directory.CreateDirectory(resources);
        foreach (var suffix in new[] { "", ".ja", ".zh-Hans", ".zh-Hant" })
        {
            var entries = new List<XElement> { Entry("save", "Save {0}") };
            if (suffix == ".ja")
            {
                switch (defect)
                {
                    case "missing": entries.Clear(); break;
                    case "extra": entries.Add(Entry("extra", "Extra")); break;
                    case "empty": entries[0] = Entry("save", ""); break;
                    case "duplicate": entries.Add(Entry("save", "Duplicate")); break;
                    case "format": entries[0] = Entry("save", "Save {1}"); break;
                    case "malformed-format": entries[0] = Entry("save", "Save {0"); break;
                }
            }
            new XDocument(new XElement("root", entries)).Save(Path.Combine(resources, $"LauncherStrings{suffix}.resx"));
        }
        var output = directory.Sub("UI");
        Directory.CreateDirectory(Path.Combine(output, "Constants"));
        Directory.CreateDirectory(Path.Combine(output, "Resources"));
        File.WriteAllText(Path.Combine(output, "Test.axaml"), defect.StartsWith("xaml", StringComparison.Ordinal)
            ? $"<TextBlock Text=\"{{Binding I18n[{(defect == "xaml" ? "typo" : "save-typo")}]}}\"/>" : "<TextBlock Text=\"{Binding I18n[save]}\"/>");
        var result = await RunGenerator(resources, output);
        Assert.NotEqual(0, result.ExitCode);
        var expected = defect switch
        {
            "missing" or "extra" => "Resource key set mismatch",
            "empty" => "Empty resource",
            "duplicate" => "Duplicate resource key",
            "format" => "Placeholder mismatch",
            "malformed-format" => "Invalid resource format",
            _ => "Unknown localization key"
        };
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output, "Constants", "LocalizationKeys.cs")), result.Output);
    }

    [Theory]
    [InlineData("Literal {{0}} and {0:N2}")]
    [InlineData("{{{0:N2}}}")]
    public async Task Generate_EscapedBracesAndNumericFormats_ValidatesWithoutTypedValues(string template)
    {
        using var directory = TestDirectory.Create();
        var resources = directory.Sub("Resources");
        Directory.CreateDirectory(resources);
        foreach (var suffix in new[] { "", ".ja", ".zh-Hans", ".zh-Hant" })
            new XDocument(new XElement("root", Entry("save", template)))
                .Save(Path.Combine(resources, $"LauncherStrings{suffix}.resx"));
        var output = directory.Sub("UI");
        Directory.CreateDirectory(Path.Combine(output, "Constants"));
        Directory.CreateDirectory(Path.Combine(output, "Resources"));
        var result = await RunGenerator(resources, output);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task Generate_CommittedCatalog_ChecksWithoutChangingFiles()
    {
        var result = await RunGenerator(TestRepository.ResourcesPath, TestRepository.PresentationPath, check: true);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task Check_GeneratedFileWasChanged_RejectsWithoutRewritingIt()
    {
        using var directory = TestDirectory.Create();
        var output = directory.Sub("UI");
        Directory.CreateDirectory(Path.Combine(output, "Constants"));
        Directory.CreateDirectory(Path.Combine(output, "Resources"));
        var generated = await RunGenerator(TestRepository.ResourcesPath, output);
        Assert.True(generated.ExitCode == 0, generated.Output);
        var path = Path.Combine(output, "Constants", "LocalizationKeys.cs");
        File.WriteAllText(path, "stale catalog");
        var check = await RunGenerator(TestRepository.ResourcesPath, output, check: true);
        Assert.NotEqual(0, check.ExitCode);
        Assert.Equal("stale catalog", File.ReadAllText(path));
    }

    private static XElement Entry(string key, string value) => new("data", new XAttribute("name", key), new XElement("value", value));

    private static async Task<(int ExitCode, string Output)> RunGenerator(string resources, string output, bool check = false)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-File", TestRepository.FromRepositoryRoot("scripts/Generate-LocalizationCatalog.ps1"), "-ResourcesDirectory", resources, "-OutputDirectory", output })
            start.ArgumentList.Add(argument);
        if (check) start.ArgumentList.Add("-Check");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
