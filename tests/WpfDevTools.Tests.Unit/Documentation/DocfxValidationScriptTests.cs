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

    private static string CreateFixture()
        => Path.Combine(Path.GetTempPath(), $"wpf-devtools-docfx-validation-{Guid.NewGuid():N}");

    private static void WriteValidDocumentationFixture(string root)
    {
        WriteFile(root, "docfx/index.md", "# Home\n\n[Quickstart](quickstart/index.md#setup)\n");
        WriteFile(root, "docfx/quickstart/index.md", "# Quickstart\n\n## Setup\n");
        WriteFile(root, "docfx/reference/tools/index.md", "# Tools\n\n- `connect`\n");
        WriteFile(root, "docfx/zh-tw/index.md", "# 首頁\n\n[快速開始](quickstart/index.md#setup)\n");
        WriteFile(root, "docfx/zh-tw/quickstart/index.md", "# 快速開始\n\n## Setup\n");
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
            """[McpServerTool(Name = "connect", Title = "Connect")] public static class FakeMcpTools { }""");
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
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record ScriptResult(int ExitCode, string CombinedOutput);
}
