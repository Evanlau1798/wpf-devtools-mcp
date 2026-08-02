using FluentAssertions;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerProjectResourceGlobTests
{
    [Theory]
    [InlineData(@"Assets\*.png", null, @"Assets\hero.png", false)]
    [InlineData(@"Assets\*.jpg", null, @"Assets\hero.png", true)]
    [InlineData(@"Assets\**\*.png", null, @"Assets\cards\hero.png", false)]
    [InlineData(@"Assets\*.png", @"Assets\hero.png", @"Assets\hero.png", true)]
    public void PatchProject_ShouldRespectExistingResourceGlobs(
        string includePattern,
        string? excludePattern,
        string resourcePath,
        bool shouldAddExplicitResource)
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-resource-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Test.csproj");
            var resource = new XElement("Resource", new XAttribute("Include", includePattern));
            if (excludePattern is not null)
            {
                resource.SetAttributeValue("Exclude", excludePattern);
            }

            new XDocument(new XElement(
                    "Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("ItemGroup", resource)))
                .Save(projectPath);

            var result = ProjectIntegrationXmlPatcher.PatchProject(
                projectPath,
                Array.Empty<RequiredNuGetPackage>(),
                central: false,
                [resourcePath]);

            result.Success.Should().BeTrue(result.Error?.Message);
            var document = XDocument.Parse(result.Content);
            var explicitCount = document.Descendants("Resource").Count(element =>
                string.Equals((string?)element.Attribute("Include"), resourcePath, StringComparison.OrdinalIgnoreCase));
            explicitCount.Should().Be(shouldAddExplicitResource ? 1 : 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
