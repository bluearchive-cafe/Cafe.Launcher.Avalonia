using System;

namespace Cafe.Launcher.Avalonia.Services;

public static class VersionComparer
{
    public static int Compare(string? v1, string? v2)
    {
        var v1Arr = (v1 ?? "").Split('.');
        var v2Arr = (v2 ?? "").Split('.');
        var len = Math.Max(v1Arr.Length, v2Arr.Length);

        for (var i = 0; i < len; i++)
        {
            var num1 = ParseSegment(v1Arr, i);
            var num2 = ParseSegment(v2Arr, i);

            if (num1 > num2)
            {
                return 1;
            }

            if (num1 < num2)
            {
                return -1;
            }
        }

        return 0;
    }

    private static int ParseSegment(string[] values, int index)
    {
        if (index >= values.Length)
        {
            return 0;
        }

        return int.TryParse(values[index], out var value) ? value : 0;
    }
}
