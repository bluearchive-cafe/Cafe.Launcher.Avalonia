using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Models;
using MaterialColorUtilities.Quantize;
using MaterialColorUtilities.Utils;

namespace Cafe.Launcher.Avalonia.Services;

public static class ThemeColorExtractionService
{
    private const int MaxScaledSide = 64;
    private const int MaxLeafCount = 16;
    private const int MaxPaletteColors = 5;
    private const double TargetLightValue = 0.6;
    private const double MinimumSaturation = 0.08;
    private const double MinimumValue = 0.18;
    private const byte MinimumAlpha = 32;

    public static IReadOnlyList<Color> ExtractPalette(Bitmap bitmap) =>
        ExtractPalette(bitmap, ThemeColorExtractionAlgorithms.Octree);

    public static IReadOnlyList<Color> ExtractPalette(Bitmap bitmap, string algorithm)
    {
        var sourceSize = bitmap.PixelSize;
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
        {
            return [];
        }

        var scale = MaxScaledSide / (double)Math.Max(sourceSize.Width, sourceSize.Height);
        var targetSize = new PixelSize(
            Math.Max(1, (int)Math.Round(sourceSize.Width * scale)),
            Math.Max(1, (int)Math.Round(sourceSize.Height * scale)));
        using var scaled = bitmap.CreateScaledBitmap(targetSize, BitmapInterpolationMode.LowQuality);
        using var copy = new WriteableBitmap(targetSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frameBuffer = copy.Lock();
        scaled.CopyPixels(frameBuffer);

        var buffer = new byte[frameBuffer.RowBytes * frameBuffer.Size.Height];
        Marshal.Copy(frameBuffer.Address, buffer, 0, buffer.Length);

        return ExtractPaletteFromBgraBuffer(
            buffer,
            frameBuffer.Size.Width,
            frameBuffer.Size.Height,
            frameBuffer.RowBytes,
            algorithm);
    }

    public static IReadOnlyList<Color> ExtractPaletteFromBgraBuffer(byte[] buffer, int width, int height, int rowBytes)
        => ExtractPaletteFromBgraBuffer(
            buffer,
            width,
            height,
            rowBytes,
            ThemeColorExtractionAlgorithms.Octree);

    public static IReadOnlyList<Color> ExtractPaletteFromBgraBuffer(
        byte[] buffer,
        int width,
        int height,
        int rowBytes,
        string algorithm)
    {
        if (width <= 0 || height <= 0 || rowBytes <= 0)
        {
            return [];
        }

        if (algorithm != ThemeColorExtractionAlgorithms.Octree)
        {
            return ExtractPaletteWithMaterialQuantizer(buffer, width, height, rowBytes, algorithm);
        }

        var root = new ColorOctreeNode();
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                if (offset + 3 >= buffer.Length)
                {
                    continue;
                }

                var b = buffer[offset];
                var g = buffer[offset + 1];
                var r = buffer[offset + 2];
                var a = buffer[offset + 3];
                if (a < MinimumAlpha)
                {
                    continue;
                }

                if (a is > 0 and < 255)
                {
                    r = Unpremultiply(r, a);
                    g = Unpremultiply(g, a);
                    b = Unpremultiply(b, a);
                }

                var (saturation, value) = ToSv(r, g, b);
                if (saturation < MinimumSaturation || value < MinimumValue)
                {
                    continue;
                }

                root.AddColor(Color.FromRgb(r, g, b), 0);
                while (root.LeafCount > MaxLeafCount)
                {
                    root.ReduceTree();
                }
            }
        }

