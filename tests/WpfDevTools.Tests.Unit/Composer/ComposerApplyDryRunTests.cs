using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerApplyDryRunTests
{
    [Fact]
    public void ApplyBlueprint_ShouldReturnDryRunFilePlanWithoutWriting()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot));

            result.Success.Should().BeTrue();
            result.DryRun.Should().BeTrue();
            result.WouldWriteFiles.Should().BeFalse();
            result.FilePlan.Should().Contain(item => item.Role == "view"
                && item.TargetPath.EndsWith(Path.Combine("Views", "GeneratedView.xaml"), StringComparison.Ordinal)
                && item.WouldWrite == false);
            result.FilePlan.Should().Contain(item => item.Role == "viewmodel-binding-contract"
                && item.TargetPath.EndsWith("GeneratedView.Bindings.json", StringComparison.Ordinal)
                && item.WouldWrite == false);
            result.ResourcePlan.Should().Contain(resource => resource.Contains("Wpf.Ui.xaml", StringComparison.Ordinal));
            result.RequiredNuGetPackages.Should().Contain(package => package.Id == "WPF-UI");
            result.ViewModelBindingContract.TargetPath.Should().EndWith("GeneratedView.Bindings.json");
            File.Exists(Path.Combine(projectRoot, "Views", "GeneratedView.xaml")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectWriteWhenProjectWriteGateDisabled()
    {
        var tempRoot = CreateTempDirectory();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, null);
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, null);
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, DryRun: false));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectWritesDisabled");
            File.Exists(Path.Combine(projectRoot, "Views", "GeneratedView.xaml")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldNotReadExistingTargetBeforeWriteIsAuthorized()
    {
        var tempRoot = CreateTempDirectory();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, null);
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, null);
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var targetPath = Path.Combine(projectRoot, "Views", "GeneratedView.xaml");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, ExistingViewWithManualSlot());
            using var locked = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var dryRun = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath));
            var blockedWrite = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath, DryRun: false));

            dryRun.Success.Should().BeTrue();
            blockedWrite.Success.Should().BeFalse();
            blockedWrite.Errors.Should().Contain(error => error.Code == "ProjectWritesDisabled");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectTargetOutsideProjectRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var outsidePath = Path.Combine(tempRoot, "outside", "GeneratedView.xaml");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, outsidePath));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectPathOutsideRoot");
            File.Exists(outsidePath).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectMalformedProjectRootAllowlist()
    {
        var tempRoot = CreateTempDirectory();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, @"relative\project");
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, DryRun: false));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "InvalidProjectRootAllowlist");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldWriteInsideAllowedProjectRootAndPreserveSafeSlot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var targetPath = Path.Combine(projectRoot, "Views", "GeneratedView.xaml");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, ExistingViewWithManualSlot());
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath, DryRun: false));

            result.Success.Should().BeTrue();
            result.DryRun.Should().BeFalse();
            result.WouldWriteFiles.Should().BeTrue();
            result.FilePlan.Single(item => item.Role == "view").BackupPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(result.FilePlan.Single(item => item.Role == "view").BackupPath).Should().BeTrue();
            var written = File.ReadAllText(targetPath);
            written.Should().Contain("WPFDEVTOOLS_BLUEPRINT_SOURCE");
            written.Should().Contain("Manual note");
            written.Should().Contain("WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldWriteInsideAllowedProjectRootWhenTargetDirectoryIsMissing()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, DryRun: false));

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, "Views", "GeneratedView.xaml")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectReparsePointTargetParent()
    {
        var tempRoot = CreateTempDirectory();
        var viewsPath = Path.Combine(tempRoot, "project", "Views");
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var outsideRoot = Path.Combine(tempRoot, "outside");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(outsideRoot);
            CreateDirectoryJunction(viewsPath, outsideRoot);
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, DryRun: false));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectPathUsesReparsePoint");
            File.Exists(Path.Combine(outsideRoot, "GeneratedView.xaml")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(viewsPath))
            {
                Directory.Delete(viewsPath);
            }

            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ApplyUiBlueprintTool_ShouldReturnStructuredDryRun()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");

            var result = await UiComposerMcpTools.ApplyUiBlueprint(
                Blueprint(),
                projectRoot,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("dryRun").GetBoolean().Should().BeTrue();
            payload.GetProperty("filePlan")[0].GetProperty("wouldWrite").GetBoolean().Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "GeneratedView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button", "properties": { "text": "Apply" } }
            }
            """;

    private static string ExistingViewWithManualSlot()
        => """
            <Grid>
            <!-- WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content -->
            <TextBlock Text="Manual note" />
            <!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->
            </Grid>
            """;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
