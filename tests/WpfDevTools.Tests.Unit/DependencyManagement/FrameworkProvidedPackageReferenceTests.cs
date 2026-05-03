using System.Xml.Linq;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.DependencyManagement;

public sealed class FrameworkProvidedPackageReferenceTests
{
    private static readonly string[] FrameworkProvidedPackages =
    [
        "System.Net.Http",
        "System.Text.RegularExpressions"
    ];

    [Theory]
    [MemberData(nameof(FrameworkProvidedPackageNames))]
    public void DirectoryPackages_ShouldNotPinNet8FrameworkProvidedBclPackages(string packageName)
    {
        var packageVersions = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>();

        packageVersions.Should().NotContain(packageName,
            $"{packageName} is provided by net8.0 and must not be centrally pinned to an obsolete package version");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj")]
    [InlineData("tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj")]
    public void Net8TestProjects_ShouldNotDirectlyReferenceFrameworkProvidedBclPackages(string projectPath)
    {
        var packageReferences = LoadProject(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>();

        packageReferences.Should().NotContain(FrameworkProvidedPackages,
            "net8.0-windows test projects should use the framework-provided assemblies instead of obsolete compatibility packages");
    }

    public static IEnumerable<object[]> FrameworkProvidedPackageNames()
        => FrameworkProvidedPackages.Select(packageName => new object[] { packageName });

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
