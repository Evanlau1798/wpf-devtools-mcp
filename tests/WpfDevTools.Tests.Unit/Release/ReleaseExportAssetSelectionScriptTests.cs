using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseExportAssetSelectionScriptTests
{
    private static readonly string ScriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
        "scripts/tools/packaging/Export-GitHubReleaseAssets.ps1");

    [Fact]
    public void Export_WhenNestedReleaseArchiveExists_ShouldFailBeforeSidecarGeneration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "input");
            WriteArchive(inputRoot, "release_1.2.3_win-x64.zip");
            WriteArchive(inputRoot, "release_1.2.3_win-x86.zip");
            WriteArchive(inputRoot, "release_1.2.3_win-arm64.zip");
            WriteArchive(Path.Combine(inputRoot, "old"), "release_1.2.3_win-x64.zip");

            var result = RunExport(inputRoot, Path.Combine(tempRoot, "output"));

            result.ExitCode.Should().NotBe(0);
            result.Output.Should().Contain("Nested release archives are not allowed");
            result.Output.Should().Contain("old");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Export_WhenArchiveVersionDoesNotMatchTag_ShouldFailBeforeSidecarGeneration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "input");
            WriteArchive(inputRoot, "release_9.9.9_win-x64.zip");
            WriteArchive(inputRoot, "release_1.2.3_win-x86.zip");
            WriteArchive(inputRoot, "release_1.2.3_win-arm64.zip");

            var result = RunExport(inputRoot, Path.Combine(tempRoot, "output"));

            result.ExitCode.Should().NotBe(0);
            result.Output.Should().Contain("does not match release tag v1.2.3");
            result.Output.Should().Contain("release_9.9.9_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Export_WhenArchitectureArchiveIsMissing_ShouldFailBeforeSidecarGeneration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "input");
            WriteArchive(inputRoot, "release_1.2.3_win-x64.zip");
            WriteArchive(inputRoot, "release_1.2.3_win-x86.zip");

            var result = RunExport(inputRoot, Path.Combine(tempRoot, "output"));

            result.ExitCode.Should().NotBe(0);
            result.Output.Should().Contain("Missing release archive for architecture arm64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static ScriptRunResult RunExport(string inputRoot, string outputRoot)
    {
        var result = ReleaseScriptTestHarness.RunPowerShellScript(
            ScriptPath,
            ["-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3"],
            timeout: TimeSpan.FromSeconds(30));

        return new ScriptRunResult(result.ExitCode, result.Stdout + result.Stderr);
    }

    private static void WriteArchive(string root, string fileName)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, fileName), "dummy release archive");
    }

    private sealed record ScriptRunResult(int ExitCode, string Output);
}
