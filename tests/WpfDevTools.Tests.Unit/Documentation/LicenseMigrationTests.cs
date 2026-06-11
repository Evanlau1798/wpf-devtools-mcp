using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class LicenseMigrationTests
{
    [Fact]
    public void RootLicense_ShouldBeUnmodifiedApache20TextOnly()
    {
        var license = ReadRepoFile("LICENSE");

        license.TrimStart().Should().StartWith("Apache License");
        license.Should().Contain("Version 2.0, January 2004");
        license.Should().Contain("http://www.apache.org/licenses/");
        license.Should().NotContain("MIT License");
        license.Should().NotContain("Microsoft Public License");
        license.Should().NotContain("Snoop WPF");
    }

    [Fact]
    public void NoticeAndTrademarkPolicy_ShouldDocumentAttributionWithoutExtraRestrictions()
    {
        var notice = ReadRepoFile("NOTICE");
        var trademark = ReadRepoFile("TRADEMARK.md");

        notice.Should().Contain("WPF DevTools MCP Server");
        notice.Should().Contain("https://github.com/Evanlau1798/wpf-devtools-mcp");
        notice.Should().NotContain("non-commercial");
        notice.Should().NotContain("do not sell");
        notice.Should().NotContain("homepage");

        trademark.Should().Contain("official status");
        trademark.Should().Contain("endorsement");
        trademark.Should().Contain("WPF");
        trademark.Should().Contain("MCP");
        trademark.Should().Contain("third-party");
    }

    [Fact]
    public void ThirdPartyNotices_ShouldPreserveSnoopMsPlAttribution()
    {
        var notices = ReadRepoFile("THIRD_PARTY_NOTICES.md");

        notices.Should().Contain("Snoop WPF");
        notices.Should().Contain("https://github.com/snoopwpf/snoopwpf");
        notices.Should().Contain("Microsoft Public License");
    }

    [Fact]
    public void ReadmeAndPackageMetadata_ShouldUseApache20()
    {
        var readme = ReadRepoFile("README.md");
        var sdkReadme = ReadRepoFile("src/WpfDevTools.Inspector.Sdk/README.md");
        var sdkProject = ReadRepoFile("src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj");

        readme.Should().Contain("Apache License, Version 2.0");
        readme.Should().Contain("Commercial use");
        readme.Should().Contain("NOTICE");
        readme.Should().Contain("TRADEMARK.md");
        readme.Should().NotContain("License: MIT");
        readme.Should().NotContain("MIT.");

        sdkReadme.Should().Contain("Apache License, Version 2.0");
        sdkProject.Should().Contain("<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>");
        sdkProject.Should().Contain("..\\..\\LICENSE");
        sdkProject.Should().Contain("..\\..\\NOTICE");
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath));
}
