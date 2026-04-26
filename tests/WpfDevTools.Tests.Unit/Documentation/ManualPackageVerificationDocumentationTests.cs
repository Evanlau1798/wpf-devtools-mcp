using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ManualPackageVerificationDocumentationTests
{
    [Theory]
    [InlineData("docfx/quickstart/index.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/quickstart/ai-agent-clients.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/quickstart/claude-code.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/quickstart/openai-codex.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/quickstart/claude-desktop.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/quickstart/cursor-vscode.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/production/deployment.md", "Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.", "Run `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/ai-agent-clients.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/claude-desktop.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    [InlineData("docfx/zh-tw/production/deployment.md", "解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。", "執行 `run.bat`")]
    public void ManualPackageFallbackDocs_ShouldVerifyReleaseSidecarsBeforeExtractionOrLaunch(
        string relativePath,
        string verificationStep,
        string runStep)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        var verificationIndex = content.IndexOf(verificationStep, StringComparison.Ordinal);
        var runIndex = content.IndexOf(runStep, StringComparison.Ordinal);

        verificationIndex.Should().BeGreaterThanOrEqualTo(0,
            $"{relativePath} should make release provenance verification an explicit manual step");
        runIndex.Should().BeGreaterThanOrEqualTo(0,
            $"{relativePath} should document the package-local launcher");
        verificationIndex.Should().BeLessThan(runIndex,
            $"{relativePath} should verify release sidecars before extraction or run.bat launch");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
