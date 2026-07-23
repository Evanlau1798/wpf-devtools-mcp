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

[CollectionDefinition("ComposerExtension", DisableParallelization = true)]
public sealed class ComposerExtensionCollection;

[Collection("ComposerExtension")]
public sealed class ComposerExtensionAcceptanceTests
{
    [Fact]
    public async Task SyntheticExtensionPack_ShouldBuildLaunchAndSupportMcpInteraction()
    {
        string? projectRoot = null;
        Process? consumer = null;
        try
        {
            projectRoot = ComposerSyntheticPackFixture.CreateProject();
            var apply = ApplyConfirmed(
                projectRoot,
                ComposerSyntheticPackFixture.Blueprint,
                "MainWindow.xaml");

            apply.RequiredNuGetPackages.Should().ContainSingle(package =>
                package.Id == "System.Threading.Channels" && package.VersionRange == "[8.0.0]");
            apply.BehaviorIntegrationContract.Interactions.Should().ContainSingle(interaction =>
                interaction.CommandPath == "RunExtensionCommand"
                && interaction.CommandParameter == "neutral-payload");
            apply.Xaml.Should().Contain("<synthetic:ExtensionWindow");
            apply.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.Action.Contains(
                    "SyntheticExtension.Controls.ExtensionWindow",
                    StringComparison.Ordinal));

            File.ReadAllText(Path.Combine(projectRoot, "App.xaml"))
                .Should().Contain("x:Key=\"ExistingBrush\"")
                .And.Contain("SyntheticTheme.xaml");
            File.ReadAllText(Path.Combine(projectRoot, "SyntheticConsumer.csproj"))
                .Should().Contain("PackageReference Include=\"System.Threading.Channels\"");
            File.ReadAllText(Path.Combine(projectRoot, "MainWindow.xaml.cs"))
                .Should().Contain("SyntheticExtension.Controls")
                .And.Contain(
                    "MainWindow : SyntheticExtension.Controls.ExtensionWindow");
            await AssertBuildAsync(projectRoot);

            var authSecret = CreateAuthSecret();
            var certificateDirectory = Path.Combine(projectRoot, ".mcp-certificates");
            WriteInspectorOptions(projectRoot, authSecret, certificateDirectory);
            consumer = LaunchConsumer(projectRoot);
            await WaitForWindowAsync(consumer);
            await WaitForInspectorAsync(projectRoot);
            await AssertRuntimeOverStdioAsync(
                consumer,
                projectRoot,
                authSecret,
                certificateDirectory);
        }
        finally
        {
            LiveTestProcessCleanup.StopAndDispose(consumer);
            DeleteProjectRoot(projectRoot);
        }
    }

    private static ApplyBlueprintResult ApplyConfirmed(
        string projectRoot,
        string blueprint,
        string targetPath)
    {
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(ReleasePackagingTestHarness.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));
        var validation = new BlueprintValidationService(registry).Validate(blueprint);
        validation.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            validation.Errors.Select(error => error.Message)));
        var render = new UiBlueprintRenderer(registry).Render(
            new RenderBlueprintRequest(blueprint, ProjectRoot: projectRoot));
        render.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            render.Errors.Select(error => error.Message)));

        using var allowedRoot = new EnvironmentVariableScope(
            McpServerConfiguration.AllowedProjectRootsEnvVar,
            projectRoot);
        using var projectWrites = new EnvironmentVariableScope(
            McpServerConfiguration.AllowProjectWritesEnvVar,
            "true");
        var applyService = new UiBlueprintApplyService(registry);
        var dryRun = applyService.Apply(
            new ApplyBlueprintRequest(
                blueprint,
                projectRoot,
                targetPath,
                DryRun: true));
        dryRun.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            dryRun.Errors
                .Concat(dryRun.ProjectIntegrationPlan.Errors)
                .Select(error => $"{error.Code}: {error.Message}")));
        dryRun.ProjectIntegrationPlan.Ready.Should().BeTrue(string.Join(
            Environment.NewLine,
            dryRun.ProjectIntegrationPlan.Errors.Select(
                error => $"{error.Code}: {error.Message}")));

        var integration = new UiBlueprintProjectIntegrationService(registry).Apply(
            new ProjectIntegrationRequest(
                blueprint,
                projectRoot,
                targetPath,
                dryRun.ProjectIntegrationPlan.PlanHash,
                ConfirmIntegration: true));
        integration.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            integration.Errors.Select(error => $"{error.Code}: {error.Message}")));
        integration.Applied.Should().BeTrue();

        var apply = applyService.Apply(
            new ApplyBlueprintRequest(
                blueprint,
                projectRoot,
                targetPath,
                DryRun: false,
                ConfirmApply: true));
        apply.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            apply.Errors.Select(error => $"{error.Code}: {error.Message}")));
        apply.DryRun.Should().BeFalse();
        apply.WouldWriteFiles.Should().BeTrue();
        File.Exists(Path.Combine(projectRoot, targetPath)).Should().BeTrue();
        return apply;
    }

    private static async Task AssertBuildAsync(string projectRoot)
    {
        var restore = await ComposerSyntheticPackFixture.RunDotNetAsync(
            projectRoot,
            "restore",
            noRestore: false,
            TimeSpan.FromMinutes(3));
        restore.ExitCode.Should().Be(0, restore.Output);
        var build = await ComposerSyntheticPackFixture.RunDotNetAsync(
            projectRoot,
            "build",
            noRestore: true,
            TimeSpan.FromMinutes(3));
        build.ExitCode.Should().Be(0, build.Output);
    }

    private static Process LaunchConsumer(string projectRoot)
    {
        var executable = GetConsumerExecutable(projectRoot);
        File.Exists(executable).Should().BeTrue();
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("consumer process did not start");
    }

    private static string GetConsumerExecutable(string projectRoot)
        => Path.Combine(
            projectRoot,
            "bin",
            "Debug",
            "net8.0-windows",
            "SyntheticConsumer.exe");

    private static Task WaitForWindowAsync(Process process)
        => ConditionWaiter.WaitForAsync(
            () => Task.FromResult(RefreshAndHasWindow(process)),
            ready => ready,
            TimeSpan.FromSeconds(30),
            "Synthetic extension consumer did not expose a visible main window.");

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
        var readyPath = Path.Combine(
            projectRoot,
            "bin",
            "Debug",
            "net8.0-windows",
            "inspector-ready.txt");
        return ConditionWaiter.WaitForAsync(
            () => Task.FromResult(File.Exists(readyPath)),
            ready => ready,
            TimeSpan.FromSeconds(30),
            "Synthetic extension consumer Inspector SDK did not become ready.");
    }

    private static async Task AssertRuntimeOverStdioAsync(
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
        serverExecutable.Should().NotBeNull(
            "the integration build must produce the actual MCP server executable");

        using var client = new McpStdioClient();
        var initialize = await client.StartAsync(
            serverExecutable!,
            McpE2eFixture.CreateServerEnvironment(
                GetConsumerExecutable(projectRoot),
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
        summary.GetRawText().Should().Contain("Extension workspace").And.Contain("Ready");

        var find = await client.CallToolAsync("find_elements", new
        {
            processId,
            query = "Run extension action",
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

        var click = await client.CallToolAsync(
            "click_element",
            new { processId, elementId = buttonId });
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());
        await ConditionWaiter.WaitForAsync(
            cancellationToken => ReadSummaryAsync(client, processId, cancellationToken),
            current => current.GetRawText().Contains(
                "Extension action completed: neutral-payload",
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            "Command-bound synthetic extension action did not update the visible status.");
    }

    private static Task<JsonElement> ReadSummaryAsync(
        McpStdioClient client,
        int processId,
        CancellationToken cancellationToken = default)
        => client.CallToolAsync(
            "get_ui_summary",
            new { processId, depthMode = "semantic" },
            ct: cancellationToken);

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
