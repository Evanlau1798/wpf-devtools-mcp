using FluentAssertions;
using System.Diagnostics;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackRegistryTests
{
    [Theory]
    [InlineData(@"\\controlled.invalid\share")]
    [InlineData("//controlled.invalid/share")]
    [InlineData(@"\\?\UNC\controlled.invalid\share")]
    public void ComposerLocalPathPolicy_ShouldRejectNetworkPathSyntax(string path)
    {
        var act = () => ComposerLocalPathPolicy.RequireLocalRoot(path, "projectRoot");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*local*");
    }

    [Fact]
    public void ComposerLocalPathPolicy_ShouldRejectMappedNetworkDrive()
    {
        var act = () => ComposerLocalPathPolicy.RequireLocalRoot(
            Path.GetFullPath("C:/packs"),
            "projectRoot",
            static _ => DriveType.Network);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*local*");
    }

    [Fact]
    public void ComposerLocalPathPolicy_ShouldRejectReparsePointAncestor()
    {
        var tempRoot = CreateTempDirectory();
        var target = Path.Combine(tempRoot, "target");
        var junction = Path.Combine(tempRoot, "junction");
        Directory.CreateDirectory(Path.Combine(target, "packs"));
        try
        {
            CreateDirectoryJunction(junction, target);

            var act = () => ComposerLocalPathPolicy.RequireLocalRoot(
                Path.Combine(junction, "packs"),
                "projectRoot");

            act.Should().Throw<ArgumentException>()
                .WithMessage("*reparse point*");
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }

            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackPathContract_ShouldUseDocumentedRoots()
    {
        ComposerPackPaths.BuiltinRoot("C:/repo").Should().Be(Path.GetFullPath("C:/repo/packs/builtin"));
        ComposerPackPaths.ProjectLocalRoot("C:/app").Should().Be(Path.GetFullPath("C:/app/.wpfdevtools/packs"));
        ComposerPackPaths.UserGlobalRoot("C:/local").Should().Be(Path.GetFullPath("C:/local/WpfDevTools/Composer/Packs"));
    }

    [Fact]
    public void PackRegistry_ShouldDiscoverBuiltInWpfUiPackWithMetadata()
    {
        var registry = PackRegistry.ForRepository(GetRepoFilePath("."));

        var result = registry.ListPacks();

        result.Diagnostics.Should().BeEmpty();
        var pack = result.Packs.Should().ContainSingle(p => p.Id == "wpfui").Subject;
        pack.Version.Should().Be("0.1.0");
        pack.Scope.Should().Be(PackScope.Builtin);
        pack.BlockCount.Should().Be(18);
        pack.ReadinessValid.Should().BeTrue();
        pack.SourceRepository.Should().Be("https://github.com/lepoco/wpfui");
        pack.Kind.Should().Be("skill-generated-style-pack");
        pack.ThemeTokens["spacing.medium"].GetString().Should().Be("12");
    }

    [Fact]
    public void PackRegistry_ShouldLoadPackArtifactsAndRendererTemplateMetadata()
    {
        var registry = PackRegistry.ForRepository(GetRepoFilePath("."));

        var result = registry.ListPacks();

        var pack = result.Packs.Single(p => p.Id == "wpfui");
        pack.BlockKinds.Should().Contain("wpfui.navigationView");
        pack.RecipeCount.Should().Be(4);
        pack.ExampleCount.Should().Be(1);
        pack.RendererCount.Should().Be(18);
    }

    [Fact]
    public void PackRegistry_ShouldRejectRendererTemplatesOutsidePackRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            CopyPackTo(ComposerPackPaths.ProjectLocalRoot(projectRoot), enabled: true);
            File.WriteAllText(
                Path.Combine(ComposerPackPaths.ProjectLocalRoot(projectRoot), "wpfui", "0.1.0", "blocks", "button.block.json"),
                """
                {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"wpfui.button","displayName":"Button","category":"input","properties":{},"slots":{},"renderer":{"xamlTemplate":"../outside.xaml.sbn"},"sourceHints":[]}
                """);

            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(tempRoot),
                ComposerPackPaths.ProjectLocalRoot(projectRoot));

            var result = registry.ListPacks();

            result.Packs.Should().BeEmpty();
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("escapes pack root", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldApplyProjectUserBuiltinPrecedenceAndSkipDisabledPacks()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var userRoot = Path.Combine(tempRoot, "local");
            CopyPackTo(ComposerPackPaths.ProjectLocalRoot(projectRoot), enabled: false);
            CopyPackTo(ComposerPackPaths.UserGlobalRoot(userRoot), enabled: true);

            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(GetRepoFilePath(".")),
                ComposerPackPaths.ProjectLocalRoot(projectRoot),
                ComposerPackPaths.UserGlobalRoot(userRoot));

            var result = registry.ListPacks();

            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("disabled", StringComparison.OrdinalIgnoreCase));
            result.Packs.Single(pack => pack.Id == "wpfui").Scope.Should().Be(PackScope.UserGlobal);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldSupportMultiplePacksAndReportDifferentVersions()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var userRoot = Path.Combine(tempRoot, "local");
            CreateMinimalPack(ComposerPackPaths.ProjectLocalRoot(projectRoot), "wpfui", "9.9.9");
            CreateMinimalPack(ComposerPackPaths.ProjectLocalRoot(projectRoot), "sample", "1.0.0");
            CopyPackTo(ComposerPackPaths.UserGlobalRoot(userRoot), enabled: true);

            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(tempRoot),
                ComposerPackPaths.ProjectLocalRoot(projectRoot),
                ComposerPackPaths.UserGlobalRoot(userRoot));

            var result = registry.ListPacks();

            result.Packs.Select(pack => pack.Id).Should().BeEquivalentTo("sample", "wpfui");
            result.Packs.Single(pack => pack.Id == "wpfui").Scope.Should().Be(PackScope.ProjectLocal);
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("multiple versions", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldDeriveRoleFromArbitraryPackKind()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectPackRoot = ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project"));
            CreateMinimalPack(projectPackRoot, "sample-theme", "1.0.0", "style-pack");

            var result = new PackRegistry(Path.Combine(tempRoot, "builtin"), projectPackRoot).ListPacks();

            var pack = result.Packs.Should().ContainSingle().Subject;
            pack.Id.Should().Be("sample-theme");
            pack.Role.Should().Be(ComposerPackRoles.Primary);
            pack.Required.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldReportMissingInstallManifestAsPackHealthDiagnostic()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            CreateMinimalPack(ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project")), "sample", "1.0.0");
            File.Delete(Path.Combine(
                ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project")),
                "sample",
                "1.0.0",
                "install.manifest.json"));

            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(tempRoot),
                ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project")));

            var result = registry.ListPacks();

            result.Packs.Should().BeEmpty();
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("install.manifest.json", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldIgnoreImportStagingDirectory()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectPackRoot = ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project"));
            CreateMinimalPack(Path.Combine(projectPackRoot, ".staging", "pending"), "sample", "1.0.0");

            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(tempRoot),
                projectPackRoot);

            var result = registry.ListPacks();

            result.Packs.Should().BeEmpty();
            result.Diagnostics.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldCapInvalidPackDiagnostics()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project"));
            CreateInvalidPackCandidates(packRoot, 100);

            var result = new PackRegistry(Path.Combine(tempRoot, "builtin"), packRoot).ListPacks();

            result.Packs.Should().BeEmpty();
            result.Diagnostics.Should().HaveCountLessThanOrEqualTo(64);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldRejectMoreThan256PackCandidates()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = ComposerPackPaths.ProjectLocalRoot(Path.Combine(tempRoot, "project"));
            CreateInvalidPackCandidates(packRoot, 257);
            var registry = new PackRegistry(Path.Combine(tempRoot, "builtin"), packRoot);

            var act = () => registry.ListPacks();

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*limit*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackRegistry_ShouldObserveCancellationBeforeDiscovery()
    {
        var tempRoot = CreateTempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            var registry = new PackRegistry(Path.Combine(tempRoot, "builtin"));

            var act = () => registry.ListPacks(cancellation.Token);

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ListUiBlockPacksTool_ShouldReturnBuiltInWpfUiPack()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.ListUiBlockPacks(
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("packCount").GetInt32().Should().BeGreaterOrEqualTo(1);
            payload.GetProperty("packs").EnumerateArray()
                .Should().Contain(pack => pack.GetProperty("id").GetString() == "wpfui"
                    && pack.GetProperty("version").GetString() == "0.1.0"
                    && pack.GetProperty("blockCount").GetInt32() == 18
                    && pack.GetProperty("kind").GetString() == "skill-generated-style-pack"
                    && pack.GetProperty("themeTokens").GetProperty("cornerRadius.control").GetString() == "8"
                    && pack.GetProperty("role").GetString() == ComposerPackRoles.Primary
                    && pack.GetProperty("required").GetBoolean());
            payload.GetProperty("allowedPackRoles").EnumerateArray()
                .Select(role => role.GetString()).Should()
                .Equal(ComposerPackRoles.All.Order(StringComparer.Ordinal));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static void CopyPackTo(string packRoot, bool enabled)
    {
        var destination = Path.Combine(packRoot, "wpfui", "0.1.0");
        CopyDirectory(GetRepoFilePath("packs/builtin/wpfui/0.1.0"), destination);
        File.WriteAllText(
            Path.Combine(destination, "install.manifest.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"wpfui","version":"0.1.0","scope":"project-local","path":"{{destination.Replace("\\", "\\\\")}}","enabled":{{enabled.ToString().ToLowerInvariant()}}}
            """);
    }

    private static void CreateInvalidPackCandidates(string packRoot, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var destination = Path.Combine(packRoot, $"pack-{index:D3}", "1.0.0");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "pack.json"), "{}");
        }
    }

    private static void CreateMinimalPack(
        string packRoot,
        string packId,
        string version,
        string kind = "control-pack")
    {
        var destination = Path.Combine(packRoot, packId, version);
        Directory.CreateDirectory(Path.Combine(destination, "blocks"));
        Directory.CreateDirectory(Path.Combine(destination, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(destination, "recipes"));
        Directory.CreateDirectory(Path.Combine(destination, "examples"));

        File.WriteAllText(
            Path.Combine(destination, "pack.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{packId}}","kind":"{{kind}}","displayName":"Sample Pack","version":"{{version}}","blocks":["{{packId}}.text"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(destination, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Sample","url":"https://example.invalid/sample","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}
            """);
        File.WriteAllText(
            Path.Combine(destination, "blocks", "text.block.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"{{packId}}.text","displayName":"Text","category":"text","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(destination, "renderers", "xaml", "text.xaml.sbn"), "<TextBlock />");
        File.WriteAllText(
            Path.Combine(destination, "install.manifest.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{packId}}","version":"{{version}}","scope":"project-local","path":"{{destination.Replace("\\", "\\\\")}}","enabled":true}
            """);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        process.Should().NotBeNull();
        process!.WaitForExit();
        process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
    }

    private static void DeleteDirectory(string path)
        => TestDirectory.Delete(path);

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
