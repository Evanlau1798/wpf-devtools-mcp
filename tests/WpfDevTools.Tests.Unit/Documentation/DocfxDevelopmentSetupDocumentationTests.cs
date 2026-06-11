using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxDevelopmentSetupDocumentationTests
{
    [Theory]
    [InlineData("docfx/contributors/development-setup.md")]
    [InlineData("docfx/zh-tw/contributors/development-setup.md")]
    public void DevelopmentSetup_ShouldDocumentApiDocPrerequisiteBuilds(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("dotnet build src/WpfDevTools.Shared/WpfDevTools.Shared.csproj -c Debug -f net8.0");
        content.Should().Contain("dotnet build src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj -c Debug -f net8.0-windows");
        content.Should().Contain("dotnet tool run docfx docfx/docfx.json");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}