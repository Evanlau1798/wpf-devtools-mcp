using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxValidationScriptTests
{
    private static readonly string ScriptPath = TestRepositoryPaths.GetRepoFilePath(
        "scripts/ci/Test-DocFxDocumentation.ps1");

    [Fact]
    public void Script_ShouldPassForGeneratedPagesLinksParityAndToolCoverage()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().Contain("DocFX documentation validation passed");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldAllowBriefReadmeThatLinksToReleaseLayout()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            WriteReleaseLayoutFixture(fixture);
            WriteFile(fixture, "README.md", """
                # WPF DevTools MCP Server

                Use the Release Layout documentation for release verification details.

                ViewModel policy terms: get_datacontext_chain, capture_state_snapshot,
                batch_mutate, wait_for_dp_change_after_mutation.
                """);

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().Contain("DocFX documentation validation passed");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldIgnoreAgentFeedbackMarkdownExcludedFromPublicDocfx()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            WriteFile(fixture, "docfx/agent-feedback/index.md", "# Agent feedback\n");
            WriteFile(fixture, "docfx/zh-tw/agent-feedback/index.md", "# Agent feedback\n");

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().Contain("DocFX documentation validation passed");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldFailWhenExcludedAgentFeedbackOutputRemainsInSite()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            WriteFile(fixture, "docfx/_site/agent-feedback/index.html",
                """<html><body><h1>Agent feedback</h1></body></html>""");

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("Excluded public DocFX output remains in _site");
            result.CombinedOutput.Should().Contain("agent-feedback");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldFailWhenGeneratedInternalAnchorIsMissing()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            File.WriteAllText(Path.Combine(fixture, "docfx", "_site", "index.html"),
                """<html><body><a href="quickstart/index.html#missing-anchor">broken</a></body></html>""");

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("Broken internal link");
            result.CombinedOutput.Should().Contain("missing-anchor");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldFailWhenZhTwParityOrToolCoverageIsMissing()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            File.Delete(Path.Combine(fixture, "docfx", "zh-tw", "quickstart", "index.md"));
            File.WriteAllText(Path.Combine(fixture, "docfx", "_site", "zh-tw", "reference", "tools", "index.html"),
                """<html><body><h1 id="tools">Tools</h1></body></html>""");

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("Missing zh-TW DocFX page");
            result.CombinedOutput.Should().Contain("Missing generated zh-TW tool reference");
            result.CombinedOutput.Should().Contain("connect");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldDiscoverToolNamesWhenAttributeNameIsNotFirstArgument()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(
                fixture,
                """
                [McpServerTool(
                    Title = "Connect",
                    Name = "connect")]
                public static class FakeMcpTools { }
                """);

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().Contain("DocFX documentation validation passed");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldFailWhenGeneratedContractSnapshotIsStale()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            var sourceToolHash = new string('a', 64);
            var sourceResponseHash = new string('b', 64);
            var staleToolHash = new string('c', 64);
            WriteFile(fixture, "docfx/reference/tools/index.md", $$"""
                # Tools

                ## Generated Contract Snapshot

                - `wpf://contracts/tools` SHA-256: `{{sourceToolHash}}`
                - `wpf://contracts/response` SHA-256: `{{sourceResponseHash}}`

                - `connect`
                """);
            WriteFile(fixture, "docfx/_site/reference/tools/index.html", $$"""
                <html><body>
                <h1 id="tools">Tools</h1>
                <h2 id="generated-contract-snapshot">Generated Contract Snapshot</h2>
                <ul>
                <li><code>wpf://contracts/tools</code> SHA-256: <code>{{staleToolHash}}</code></li>
                <li><code>wpf://contracts/response</code> SHA-256: <code>{{sourceResponseHash}}</code></li>
                </ul>
                <code>connect</code>
                </body></html>
                """);

            var result = RunValidationScript(fixture);

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("Stale generated contract snapshot");
            result.CombinedOutput.Should().Contain("wpf://contracts/tools");
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldWriteDocFxEvidenceJson()
    {
        var fixture = CreateFixture();
        try
        {
            WriteValidDocumentationFixture(fixture);
            var evidencePath = Path.Combine(fixture, "artifacts", "docfx-evidence.json");

            var result = RunValidationScript(fixture, evidencePath);

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            File.Exists(evidencePath).Should().BeTrue();
            using var evidence = JsonDocument.Parse(File.ReadAllText(evidencePath));
            evidence.RootElement.GetProperty("englishParity").GetBoolean().Should().BeTrue();
            evidence.RootElement.GetProperty("zhTwParity").GetBoolean().Should().BeTrue();
            evidence.RootElement.GetProperty("brokenLinks").GetInt32().Should().Be(0);
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public void Script_ShouldUsePortableRelativePathJoins()
    {
        var content = File.ReadAllText(ScriptPath);

        content.Should().NotContain("Replace('/', '\\')",
            "DocFX validation must not normalize canonical relative paths to Windows-only separators");
        content.Should().NotContain("Join-Path $SiteRoot $expectedRelative",
            "relative paths that may contain separators should be joined segment-by-segment");
    }

    [Fact]
    public void Script_ShouldNotScrapeMcpToolAttributesWithRegex()
    {
        var content = File.ReadAllText(ScriptPath);

        content.Should().Contain("Get-McpToolNames");
        content.Should().NotContain("McpServerTool\\s*\\(",
            "DocFX coverage should prefer the canonical tool manifest and only use a structured fallback for lightweight fixtures");
    }

    [Fact]
    public async Task DeleteFixture_WhenGeneratedSiteDirectoryIsTemporarilyLocked_ShouldRetry()
    {
        var fixture = CreateFixture();
        var lockedDirectory = Path.Combine(fixture, "docfx", "_site", "reference");
        Directory.CreateDirectory(lockedDirectory);
        var lockedFile = Path.Combine(lockedDirectory, "locked.txt");

        using var stream = new FileStream(
            lockedFile,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(250);
            stream.Dispose();
        });

        try
        {
            var act = () => DeleteFixture(fixture);

            act.Should().NotThrow();
            Directory.Exists(fixture).Should().BeFalse();
        }
        finally
        {
            stream.Dispose();
            await releaseTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(fixture))
            {
                Directory.Delete(fixture, recursive: true);
            }
        }
    }

    private static string CreateFixture()
        => Path.Combine(Path.GetTempPath(), $"wpf-devtools-docfx-validation-{Guid.NewGuid():N}");

    private static void WriteValidDocumentationFixture(string root, string? fakeToolSource = null)
    {
        const string sidecars = "`SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json`";
        WriteFile(root, "docfx/index.md", $"# Home\n\n{sidecars}\n\n[Quickstart](quickstart/index.md#setup)\n");
        WriteFile(root, "docfx/quickstart/index.md", $"# Quickstart\n\n{sidecars}\n\n## Setup\n");
        WriteFile(root, "docfx/reference/tools/index.md", "# Tools\n\n- `connect`\n");
        WriteFile(root, "docfx/zh-tw/index.md", $"# 首頁\n\n{sidecars}\n\n[快速開始](quickstart/index.md#setup)\n");
        WriteFile(root, "docfx/zh-tw/quickstart/index.md", $"# 快速開始\n\n{sidecars}\n\n## Setup\n");
        WriteFile(root, "docfx/zh-tw/reference/tools/index.md", "# Tools\n\n- `connect`\n");
        WriteFile(root, "docfx/_site/index.html",
            """<html><body><h1 id="home">Home</h1><a href="quickstart/index.html#setup">quickstart</a></body></html>""");
        WriteFile(root, "docfx/_site/quickstart/index.html",
            """<html><body><h1 id="quickstart">Quickstart</h1><h2 id="setup">Setup</h2></body></html>""");
        WriteFile(root, "docfx/_site/reference/tools/index.html",
            """<html><body><h1 id="tools">Tools</h1><code>connect</code></body></html>""");
        WriteFile(root, "docfx/_site/zh-tw/index.html",
            """<html><body><h1 id="home">Home</h1><a href="quickstart/index.html#setup">quickstart</a></body></html>""");
        WriteFile(root, "docfx/_site/zh-tw/quickstart/index.html",
            """<html><body><h1 id="quickstart">Quickstart</h1><h2 id="setup">Setup</h2></body></html>""");
        WriteFile(root, "docfx/_site/zh-tw/reference/tools/index.html",
            """<html><body><h1 id="tools">Tools</h1><code>connect</code></body></html>""");
        WriteFile(root, "src/WpfDevTools.Mcp.Server/McpTools/FakeMcpTools.cs",
            fakeToolSource ?? """[McpServerTool(Name = "connect", Title = "Connect")] public static class FakeMcpTools { }""");
    }

    private static void WriteReleaseLayoutFixture(string root)
    {
        const string sidecars = "`SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json`";
        WriteFile(root, "docfx/production/release-layout.md", $"# Release Layout\n\n{sidecars}\n");
        WriteFile(root, "docfx/zh-tw/production/release-layout.md", $"# Release Layout\n\n{sidecars}\n");
        WriteFile(root, "docfx/_site/production/release-layout.html",
            """<html><body><h1 id="release-layout">Release Layout</h1></body></html>""");
        WriteFile(root, "docfx/_site/zh-tw/production/release-layout.html",
            """<html><body><h1 id="release-layout">Release Layout</h1></body></html>""");
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ScriptResult RunValidationScript(string repoRoot, string? evidenceOutputPath = null)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(repoRoot);
        if (!string.IsNullOrWhiteSpace(evidenceOutputPath))
        {
            startInfo.ArgumentList.Add("-EvidenceOutputPath");
            startInfo.ArgumentList.Add(evidenceOutputPath);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output + error);
    }

    private static void DeleteFixture(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception ex) when (IsTransientDeleteFailure(ex) && attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    private static bool IsTransientDeleteFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException;

    private sealed record ScriptResult(int ExitCode, string CombinedOutput);
}
