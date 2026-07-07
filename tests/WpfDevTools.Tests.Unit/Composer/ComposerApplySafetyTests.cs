using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerApplySafetyTests
{
    [Fact]
    public void ApplyBlueprint_ShouldRequireExplicitConfirmationForNonDryRun()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(
                Blueprint(),
                projectRoot,
                @"Views\GeneratedView.xaml",
                DryRun: false));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ApplyConfirmationRequired");
            File.Exists(Path.Combine(projectRoot, "Views", "GeneratedView.xaml")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_DryRunPlanShouldRequireConfirmationAndClassifyRisk()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot));

            result.Success.Should().BeTrue();
            result.RequiresConfirmation.Should().BeTrue();
            var viewPlan = result.FilePlan.Single(item => item.Role == "view");
            viewPlan.Action.Should().Be("create");
            viewPlan.RiskLevel.Should().Be("low");
            viewPlan.TargetPath.Should().EndWith(Path.Combine("Views", "GeneratedView.xaml"));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData(@"..\outside.xaml", "ProjectPathOutsideRoot")]
    [InlineData(@".git\config.xaml", "ProtectedProjectPath")]
    [InlineData(@"Sample.csproj", "ProjectFilePolicyViolation")]
    [InlineData(@"App.xaml", "ProjectFilePolicyViolation")]
    [InlineData(@"ViewModels\GeneratedView.xaml", "ProjectFilePolicyViolation")]
    public void ApplyBlueprint_ShouldRejectMaliciousOrProjectFileTargets(string targetPath, string expectedCode)
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == expectedCode);
            Directory.Exists(projectRoot).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectAbsoluteTargetPathEvenInsideProjectRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var targetPath = Path.Combine(projectRoot, "Views", "GeneratedView.xaml");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "AbsoluteTargetPathBlocked");
            File.Exists(targetPath).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectSystemProjectRoot()
    {
        var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsRoot))
        {
            return;
        }

        var service = new UiBlueprintApplyService(CreateRegistry());

        var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), windowsRoot));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Code == "ProjectRootIsSystemDirectory");
    }

    [Fact]
    public void ApplyBlueprint_ShouldReturnFailureAndKeepExistingFileWhenWriteFails()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var targetPath = Path.Combine(projectRoot, "Views", "GeneratedView.xaml");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var existing = ExistingViewWithManualSlot();
            File.WriteAllText(targetPath, existing);
            using var locked = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(
                Blueprint(),
                projectRoot,
                @"Views\GeneratedView.xaml",
                DryRun: false,
                ConfirmApply: true));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectWriteFailed");
            locked.Dispose();
            File.ReadAllText(targetPath).Should().Be(existing);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectBackupDirectoryReparsePoint()
    {
        var tempRoot = CreateTempDirectory();
        var backupRoot = Path.Combine(tempRoot, "project", ".wpfdevtools-backups");
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var outsideRoot = Path.Combine(tempRoot, "outside-backups");
            var targetPath = Path.Combine(projectRoot, "Views", "GeneratedView.xaml");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            Directory.CreateDirectory(outsideRoot);
            File.WriteAllText(targetPath, ExistingViewWithManualSlot());
            CreateDirectoryJunction(backupRoot, outsideRoot);
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(
                Blueprint(),
                projectRoot,
                @"Views\GeneratedView.xaml",
                DryRun: false,
                ConfirmApply: true));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectBackupPathUsesReparsePoint");
            Directory.EnumerateFileSystemEntries(outsideRoot).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot);
            }

            DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData(@".wpfdevtools\composer.json")]
    [InlineData(@"Directory.Build.props")]
    [InlineData(@"NuGet.config")]
    [InlineData(@"README.md")]
    [InlineData(@".gitignore")]
    [InlineData(@"Views\GeneratedView.txt")]
    public void ApplyBlueprint_ShouldRejectNonGeneratedViewTargets(string targetPath)
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(Blueprint(), projectRoot, targetPath));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectFilePolicyViolation");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldReturnFailureWhenTargetDirectoryCannotBeCreated()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(Path.Combine(projectRoot, "Views"), "not a directory");
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, projectRoot);
            var service = new UiBlueprintApplyService(CreateRegistry());

            var result = service.Apply(new ApplyBlueprintRequest(
                Blueprint(),
                projectRoot,
                @"Views\GeneratedView.xaml",
                DryRun: false,
                ConfirmApply: true));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectWriteFailed");
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
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-apply-safety-" + Guid.NewGuid().ToString("N"));
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
