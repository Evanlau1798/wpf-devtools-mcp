using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerPackImportMcpTests
{
    [Fact]
    public async Task ImportUiBlockPack_ShouldDefaultToConfinedDryRun()
    {
        var projectRoot = CreateProjectRoot();
        try
        {
            var result = await UiComposerMcpTools.ImportUiBlockPack(
                ArchivePath(),
                projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("dryRun").GetBoolean().Should().BeTrue();
            payload.GetProperty("destinationRoot").GetString().Should().Be(
                Path.Combine(projectRoot, ".wpfdevtools", "packs"));
            Directory.Exists(Path.Combine(projectRoot, ".wpfdevtools")).Should().BeFalse();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task ImportUiBlockPack_ShouldRequireConfirmationBeforeWriting()
    {
        var projectRoot = CreateProjectRoot();
        try
        {
            var result = await UiComposerMcpTools.ImportUiBlockPack(
                ArchivePath(),
                projectRoot,
                dryRun: false,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            result.StructuredContent!.Value.GetProperty("errors")[0]
                .GetProperty("code").GetString().Should().Be("ImportConfirmationRequired");
            Directory.Exists(Path.Combine(projectRoot, ".wpfdevtools")).Should().BeFalse();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task ImportUiBlockPack_ShouldWriteOnlyToAllowlistedProjectPackRoot()
    {
        var projectRoot = CreateProjectRoot();
        using var writes = new EnvironmentVariableScope(
            McpServerConfiguration.AllowProjectWritesEnvVar,
            "true");
        using var roots = new EnvironmentVariableScope(
            McpServerConfiguration.AllowedProjectRootsEnvVar,
            projectRoot);
        try
        {
            var result = await UiComposerMcpTools.ImportUiBlockPack(
                ArchivePath(),
                projectRoot,
                dryRun: false,
                confirmImport: true,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("dryRun").GetBoolean().Should().BeFalse();
            File.Exists(Path.Combine(
                projectRoot,
                ".wpfdevtools",
                "packs",
                payload.GetProperty("packId").GetString()!,
                payload.GetProperty("version").GetString()!,
                "install.manifest.json")).Should().BeTrue();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static string ArchivePath()
        => TestRepositoryPaths.GetRepoFilePath("packs/baselines/wpfui/0.1.0/archives/wpfui-0.1.0.zip");

    private static string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-import-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _value;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _value = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _value);
    }
}
