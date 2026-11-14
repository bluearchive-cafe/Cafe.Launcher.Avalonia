using System.Diagnostics;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class CoverageBranchCountsTests
{
    [Fact]
    public async Task CoberturaBranchCounts_PreservePartialHitsAndDoNotGuessOverlap()
    {
        using var directory = TestDirectory.Create();
        var probe = directory.Sub("Test-CoverageBranchCounts.ps1");
        await File.WriteAllTextAsync(probe, """
            param([string]$ScriptPath)
            $ErrorActionPreference = 'Stop'
            . $ScriptPath

            $counts = @{}
            Add-CoverageBranchCount $counts 'settings' '50% (2/4)' 'unit'
            Add-CoverageBranchCount $counts 'settings' '0% (0/4)' 'headless'
            if ($counts.settings.Covered -ne 2 -or $counts.settings.Total -ne 4) {
                throw 'Partial coverage must stay at 2 of 4 branches.'
            }

            Add-CoverageBranchCount $counts 'shared' '50% (1/2)' 'unit'
            Add-CoverageBranchCount $counts 'shared' '50% (1/2)' 'headless'
            if ($counts.shared.Covered -ne 1 -or $counts.shared.Total -ne 2) {
                throw 'Two partial reports only prove one distinct branch.'
            }

            Add-CoverageBranchCount $counts 'shared' '100% (2/2)' 'full'
            if ($counts.shared.Covered -ne 2 -or $counts.shared.Total -ne 2) {
                throw 'A fully covered report must contribute both branches.'
            }

            try {
                Add-CoverageBranchCount $counts 'shared' '100% (4/4)' 'different layout'
            }
            catch {
                if ($_.Exception.Message -like 'Branch totals disagree*') { exit 0 }
                throw
            }
            throw 'Reports that disagree on the number of branches must fail.'
            """);

        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", probe, TestRepository.FromRepositoryRoot("scripts/CoverageBranchCounts.ps1")
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }
}
