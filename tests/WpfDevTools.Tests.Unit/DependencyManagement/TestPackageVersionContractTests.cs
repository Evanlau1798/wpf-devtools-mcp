using System.Xml.Linq;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.DependencyManagement;

public sealed class TestPackageVersionContractTests
{
    [Theory]
    [InlineData("coverlet.collector", "8.0.1")]
    [InlineData("Microsoft.NET.Test.Sdk", "17.14.1")]
    [InlineData("xunit", "2.9.3")]
    [InlineData("xunit.runner.visualstudio", "2.8.2")]
    [InlineData("Xunit.StaFact", "1.2.69")]
    public void TestInfrastructurePackages_ShouldUseMaintainedStablePins(
        string packageName,
        string minimumVersion)
    {
        var version = LoadPackageVersion(packageName);

        version.Should().BeGreaterThanOrEqualTo(
            Version.Parse(minimumVersion),
            $"{packageName} should stay on a maintained stable test infrastructure version");
    }

    private static Version LoadPackageVersion(string packageName)
    {
        var versionText = XDocument.Load(TestRepositoryPaths.GetRepoFilePath("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == packageName)
            .Attribute("Version")?.Value;

        return Version.Parse(versionText!);
    }
}