        var colors = new List<ColorCount>();
        root.Collect(colors);
        return colors
            .OrderByDescending(item => Score(item.Color, item.Count))
            .ThenByDescending(item => item.Count)
            .Take(MaxPaletteColors)
            .Select(item => Color.FromArgb(0xFF, item.Color.R, item.Color.G, item.Color.B))
            .ToArray();
    }

    public static string ToColorHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static IReadOnlyList<Color> ExtractPaletteWithMaterialQuantizer(
        byte[] buffer,
        int width,
        int height,
        int rowBytes,
        string algorithm)
    {
        var pixels = new List<ArgbColor>(width * height);
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                if (offset + 3 >= buffer.Length)
                {
                    continue;
                }

                var b = buffer[offset];
                var g = buffer[offset + 1];
                var r = buffer[offset + 2];
                var a = buffer[offset + 3];
                if (a < MinimumAlpha)
                {
                    continue;
                }

                if (a is > 0 and < 255)
                {
                    r = Unpremultiply(r, a);
                    g = Unpremultiply(g, a);
                    b = Unpremultiply(b, a);
                }

                var (saturation, value) = ToSv(r, g, b);
                if (saturation < MinimumSaturation || value < MinimumValue)
                {
                    continue;
                }

                pixels.Add(new ArgbColor(0xFF, r, g, b));
            }
        }

        if (pixels.Count == 0)
        {
            return [];
        }

        var result = algorithm switch
        {
            ThemeColorExtractionAlgorithms.Wu =>
                Task.Run(() => new QuantizerWu().QuantizeAsync(pixels, MaxPaletteColors))
                    .GetAwaiter()
                    .GetResult(),
            ThemeColorExtractionAlgorithms.Wsmeans =>
                QuantizerWsmeans.Quantize(
                    pixels,
                    MaxPaletteColors,
                    new List<ArgbColor>(),
                    new PointProviderLab(),
                    maxIterations: 10,
                    returnInputPixelToClusterPixel: false),
            _ => Task.Run(() => new QuantizerCelebi().QuantizeAsync(pixels, MaxPaletteColors))
                .GetAwaiter()
                .GetResult()
        };

        return result.ColorToCount
            .OrderByDescending(item => Score(ToAvaloniaColor(item.Key), item.Value))
            .ThenByDescending(item => item.Value)
            .Take(MaxPaletteColors)
            .Select(item => ToAvaloniaColor(item.Key))
            .ToArray();
    }

    private static Color ToAvaloniaColor(ArgbColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static double Score(Color color, int count)
    {
        var (saturation, value) = ToSv(color.R, color.G, color.B);
        var right = TargetLightValue - 0.5;
        var left = TargetLightValue + 0.5;
        var valueWeight = value * (-(value - right) * (value - left) * 4);
        return (saturation + valueWeight) * Math.Log2(Math.Max(count, 1));
    }

    private static byte Unpremultiply(byte value, byte alpha) =>
        (byte)Math.Clamp((int)Math.Round(value * 255d / alpha), 0, 255);

    private static (double Saturation, double Value) ToSv(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var saturation = max == 0 ? 0 : 1d - (min / (double)max);
        return (saturation, max / 255d);
    }

    private sealed class ColorOctreeNode
    {
        private readonly ColorOctreeNode root;
        private readonly List<ColorOctreeNode>[] reducibleNodes;
        private readonly ColorOctreeNode?[] children = new ColorOctreeNode?[8];
        private bool isLeaf;
        private long red;
        private long green;
        private long blue;
        private int count;

        public ColorOctreeNode()
        {
            root = this;
            reducibleNodes = Enumerable.Range(0, 8).Select(_ => new List<ColorOctreeNode>()).ToArray();
        }

        private ColorOctreeNode(ColorOctreeNode root, int level)
        {
            this.root = root;
            reducibleNodes = root.reducibleNodes;
            if (level == 7)
            {
                isLeaf = true;
                root.LeafCount++;
            }
            else
            {
                root.reducibleNodes[level].Add(this);
            }
        }

        public int LeafCount { get; private set; }

        public void AddColor(Color color, int level)
        {
            if (isLeaf)
            {
                count++;
                red += color.R;
                green += color.G;
                blue += color.B;
                return;
            }

            var index = (((color.R >> (7 - level)) & 1) << 2)
                | (((color.G >> (7 - level)) & 1) << 1)
                | ((color.B >> (7 - level)) & 1);
            children[index] ??= new ColorOctreeNode(root, level + 1);
            children[index]!.AddColor(color, level + 1);
        }

        public void ReduceTree()
        {
            var level = 6;
            while (level >= 0 && root.reducibleNodes[level].Count == 0)
            {
                level--;
            }

            if (level < 0)
            {
                return;
            }

            var node = root.reducibleNodes[level][^1];
            root.reducibleNodes[level].RemoveAt(root.reducibleNodes[level].Count - 1);
            node.isLeaf = true;
            node.red = 0;
            node.green = 0;
            node.blue = 0;
            node.count = 0;

            foreach (var child in node.children)
            {
                if (child is null)
                {
                    continue;
                }

                node.red += child.red;
                node.green += child.green;
                node.blue += child.blue;
                node.count += child.count;
                if (child.isLeaf)
                {
                    root.LeafCount--;
                }
            }

            root.LeafCount++;
        }

        public void Collect(List<ColorCount> colors)
        {
            if (isLeaf)
            {
                if (count > 0)
                {
                    colors.Add(new ColorCount(
                        Color.FromRgb(
                            (byte)Math.Clamp((int)Math.Round(red / (double)count), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(green / (double)count), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(blue / (double)count), 0, 255)),
                        count));
                }

                return;
            }

            foreach (var child in children)
            {
                child?.Collect(colors);
            }
        }
    }

    private readonly record struct ColorCount(Color Color, int Count);
}
