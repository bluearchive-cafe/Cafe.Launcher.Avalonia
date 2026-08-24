using System;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Semantic-version comparison aware of pre-release suffixes (e.g. <c>1.0.0-beta.1</c> &lt; <c>1.0.0</c>).
/// Non-numeric segments are treated as 0, matching the behaviour of the original Electron launcher.
/// </summary>
public static class VersionComparer
{
    public static int Compare(string? v1, string? v2)
    {
        var core1 = StripPrereleaseSuffix(v1 ?? "", out var suffix1);
        var core2 = StripPrereleaseSuffix(v2 ?? "", out var suffix2);

        var v1Arr = core1.Split('.');
        var v2Arr = core2.Split('.');
        var len = Math.Max(v1Arr.Length, v2Arr.Length);

        for (var i = 0; i < len; i++)
        {
            var num1 = ParseSegment(v1Arr, i);
            var num2 = ParseSegment(v2Arr, i);

            if (num1 > num2) return 1;
            if (num1 < num2) return -1;
        }

        // Numeric parts are equal — compare pre-release status.
        // Per SemVer 2.0.0: a pre-release version has lower precedence than a normal version.
        if (suffix1 is null && suffix2 is not null) return 1;
        if (suffix1 is not null && suffix2 is null) return -1;
        if (suffix1 is not null && suffix2 is not null) return ComparePrerelease(suffix1, suffix2);
        return 0;
    }

    /// <summary>Returns the version string without its pre-release suffix and the suffix itself.</summary>
    private static string StripPrereleaseSuffix(string version, out string? suffix)
    {
        suffix = null;
        if (string.IsNullOrEmpty(version)) return version;
        var dashIndex = version.IndexOf('-');
        if (dashIndex < 0) return version;
        suffix = version[(dashIndex + 1)..];
        return version[..dashIndex];
    }

    /// <summary>
    /// Compare two pre-release identifiers according to SemVer 2.0.0 §11.
    /// Numeric identifiers compare numerically; alphanumeric identifiers compare by ASCII sort order.
    /// Numeric identifiers always have lower precedence than non-numeric identifiers.
    /// A larger set of pre-release fields denotes a higher precedence than a smaller set if all
    /// preceding identifiers are equal.
    /// </summary>
    private static int ComparePrerelease(string s1, string s2)
    {
        var parts1 = s1.Split('.');
        var parts2 = s2.Split('.');
        var maxLen = Math.Max(parts1.Length, parts2.Length);

        for (var i = 0; i < maxLen; i++)
        {
            if (i >= parts1.Length) return -1;
            if (i >= parts2.Length) return 1;

            var n1IsNumeric = int.TryParse(parts1[i], out var n1);
            var n2IsNumeric = int.TryParse(parts2[i], out var n2);

            if (n1IsNumeric && n2IsNumeric)
            {
                if (n1 > n2) return 1;
                if (n1 < n2) return -1;
            }
            else if (n1IsNumeric != n2IsNumeric)
            {
                // Numeric identifiers have lower precedence than alphanumeric (SemVer 2.0.0 §11)
                return n1IsNumeric ? -1 : 1;
            }
            else
            {
                var cmp = string.Compare(parts1[i], parts2[i], StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
        }

        return 0;
    }

    private static int ParseSegment(string[] values, int index)
    {
        if (index >= values.Length) return 0;
        return int.TryParse(values[index], out var value) ? value : 0;
    }
}
