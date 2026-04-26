using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class CliQuickstartElevatedRegistrationDocumentationTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Theory]
    [InlineData("docfx/quickstart/claude-code.md")]
    [InlineData("docfx/quickstart/openai-codex.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md")]
    public void CliQuickstarts_ShouldDocumentElevatedCommandPathOptIn(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1",
            $"{relativePath} should keep the safer unelevated registration workaround visible");
        content.Should().Contain("WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH=1",
            $"{relativePath} should document the explicit opt-in required before elevated CLI command path overrides are honored");
        content.Should().Contain("WPFDEVTOOLS_CODEX_COMMAND_PATH",
            $"{relativePath} should name the Codex CLI path override when elevated registration cannot be avoided");
        content.Should().Contain("WPFDEVTOOLS_CLAUDE_COMMAND_PATH",
            $"{relativePath} should name the Claude CLI path override when elevated registration cannot be avoided");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
