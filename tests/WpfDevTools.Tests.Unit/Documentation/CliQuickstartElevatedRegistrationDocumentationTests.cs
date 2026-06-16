using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class CliQuickstartElevatedRegistrationDocumentationTests
{
    [Theory]
    [InlineData("docfx/quickstart/claude-code.md")]
    [InlineData("docfx/quickstart/openai-codex.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md")]
    public void CliQuickstarts_ShouldUseGeneratedRegistrationArtifacts(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("client-registration");
        content.Should().Contain("wpf-devtools-<arch>.exe");
        content.Should().NotContain("dotnet run --project");
        content.Should().NotContain("WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH",
            $"{relativePath} must not document the removed elevated CLI path opt-in");
        content.Should().NotContain("If elevated registration is unavoidable",
            $"{relativePath} must not imply elevated CLI registration can be made safe with environment variables");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
