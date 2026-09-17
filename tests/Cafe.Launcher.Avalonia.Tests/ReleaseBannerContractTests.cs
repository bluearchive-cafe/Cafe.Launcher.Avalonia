using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards the release-banner contract: the spec template under docs/promo/specs must keep
/// producing a banner at the path and canvas the release workflow expects, and must stay on the
/// scene (v2) authoring path the promotional-image pipeline treats as current, so a broken or
/// regressed template surfaces here instead of at tag time.
/// </summary>
public sealed class ReleaseBannerContractTests
{
    private const string TemplateRelativePath = "docs/promo/specs/release-banner.template.json";
    private const string BannerDirectoryRelativePath = "docs/assets/release-banners";
    private const int CanvasWidth = 2000;
    private const int CanvasHeight = 1125;
    private const double BleedTolerance = 0.5;

    [Fact]
    public void BannerTemplate_OutputImageResolvesIntoReleaseBannerDirectory()
    {
        var root = TestLocalizationHelper.FindRepositoryRoot();
        var imagePath = ReadOutputImage(root);

        // Output paths resolve against the spec file's own directory, so the template's
        // relative path is the load-bearing part: get it wrong and the PNG lands somewhere
        // release.yml never looks.
        var resolved = Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(TemplateRelativePath)!, imagePath));
        var expectedDirectory = Path.GetFullPath(Path.Combine(root, BannerDirectoryRelativePath));

        Assert.Equal(expectedDirectory, Path.GetDirectoryName(resolved));
        Assert.EndsWith("-release-banner.png", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerTemplate_DeclaresHouseCanvasAndDeterministicScale()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var canvas = document.RootElement.GetProperty("canvas");

        Assert.Equal("custom", canvas.GetProperty("preset").GetString());
        Assert.Equal(CanvasWidth, canvas.GetProperty("width").GetInt32());
        Assert.Equal(CanvasHeight, canvas.GetProperty("height").GetInt32());

        // The pipeline and its schema both reject any other scale factor, and a non-1
        // factor would silently double the exported raster.
        Assert.Equal(1, canvas.GetProperty("device_scale_factor").GetInt32());
    }

    [Fact]
    public void BannerTemplate_UsesLightMd3WithExplicitTokens()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var material = document.RootElement.GetProperty("material");

        Assert.Equal("md3", material.GetProperty("version").GetString());
        Assert.Equal("light", material.GetProperty("mode").GetString());

        // The template declares tokens outright rather than a color source: material.tokens is a
        // closed mapping, so an undeclared or camelCase role is rejected by the schema instead of
        // overriding anything, and the palette has to be stated for the poster to be light at all.
        Assert.True(
            material.GetProperty("tokens").TryGetProperty("primary", out var primary),
            "The template must declare explicit MD3 tokens; material.seed_color would instead require the MCU dependency.");
        Assert.Equal("#2E7DF6", primary.GetString());
        Assert.False(material.TryGetProperty("seed_color", out _));
        Assert.False(material.TryGetProperty("source_asset", out _));
    }

    [Fact]
    public void CommittedBanner_ForTheDeclaredProjectVersion_IsARealPngAtTheTemplatesCanvas()
    {
        // AUD-MAINT-006：本卷此前只读模板 JSON——全文件没有 File.Exists、没有解码、没有尺寸，
        // :33 那条断言作用于模板里 output.image 的字符串。于是零字节或尺寸错误的横幅照样全绿，
        // 而 release.yml 的 tag 门禁（Test-Path -PathType Leaf）也只查存在性：真到发版那一刻
        // 才会发现横幅本身是坏的。这条把「csproj 声明的版本 ↔ 该版本的横幅文件」钉死。
        var version = ProjectMetadata.ReadVersionPrefix();
        var relativePath = $"{BannerDirectoryRelativePath}/cafe-launcher-v{version}-release-banner.png";
        var bannerPath = TestRepository.FromRepositoryRoot(relativePath);

        Assert.True(
            File.Exists(bannerPath),
            $"The banner for the declared version must be committed before tagging (AGENTS.md, Release Notes): {relativePath}");

        var (width, height) = ReadPngSize(bannerPath);
        Assert.Equal(CanvasWidth, width);
        Assert.Equal(CanvasHeight, height);
    }

    [Fact]
    public void BannerTemplate_AuthorsWithSceneSchemaV2()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var root = document.RootElement;

        // v2 is the pipeline's authoring path; v1 exists only to re-render existing posters and
        // cannot run the release profile, because the renderer pins v1's profile to "legacy".
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("group", root.GetProperty("scene").GetProperty("type").GetString());

        // v1-only escape hatches: a v2 spec is rejected by the schema when these reappear, and
        // custom CSS is exactly the structurally unvalidatable path v2 replaces.
        Assert.False(root.TryGetProperty("layout", out _), "v2 has no layout section; positioning lives on the scene nodes.");
        Assert.False(root.TryGetProperty("template", out _), "v2 always renders through the freeform contract; a template id is v1-only.");
    }

    [Fact]
    public void BannerTemplate_RequestsReleaseProfileForDelivery()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var validation = document.RootElement.GetProperty("validation");

        // The banner is rendered once per release, so it runs the delivery gate: draft/review can
        // never be release-eligible, while release turns an unavailable requested font into a hard
        // render failure instead of a banner silently rendered with fallback glyphs.
        Assert.Equal("release", validation.GetProperty("profile").GetString());
        Assert.Equal("advisory", validation.GetProperty("layout").GetString());
    }

    [Fact]
    public void BannerTemplate_FontStackIsInstalledOnWindowsRenderHosts()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var stack = document.RootElement
            .GetProperty("typography").GetProperty("font_family").GetString()!;

        var families = stack.Split(',')
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToList();

        Assert.EndsWith("sans-serif", families[^1], StringComparison.Ordinal);

        // The release profile treats an unavailable family as a hard render failure, so a name
        // that cannot resolve on the render host blocks the banner outright. Noto Sans CJK SC is
        // the Linux packaging name for the family Windows exposes as Noto Sans SC; leaving it in
        // the stack turned rendering the template into a release-profile font failure on the
        // maintainer's own machine. This guards the family names, not their installation, so it
        // stays meaningful on hosts that legitimately lack any of these fonts.
        Assert.DoesNotContain("Noto Sans CJK SC", families, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Segoe UI", families, StringComparer.Ordinal);
        Assert.Contains("Microsoft YaHei UI", families, StringComparer.Ordinal);
    }

    [Fact]
    public void BannerTemplate_SceneIdsAreUniqueAndAssetReferencesResolve()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var root = document.RootElement;

        var declared = root.GetProperty("assets").EnumerateArray()
            .Select(asset => asset.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var ids = new List<string>();
        var referenced = new List<string>();
        Collect(root.GetProperty("scene"), ids, referenced);

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(referenced.Except(declared));

        // Icons are decoration, not product evidence: authentic assets would hard-fail on the
        // rotation and baked-in alpha these nodes rely on.
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            Assert.Equal("decorative", asset.GetProperty("origin_class").GetString());
        }
    }

    [Fact]
    public void BannerTemplate_BleedingNodesDeclareAllowBleed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var scene = document.RootElement.GetProperty("scene");

        // The renderer records an off-canvas node as a warning unless the node's own intent marks
        // the bleed as intended, so every node whose box (rotation included) leaves the canvas has
        // to say so. Without this the template silently gains layout findings in the manifest.
        var offenders = new List<string>();
        Walk(scene, 0, 0, offenders);

        Assert.Empty(offenders);
    }

    [Fact]
    public void BannerTemplate_KeepsReleasePlaceholders()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot(TemplateRelativePath)));
        var root = document.RootElement;

        // The template is copied per release and its placeholders are replaced in the copy, so the
        // template itself must keep them: a stray rendered version here means a release spec was
        // committed over the template.
        var badge = FindTextNode(root.GetProperty("scene"), "badge") ?? string.Empty;
        Assert.Equal("vX.Y.Z-beta.N", badge);

        var output = root.GetProperty("output");
        foreach (var name in new[] { "image", "manifest", "prepared", "components_dir" })
        {
            Assert.Contains("vX.Y.Z-beta.N", output.GetProperty(name).GetString()!, StringComparison.Ordinal);
        }
    }

    private static void Walk(JsonElement node, double originX, double originY, List<string> offenders)
    {
        var left = originX;
        var top = originY;
        if (node.TryGetProperty("style", out var style))
        {
            if (style.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number)
            {
                left = originX + x.GetDouble();
            }

            if (style.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number)
            {
                top = originY + y.GetDouble();
            }

            if (TryBox(style, out var width, out var height))
            {
                var rotation = style.TryGetProperty("rotation", out var angle) && angle.ValueKind == JsonValueKind.Number
                    ? angle.GetDouble()
                    : 0;
                var radians = rotation * Math.PI / 180;
                var halfWidth = (width * Math.Abs(Math.Cos(radians)) + height * Math.Abs(Math.Sin(radians))) / 2;
                var halfHeight = (width * Math.Abs(Math.Sin(radians)) + height * Math.Abs(Math.Cos(radians))) / 2;
                var centerX = left + width / 2;
                var centerY = top + height / 2;

                var bleeds = centerX - halfWidth < -BleedTolerance
                    || centerY - halfHeight < -BleedTolerance
                    || centerX + halfWidth > CanvasWidth + BleedTolerance
                    || centerY + halfHeight > CanvasHeight + BleedTolerance;
                var allowed = node.TryGetProperty("intent", out var intent)
                    && intent.TryGetProperty("allow_bleed", out var allowBleed)
                    && allowBleed.GetBoolean();

                if (bleeds && !allowed)
                {
                    offenders.Add(node.GetProperty("id").GetString()!);
                }
            }
        }

        // Only absolute placement resolves statically; children of a stack group are laid out by
        // flex flow, and the renderer measures those live.
        var childrenResolve = node.TryGetProperty("layout", out var layout) && layout.GetString() == "absolute";
        if (!childrenResolve || !node.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            Walk(child, left, top, offenders);
        }
    }

    private static bool TryBox(JsonElement style, out double width, out double height)
    {
        width = height = 0;
        if (!style.TryGetProperty("width", out var w) || w.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!style.TryGetProperty("height", out var h) || h.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        width = w.GetDouble();
        height = h.GetDouble();
        return true;
    }

    private static void Collect(JsonElement node, List<string> ids, List<string> referenced)
    {
        ids.Add(node.GetProperty("id").GetString()!);
        if (node.TryGetProperty("asset", out var asset))
        {
            referenced.Add(asset.GetString()!);
        }

        if (!node.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            Collect(child, ids, referenced);
        }
    }

    private static string? FindTextNode(JsonElement node, string id)
    {
        if (node.TryGetProperty("id", out var nodeId)
            && nodeId.GetString() == id
            && node.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        if (!node.TryGetProperty("children", out var children))
        {
            return null;
        }

        foreach (var child in children.EnumerateArray())
        {
            var found = FindTextNode(child, id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string ReadOutputImage(string repositoryRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, TemplateRelativePath)));

        return document.RootElement.GetProperty("output").GetProperty("image").GetString()!;
    }

    /// <summary>
    /// 读 PNG 的 IHDR 尺寸。单元工程不初始化 Avalonia，因此「可解码」以签名与 IHDR 校验为准：
    /// 这已经覆盖本条要找的失败形态（截断、零字节、非 PNG、画布不符）；真正的位图像素解码
    /// 属于无头工程的域（见它自己的 golden 用例）。
    /// </summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(
            bytes.Length > 24,
            $"{path} is too short to be a PNG ({bytes.Length} bytes).");

        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.True(
            bytes.AsSpan(0, 8).SequenceEqual(signature),
            $"{path} does not start with the PNG signature.");
        Assert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4));

        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }
}
