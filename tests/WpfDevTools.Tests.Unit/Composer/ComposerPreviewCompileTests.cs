using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed partial class ComposerPreviewCompileTests
{
    [Theory]
    [MemberData(nameof(CompilableBlueprints))]
    public void PreviewBlueprint_ShouldCompileGoldenBlueprint(string blueprintJson, string expectedConclusion)
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = service.Preview(new PreviewBlueprintRequest(blueprintJson, RestoreEnabled: true));

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.RestoreEnabled.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == expectedConclusion);
        result.PreviewHost.Status.Should().Be("compiled");
    }

    [Fact]
    public void PreviewBlueprint_ShouldCaptureRestoreDisabledBuildFailure()
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = service.Preview(new PreviewBlueprintRequest(ButtonBlueprint(), RestoreEnabled: false));

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeFalse();
        result.RestoreEnabled.Should().BeFalse();
        result.BuildOutput.Should().Contain("project.assets.json");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
            && diagnostic.JsonPath == "$.layout"
            && diagnostic.RendererTemplatePath.EndsWith("button.xaml.sbn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenCancelled_ShouldReturnDiagnosticAndDeleteTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-cancel-" + Guid.NewGuid().ToString("N"));
        var service = new UiBlueprintPreviewService(CreateRegistry());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(ButtonBlueprint(), TemporaryRoot: tempRoot),
            cancellation.Token);

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeFalse();
        result.BuildOutput.Should().Contain("cancelled");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "PreviewCancelled");
        result.PreviewHost.Status.Should().Be("cancelled");
        Directory.Exists(tempRoot).Should().BeFalse();
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldReturnStructuredCompileResult()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            ButtonBlueprint(),
            restoreEnabled: true,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("buildSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("previewHost").GetProperty("status").GetString().Should().Be("compiled");
        payload.GetProperty("previewHost").GetProperty("viewLoaded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_WhenRuntimeDiagnosticsRequestedForNavigationShell_ShouldLoadGeneratedView()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var session = SecurePreviewSession.Create();
        using var timeout = CreateTimeout();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            session.SessionManager,
            NavigationShellBlueprint(),
            restoreEnabled: true,
            startHost: true,
            includeRuntimeDiagnostics: true,
            cancellationToken: timeout.Token);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("buildSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("previewHost").GetProperty("status").GetString().Should().Be("loaded");
        payload.GetProperty("previewHost").GetProperty("runtimeDiagnostics").EnumerateArray()
            .Should().Contain(diagnostic =>
                diagnostic.GetProperty("tool").GetString() == "connect"
                && diagnostic.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenStartHostIsTrue_ShouldLoadGeneratedView()
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());
        using var timeout = CreateTimeout();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(ButtonBlueprint(), RestoreEnabled: true, StartHost: true),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.Status.Should().Be("loaded");
        result.PreviewHost.Started.Should().BeTrue();
        result.PreviewHost.ViewLoaded.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "PreviewHostViewLoaded");
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenRuntimeDiagnosticsNotRequested_ShouldNotGenerateInspectorSdkDependency()
    {
        var tempRoot = Path.Combine(
            TestRepositoryPaths.GetRepoFilePath("tmp"),
            "preview-no-sdk-" + Guid.NewGuid().ToString("N"));
        var service = new UiBlueprintPreviewService(CreateRegistry());
        using var timeout = CreateTimeout();

        try
        {
            var result = await service.PreviewAsync(
                new PreviewBlueprintRequest(
                    ButtonBlueprint(),
                    RestoreEnabled: true,
                    KeepArtifacts: true,
                    TemporaryRoot: tempRoot),
                timeout.Token);

            result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
            File.ReadAllText(Path.Combine(tempRoot, "PreviewHost.csproj"))
                .Should().NotContain("WpfDevTools.Inspector.Sdk");
            File.ReadAllText(Path.Combine(tempRoot, "MainWindow.xaml.cs"))
                .Should().NotContain("WpfDevTools.Inspector.Sdk");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Fact]
    public void PreviewBlueprintRequest_WhenScreenshotDiagnosticsRequested_ShouldRequireRuntimeDiagnostics()
    {
        var request = new PreviewBlueprintRequest(
            ButtonBlueprint(),
            IncludeScreenshotDiagnostics: true);

        UiBlueprintPreviewService.RequiresRuntimeDiagnostics(request).Should().BeTrue();
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenRuntimeAndScreenshotDiagnosticsRequested_ShouldCaptureRuntimeAndPolicyBlock()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var screenshots = new EnvironmentVariableScope(McpServerConfiguration.AllowScreenshotsEnvVar, null);
        using var session = SecurePreviewSession.Create();
        var service = new UiBlueprintPreviewService(CreateRegistry(), session.SessionManager);
        using var timeout = CreateTimeout();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(
                ButtonBlueprint(),
                RestoreEnabled: true,
                StartHost: true,
                IncludeRuntimeDiagnostics: true,
                IncludeScreenshotDiagnostics: true),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic => diagnostic.Tool == "connect" && diagnostic.Success);
        var summary = result.PreviewHost.RuntimeDiagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Tool == "get_ui_summary" && diagnostic.Success)
            .Subject.Payload;
        summary.GetProperty("depthMode").GetString().Should().Be("semantic");
        result.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic => diagnostic.Tool == "get_layout_info" && diagnostic.Success);
        var screenshot = result.PreviewHost.RuntimeDiagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Tool == "element_screenshot")
            .Subject;
        screenshot.Success.Should().BeFalse();
        screenshot.Payload.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        screenshot.Payload.GetProperty("hint").GetString().Should().Contain(McpServerConfiguration.AllowScreenshotsEnvVar);
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenRuntimeDiagnosticsRequestedForNavigationShell_ShouldLoadGeneratedView()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var session = SecurePreviewSession.Create();
        var service = new UiBlueprintPreviewService(CreateRegistry(), session.SessionManager);
        using var timeout = CreateTimeout();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(
                NavigationShellBlueprint(),
                RestoreEnabled: true,
                StartHost: true,
                IncludeRuntimeDiagnostics: true),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.Status.Should().Be("loaded", result.BuildOutput);
        result.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic => diagnostic.Tool == "connect" && diagnostic.Success);
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenRuntimeDiagnosticsKeepArtifacts_ShouldDeleteSdkOptionsFile()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var screenshots = new EnvironmentVariableScope(McpServerConfiguration.AllowScreenshotsEnvVar, null);
        using var session = SecurePreviewSession.Create();
        var tempRoot = Path.Combine(
            TestRepositoryPaths.GetRepoFilePath("tmp"),
            "preview-sdk-options-" + Guid.NewGuid().ToString("N"));
        var service = new UiBlueprintPreviewService(CreateRegistry(), session.SessionManager);
        using var timeout = CreateTimeout();

        try
        {
            var result = await service.PreviewAsync(
                new PreviewBlueprintRequest(
                    ButtonBlueprint(),
                    RestoreEnabled: true,
                    KeepArtifacts: true,
                    TemporaryRoot: tempRoot,
                    StartHost: true,
                    IncludeRuntimeDiagnostics: true),
                timeout.Token);

            result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
            result.PreviewHost.RuntimeDiagnostics.Should()
                .Contain(diagnostic => diagnostic.Tool == "connect" && diagnostic.Success);
            File.ReadAllText(Path.Combine(tempRoot, "MainWindow.xaml.cs"))
                .Should().Contain("DeleteFileBestEffort(optionsPath)");
            File.Exists(Path.Combine(
                    tempRoot,
                    "bin",
                    "Debug",
                    "net8.0-windows",
                    "preview-host-sdk.txt"))
                .Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    public static TheoryData<string, string> CompilableBlueprints()
        => new()
        {
            { ButtonBlueprint(), "ButtonIconPropertyElementValid" },
            { NavigationShellBlueprint(), "PreviewXamlCompiled" },
            { DataGridBlueprint(), "DataGridColumnsPropertyElementValid" },
            { DashboardCardBlueprint(), "PreviewXamlCompiled" },
            { ContentDialogBlueprint(), "PreviewXamlCompiled" },
            { SnackbarBlueprint(), "PreviewXamlCompiled" },
            { TabbedSettingsBlueprint(), "PreviewXamlCompiled" }
        };

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string ButtonBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.button",
              "properties": { "text": "Save" },
              "slots": { "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Save24" } }] }
            }
            """);

    private static string DataGridBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.dataGrid",
              "properties": { "itemsSource": "{Binding Rows}" },
              "slots": { "columns": [{ "kind": "template" }] }
            }
            """);

    private static string NavigationShellBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.fluentWindow",
              "properties": { "title": "Composer" },
              "slots": {
                "titleBar": [{ "kind": "wpfui.titleBar", "properties": { "title": "Composer" } }],
                "content": [{
                  "kind": "wpfui.navigationView",
                  "slots": {
                    "items": [{
                      "kind": "wpfui.navigationViewItem",
                      "slots": {
                        "content": [{ "kind": "text", "properties": { "value": "Home" } }],
                        "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Home24" } }]
                      }
                    }],
                    "content": [{ "kind": "wpfui.card" }]
                  }
                }]
              }
            }
            """);

    private static string DashboardCardBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.card",
              "slots": {
                "header": [{ "kind": "text", "properties": { "value": "Dashboard" } }],
                "content": [{ "kind": "text", "properties": { "value": "Compiled preview" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Refresh" } }]
              }
            }
            """);

    private static string ContentDialogBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.contentDialog",
              "properties": { "title": "Confirm" },
              "slots": {
                "content": [{ "kind": "text", "properties": { "value": "Continue?" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Continue" } }]
              }
            }
            """);

    private static string SnackbarBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.snackbar",
              "properties": { "timeout": 4000 },
              "slots": {
                "content": [{ "kind": "text", "properties": { "value": "Saved" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Undo" } }]
              }
            }
            """);

    private static string TabbedSettingsBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.tabView",
              "slots": {
                "items": [
                  {
                    "kind": "wpfui.tabViewItem",
                    "slots": {
                      "header": [{ "kind": "text", "properties": { "value": "General" } }],
                      "content": [{ "kind": "wpfui.card" }]
                    }
                  },
                  {
                    "kind": "wpfui.tabViewItem",
                    "slots": {
                      "header": [{ "kind": "text", "properties": { "value": "Security" } }],
                      "content": [{ "kind": "wpfui.card" }]
                    }
                  }
                ]
              }
            }
            """);

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PreviewView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;

    private static CancellationTokenSource CreateTimeout()
        => new(TimeSpan.FromSeconds(180));

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
            => Environment.SetEnvironmentVariable(_name, _originalValue);
    }

    private sealed class SecurePreviewSession : IDisposable
    {
        private readonly AuthenticationManager _authenticationManager;
        private readonly string _certificateDirectory;

        private SecurePreviewSession(
            AuthenticationManager authenticationManager,
            CertificateManager certificateManager,
            string certificateDirectory)
        {
            _authenticationManager = authenticationManager;
            _certificateDirectory = certificateDirectory;
            SessionManager = new SessionManager(authManager: authenticationManager, certManager: certificateManager);
        }

        public SessionManager SessionManager { get; }

        public static SecurePreviewSession Create()
        {
            var secret = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
            var authManager = new AuthenticationManager(() => secret);
            var certificateDirectory = Path.Combine(
                TestRepositoryPaths.GetRepoFilePath("tmp"),
                "preview-diagnostics-certs-" + Guid.NewGuid().ToString("N"));
            return new SecurePreviewSession(
                authManager,
                new CertificateManager(certificateDirectory),
                certificateDirectory);
        }

        public void Dispose()
        {
            SessionManager.Dispose();
            _authenticationManager.Dispose();
            try
            {
                if (Directory.Exists(_certificateDirectory))
                {
                    Directory.Delete(_certificateDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
