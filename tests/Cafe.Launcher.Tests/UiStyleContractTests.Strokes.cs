using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed partial class UiStyleContractTests
{
    [Fact]
    public void ProductionMarkup_DoesNotUseHalfDipStrokesOrDividers()
    {
        var paths = ProjectMarkupFiles()
            .Select(TestRepository.FromPresentationRoot)
            .Concat(Directory.GetFiles(TestRepository.HostPath, "*.axaml", SearchOption.TopDirectoryOnly));
        var documents = paths.Select(path => XDocument.Load(path, LoadOptions.SetLineInfo | LoadOptions.SetBaseUri));

        var offenders = FindHalfDipStrokesAndDividers(documents);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static IReadOnlyList<string> FindHalfDipStrokesAndDividers(IEnumerable<XDocument> documents)
    {
        var elements = documents.SelectMany(document => document.Descendants()).ToArray();
        var xamlKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = elements
            .Where(element => element.Attribute(xamlKey) is not null)
            .ToLookup(element => element.Attribute(xamlKey)!.Value, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var element in elements)
        {
            if (element.Name.LocalName == "Setter" && element.Attribute("Property") is { } property)
            {
                var target = element.Ancestors().FirstOrDefault(ancestor =>
                    ancestor.Name.LocalName is "Style" or "ControlTheme" or "ControlTemplate");
                var selector = target?.Attribute("Selector")?.Value ?? target?.Attribute("TargetType")?.Value;
                var targetsDivider = selector?.Split(',').Any(part =>
                    IsDivider(part.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault())) == true;
                Check(property, property.Value, element.Attribute("Value")?.Value
                    ?? ReadStrokeValue(element.Element(element.Name.Namespace + "Setter.Value")), targetsDivider);
                continue;
            }

            var isDivider = IsDivider(element.Name.LocalName)
                || element.Attributes().Any(attribute =>
                    attribute.Name.LocalName is "Classes" or "Name" && IsDivider(attribute.Value))
                || (element.Name.LocalName == "MenuItem" && element.Attribute("Header")?.Value == "-");
            foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.Namespace == XNamespace.None))
            {
                Check(attribute, attribute.Name.LocalName, attribute.Value, isDivider);
            }

            foreach (var propertyElement in element.Elements().Where(child => child.Name.LocalName.Contains('.')))
            {
                Check(propertyElement, propertyElement.Name.LocalName, ReadStrokeValue(propertyElement), isDivider);
            }
        }

        return offenders;

        void Check(XObject source, string property, string? value, bool isDivider)
        {
            var propertyName = property[(property.LastIndexOf('.') + 1)..].TrimEnd(')');
            if (value is null)
            {
                return;
            }

            var isThickness = propertyName is "BorderThickness" or "StrokeThickness";
            var isDimension = propertyName is "Height" or "MinHeight" or "MaxHeight" or "Width" or "MinWidth" or "MaxWidth";
            var resourceKey = StrokeResourceReference().Match(value.Trim()).Groups["key"].Value;
            if (!isThickness && !(isDimension && (isDivider || IsDivider(resourceKey))))
            {
                return;
            }

            if (UsesHalfDip(value, []))
            {
                var path = Path.GetRelativePath(TestRepository.Root, new Uri(source.BaseUri).LocalPath);
                offenders.Add($"{path}:{((IXmlLineInfo)source).LineNumber} {property}=\"{value}\" resolves to 0.5 DIP.");
            }
        }

        bool UsesHalfDip(string value, HashSet<string> visiting)
        {
            var reference = StrokeResourceReference().Match(value.Trim());
            if (reference.Success)
            {
                var key = reference.Groups["key"].Value;
                if (!visiting.Add(key))
                {
                    return false;
                }

                var usesHalfDip = resources[key].Any(resource =>
                    ReadStrokeValue(resource) is { } resourceValue && UsesHalfDip(resourceValue, visiting));
                visiting.Remove(key);
                return usesHalfDip;
            }

            return !value.TrimStart().StartsWith('{')
                && value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                    && size == 0.5);
        }
    }

    private static string? ReadStrokeValue(XElement? element)
    {
        if (element?.Name.LocalName is "StaticResource" or "DynamicResource")
        {
            var key = element.Attribute("ResourceKey")?.Value;
            return key is null ? null : $"{{{element.Name.LocalName} {key}}}";
        }

        if (element?.HasElements != true)
        {
            return element?.Value;
        }

        return element.Elements().Take(2).ToArray() is [var child] ? ReadStrokeValue(child) : null;
    }

    private static bool IsDivider(string? target) =>
        target?.Contains("divider", StringComparison.OrdinalIgnoreCase) == true
        || target?.Contains("separator", StringComparison.OrdinalIgnoreCase) == true;

    [GeneratedRegex(@"^\{(?:StaticResource|DynamicResource)\s+(?<key>[^\s{}]+)\s*\}$", RegexOptions.CultureInvariant)]
    private static partial Regex StrokeResourceReference();
}
