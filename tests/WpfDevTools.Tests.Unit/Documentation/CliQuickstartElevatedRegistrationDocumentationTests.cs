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
    public void CliQuickstarts_ShouldDocumentElevatedCliRegistrationAsManualOrUnelevatedOnly(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1",
            $"{relativePath} should keep the safer unelevated registration workaround visible");
        if (relativePath.Contains("/zh-tw/", StringComparison.Ordinal))
        {
            content.Should().Contain("手動註冊",
                $"{relativePath} should document the manual fallback when elevated CLI registration is blocked");
        }
        else
        {
            content.Should().Contain("register manually after install",
                $"{relativePath} should document the manual fallback when elevated CLI registration is blocked");
        }

        content.Should().NotContain("WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH",
            $"{relativePath} must not document the removed elevated CLI path opt-in");
        content.Should().NotContain("If elevated registration is unavoidable",
            $"{relativePath} must not imply elevated CLI registration can be made safe with environment variables");
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
