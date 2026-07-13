using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Integration.E2E;
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

            var authSecret = CreateAuthSecret();
            var certificateDirectory = Path.Combine(materialRoot, ".mcp-certificates");
            WriteInspectorOptions(materialRoot, authSecret, certificateDirectory);
            consumer = LaunchConsumer(materialRoot, "MaterialDesignConsumer");
            await WaitForWindowAsync(consumer);
            await WaitForInspectorAsync(materialRoot);
            await AssertMaterialRuntimeOverStdioAsync(
                consumer,
                materialRoot,
                authSecret,
                certificateDirectory);
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
        var executable = GetConsumerExecutable(projectRoot, projectName);
        File.Exists(executable).Should().BeTrue();
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("consumer process did not start");
    }

    private static string GetConsumerExecutable(string projectRoot, string projectName)
        => Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows", projectName + ".exe");

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

    private static string CreateAuthSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static void WriteInspectorOptions(
        string projectRoot,
        string authSecret,
        string certificateDirectory)
    {
        var outputRoot = Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows");
        Directory.CreateDirectory(certificateDirectory);
        File.WriteAllLines(
            Path.Combine(outputRoot, "inspector-options.txt"),
            [authSecret, certificateDirectory]);
    }

    private static Task WaitForInspectorAsync(string projectRoot)
    {
        var outputRoot = Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows");
        var readyPath = Path.Combine(outputRoot, "inspector-ready.txt");
        return ConditionWaiter.WaitForAsync(
            () => Task.FromResult(File.Exists(readyPath)),
            ready => ready,
            TimeSpan.FromSeconds(30),
            "Material consumer Inspector SDK did not become ready.");
    }

    private static async Task AssertMaterialRuntimeOverStdioAsync(
        Process process,
        string projectRoot,
        string authSecret,
        string certificateDirectory)
    {
        var serverExecutable = IntegrationExecutableLocator.FindExecutable(
            AppContext.BaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");
        serverExecutable.Should().NotBeNull("the integration build must produce the actual MCP server executable");

        using var client = new McpStdioClient();
        var initialize = await client.StartAsync(
            serverExecutable!,
            McpE2eFixture.CreateServerEnvironment(
                GetConsumerExecutable(projectRoot, "MaterialDesignConsumer"),
                authSecret,
                certificateDirectory));
        initialize.TryGetProperty("result", out _).Should().BeTrue(initialize.GetRawText());

        var toolsList = await client.ListToolsAsync();
        var tools = toolsList.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var toolNames = tools.Select(tool => tool.GetProperty("name").GetString()).ToArray();
        toolNames.Should().OnlyHaveUniqueItems().And.Contain(
            ["connect", "get_ui_summary", "find_elements", "get_interaction_readiness", "click_element"]);

        var processId = process.Id;
        var connect = await client.CallToolAsync("connect", new { processId }, timeoutMs: 90000);
        connect.GetProperty("success").GetBoolean().Should().BeTrue(connect.GetRawText());

        var summary = await ReadSummaryAsync(client, processId);
        summary.GetRawText().Should().Contain("Material workspace").And.Contain("Ready");

        var find = await client.CallToolAsync("find_elements", new
        {
            processId,
            query = "Open workspace",
            matchMode = "contains",
            maxResults = 20
        });
        find.GetProperty("success").GetBoolean().Should().BeTrue(find.GetRawText());
        var button = find.GetProperty("results").EnumerateArray()
            .First(result => result.GetProperty("elementType").GetString() == "Button");
        var buttonId = button.GetProperty("elementId").GetString();
        buttonId.Should().NotBeNullOrWhiteSpace();

        var readiness = await client.CallToolAsync(
            "get_interaction_readiness",
            new { processId, elementId = buttonId });
        readiness.GetProperty("success").GetBoolean().Should().BeTrue(readiness.GetRawText());
        readiness.GetProperty("isReady").GetBoolean().Should().BeTrue(readiness.GetRawText());

        var click = await client.CallToolAsync("click_element", new { processId, elementId = buttonId });
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());
        await ConditionWaiter.WaitForAsync(
            () => ReadSummaryAsync(client, processId),
            current => current.GetRawText().Contains("Workspace opened: material-532", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            "Command-bound Material action did not update the visible status.");
    }

    private static Task<JsonElement> ReadSummaryAsync(McpStdioClient client, int processId)
        => client.CallToolAsync("get_ui_summary", new { processId, depthMode = "semantic" });

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
