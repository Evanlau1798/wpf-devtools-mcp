using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.Composer;

[CollectionDefinition("ComposerAcceptance", DisableParallelization = true)]
public sealed class ComposerAcceptanceCollection;

[Collection("ComposerAcceptance")]
[Trait("Category", "ComposerAcceptance")]
public sealed class ComposerThirdPartyAcceptanceTests
{
    [Fact]
    public async Task RealExtensionPacks_ShouldBuildMaterialConsumerRuntimeAndMahAppsCustomWindow()
    {
        string? materialRoot = null;
        string? mahAppsRoot = null;
        Process? consumer = null;
        try
        {
            materialRoot = ComposerRealPackFixture.CreateProject(
                "materialdesign",
                "5.3.2",
                "MaterialDesignConsumer",
                "<PackageReference Include=\"MaterialDesignThemes\" Version=\"5.3.2\" />");
            var materialApply = ApplyConfirmed(
                materialRoot,
                ComposerRealPackFixture.MaterialDesignBlueprint,
                "MainWindow.xaml");
            materialApply.RequiredNuGetPackages.Should().ContainSingle(package =>
                package.Id == "MaterialDesignThemes" && package.VersionRange == "[5.3.2]");
            materialApply.BehaviorIntegrationContract.Interactions.Should().ContainSingle(interaction =>
                interaction.CommandPath == "OpenWorkspaceCommand"
                && interaction.CommandParameter == "material-532");
            ComposerRealPackFixture.WriteApplication(
                materialRoot,
                "MaterialDesignConsumer",
                "MainWindow",
                materialApply.ResourcePlan,
                inspectorEnabled: true);
            await AssertBuildAsync(materialRoot);

            using var sensitiveReads = new EnvironmentVariableScope(
                McpServerConfiguration.AllowSensitiveReadsEnvVar,
                "true");
            using var liveSession = SecureLiveSession.Create("WpfDevTools_ComposerAcceptance");
            consumer = LaunchConsumer(materialRoot, "MaterialDesignConsumer");
            await WaitForWindowAsync(consumer);
            await InitializeInspectorAsync(consumer, materialRoot, liveSession);
            await AssertMaterialRuntimeAsync(consumer, liveSession.SessionManager);
            liveSession.SessionManager.RemoveSession(consumer.Id);
            LiveTestProcessCleanup.StopAndDispose(consumer);
            consumer = null;

            mahAppsRoot = ComposerRealPackFixture.CreateProject(
                "mahapps",
                "2.4.11",
                "MahAppsConsumer",
                "<PackageReference Include=\"MahApps.Metro\" Version=\"2.4.11\" />");
            var mahAppsApply = ApplyConfirmed(
                mahAppsRoot,
                ComposerRealPackFixture.MahAppsBlueprint,
                "OperationsWindow.xaml");
            mahAppsApply.Xaml.Should().Contain("<mah:MetroWindow");
            mahAppsApply.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.Action.Contains("MahApps.Metro.Controls.MetroWindow", StringComparison.Ordinal));
            ComposerRealPackFixture.WriteApplication(
                mahAppsRoot,
                "MahAppsConsumer",
                "OperationsWindow",
                mahAppsApply.ResourcePlan,
                inspectorEnabled: false);
            await AssertBuildAsync(mahAppsRoot);
        }
        finally
        {
            LiveTestProcessCleanup.StopAndDispose(consumer);
            DeleteProjectRoot(materialRoot);
            DeleteProjectRoot(mahAppsRoot);
        }
    }

    private static ApplyBlueprintResult ApplyConfirmed(string projectRoot, string blueprint, string targetPath)
    {
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(ReleasePackagingTestHarness.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));
        var validation = new BlueprintValidationService(registry).Validate(blueprint);
        validation.Success.Should().BeTrue(string.Join(Environment.NewLine,
            validation.Errors.Select(error => error.Message)));
        var render = new UiBlueprintRenderer(registry).Render(
            new RenderBlueprintRequest(blueprint, ProjectRoot: projectRoot));
        render.Success.Should().BeTrue(string.Join(Environment.NewLine,
            render.Errors.Select(error => error.Message)));

        using var allowedRoot = new EnvironmentVariableScope(
            McpServerConfiguration.AllowedProjectRootsEnvVar,
            projectRoot);
        using var projectWrites = new EnvironmentVariableScope(
            McpServerConfiguration.AllowProjectWritesEnvVar,
            "true");
        var apply = new UiBlueprintApplyService(registry).Apply(
            new ApplyBlueprintRequest(blueprint, projectRoot, targetPath, DryRun: false, ConfirmApply: true));
        apply.Success.Should().BeTrue(string.Join(Environment.NewLine,
            apply.Errors.Select(error => $"{error.Code}: {error.Message}")));
        apply.DryRun.Should().BeFalse();
        apply.WouldWriteFiles.Should().BeTrue();
        File.Exists(Path.Combine(projectRoot, targetPath)).Should().BeTrue();
        return apply;
    }

    private static async Task AssertBuildAsync(string projectRoot)
    {
        var restore = await ComposerRealPackFixture.RunDotNetAsync(
            projectRoot,
            "restore",
            noRestore: false,
            TimeSpan.FromMinutes(3));
        restore.ExitCode.Should().Be(0, restore.Output);
        var build = await ComposerRealPackFixture.RunDotNetAsync(
            projectRoot,
            "build",
            noRestore: true,
            TimeSpan.FromMinutes(3));
        build.ExitCode.Should().Be(0, build.Output);
    }

    private static Process LaunchConsumer(string projectRoot, string projectName)
    {
        var executable = Path.Combine(
            projectRoot,
            "bin",
            "Debug",
            "net8.0-windows",
            projectName + ".exe");
        File.Exists(executable).Should().BeTrue();
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("consumer process did not start");
    }

    private static Task WaitForWindowAsync(Process process)
        => ConditionWaiter.WaitForAsync(
            () => Task.FromResult(RefreshAndHasWindow(process)),
            ready => ready,
            TimeSpan.FromSeconds(30),
            "Material consumer did not expose a visible main window.");

    private static bool RefreshAndHasWindow(Process process)
    {
        process.Refresh();
        return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
    }

    private static async Task InitializeInspectorAsync(
        Process process,
        string projectRoot,
        SecureLiveSession liveSession)
    {
        var outputRoot = Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows");
        var secret = liveSession.SessionManager.GetAuthenticationSecretBase64(process.Id);
        secret.Should().NotBeNullOrWhiteSpace();
        await File.WriteAllLinesAsync(
            Path.Combine(outputRoot, "inspector-options.txt"),
            [secret!, liveSession.CertificateDirectoryForTesting]);
        var readyPath = Path.Combine(outputRoot, "inspector-ready.txt");
        await ConditionWaiter.WaitForAsync(
            () => Task.FromResult(File.Exists(readyPath)),
            ready => ready,
            TimeSpan.FromSeconds(30),
            "Material consumer Inspector SDK did not become ready.");
        var failure = await liveSession.SessionManager.ConnectExistingHostSessionAsync(
            process.Id,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        failure.Should().Be(NamedPipeConnectFailure.None);
    }

    private static async Task AssertMaterialRuntimeAsync(Process process, SessionManager sessionManager)
    {
        var processId = process.Id;
        var summary = await ReadSummaryAsync(sessionManager, processId);
        summary.GetRawText().Should().Contain("Material workspace").And.Contain("Ready");

        var find = JsonSerializer.SerializeToElement(await new FindElementsTool(sessionManager).ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                processId,
                query = "Open workspace",
                matchMode = "contains",
                maxResults = 20
            }),
            CancellationToken.None));
        find.GetProperty("success").GetBoolean().Should().BeTrue(find.GetRawText());
        var button = find.GetProperty("results").EnumerateArray()
            .First(result => result.GetProperty("elementType").GetString() == "Button");
        var buttonId = button.GetProperty("elementId").GetString();
        buttonId.Should().NotBeNullOrWhiteSpace();

        var readiness = JsonSerializer.SerializeToElement(await new GetInteractionReadinessTool(sessionManager).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { processId, elementId = buttonId }),
            CancellationToken.None));
        readiness.GetProperty("success").GetBoolean().Should().BeTrue(readiness.GetRawText());
        readiness.GetProperty("isReady").GetBoolean().Should().BeTrue(readiness.GetRawText());

        var click = JsonSerializer.SerializeToElement(await new ClickElementTool(sessionManager).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { processId, elementId = buttonId }),
            CancellationToken.None));
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());
        await ConditionWaiter.WaitForAsync(
            () => ReadSummaryAsync(sessionManager, processId),
            current => current.GetRawText().Contains("Workspace opened: material-532", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            "Command-bound Material action did not update the visible status.");
    }

    private static async Task<JsonElement> ReadSummaryAsync(SessionManager sessionManager, int processId)
        => JsonSerializer.SerializeToElement(await new GetUiSummaryTool(sessionManager).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { processId, depthMode = "semantic" }),
            CancellationToken.None));

    private static void DeleteProjectRoot(string? projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            ReleasePackagingTestHarness.DeleteDirectory(Path.GetDirectoryName(projectRoot)!);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        internal EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
