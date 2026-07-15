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
    [Fact]
    [Trait("Category", "ComposerCompile")]
    public void PreviewBlueprint_ShouldCompileCompositeGoldenBlueprint()
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = service.Preview(new PreviewBlueprintRequest(CompositeCompileBlueprint(), RestoreEnabled: true));

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.BuildOutput.Should().Contain("Build succeeded.");
        result.RestoreEnabled.Should().BeTrue();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("PreviewXamlCompiled");
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
        result.VisualFidelity.Should().Be("not-available");
        Directory.Exists(tempRoot).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "ComposerCompile")]
    public async Task PreviewUiBlueprintTool_ShouldReturnStructuredCompileResult()
    {
        using var sessionManager = new SessionManager();
        var draft = await UiComposerMcpTools.CreateUiBlueprintDraft(
            ButtonBlueprint(),
            CancellationToken.None);
        var draftRef = draft.StructuredContent!.Value.GetProperty("draftRef").GetString()!;

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            draftRef,
            restoreEnabled: true,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("blueprintDraftRef").GetString().Should().Be(draftRef);
        payload.GetProperty("buildSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("visualFidelity").GetString().Should().Be("resource-backed");
        payload.GetProperty("visualValidationGuidance").GetString().Should()
            .Contain("approved runtime packages and resources")
            .And.Contain("host was not started")
            .And.Contain("final application");
        var screenshotGuidance = payload.GetProperty("screenshotVerificationGuidance").GetString();
        screenshotGuidance.Should().Contain("same screenshot resource");
        screenshotGuidance.Should().Contain("SHA-256");
        var visualComparisonChecklist = payload.GetProperty("visualComparisonChecklist")
            .EnumerateArray()
            .ToArray();
        visualComparisonChecklist.Select(item => item.GetProperty("area").GetString()).Should()
            .Equal("windowChrome", "icons", "controlTemplates", "layoutAndSpacing");
        foreach (var item in visualComparisonChecklist)
        {
            item.GetProperty("preview").GetString().Should().NotBeNullOrWhiteSpace();
            item.GetProperty("finalApp").GetString().Should().NotBeNullOrWhiteSpace();
            item.GetProperty("requiredAction").GetString().Should().Contain("final");
        }
        payload.GetProperty("previewHost").GetProperty("status").GetString().Should().Be("compiled");
        payload.GetProperty("previewHost").GetProperty("viewLoaded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "ComposerRuntime")]
    public async Task PreviewBlueprintAsync_WhenCompositeControlsStartHostWithoutWindowsEnvironment_ShouldLoadGeneratedView()
    {
        using var windowsDirectory = new EnvironmentVariableScope("WINDIR", null);
        using var systemRoot = new EnvironmentVariableScope("SystemRoot", null);
        var service = new UiBlueprintPreviewService(CreateRegistry());
        using var timeout = CreateTimeout();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(CompositeRuntimeBlueprint(), RestoreEnabled: true, StartHost: true),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.Status.Should().Be("loaded", result.BuildOutput);
        result.PreviewHost.Started.Should().BeTrue();
        result.PreviewHost.ViewLoaded.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "PreviewHostViewLoaded");
    }

    [Fact]
    [Trait("Category", "ComposerCompile")]
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
    [Trait("Category", "ComposerRuntime")]
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
            result.PreviewHost.RuntimeDiagnostics.Should()
                .Contain(diagnostic => diagnostic.Tool == "get_clipping_info" && diagnostic.Success);
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

    private static string CompositeCompileBlueprint()
        => Blueprint("""
            {
              "kind": "core.stack",
              "slots": { "children": [
                {
                  "kind": "wpfui.button",
                  "properties": { "text": "Save" },
                  "slots": { "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Save24" } }] }
                },
                {
                  "kind": "wpfui.dataGrid",
                  "properties": { "itemsSource": "{Binding Rows}" },
                  "slots": { "columns": [{ "kind": "core.template" }] }
                },
                {
                  "kind": "wpfui.card",
                  "slots": {
                    "header": [{ "kind": "core.text", "properties": { "text": "Dashboard" } }],
                    "content": [{ "kind": "core.text", "properties": { "text": "Compiled preview" } }],
                    "actions": [{ "kind": "wpfui.button", "properties": { "text": "Refresh" } }]
                  }
                }
              ] }
            }
            """);

    private static string CompositeRuntimeBlueprint()
        => Blueprint("""
            {
              "kind": "core.stack",
              "slots": { "children": [
                { "kind": "wpfui.button", "properties": { "text": "Save" } },
                {
                  "kind": "wpfui.tabView",
                  "slots": { "items": [
                    {
                      "kind": "wpfui.tabViewItem",
                      "slots": {
                        "header": [{ "kind": "core.text", "properties": { "text": "General" } }],
                        "content": [{ "kind": "wpfui.card" }]
                      }
                    },
                    {
                      "kind": "wpfui.tabViewItem",
                      "slots": {
                        "header": [{ "kind": "core.text", "properties": { "text": "Security" } }],
                        "content": [{ "kind": "wpfui.card" }]
                      }
                    }
                  ] }
                }
              ] }
            }
            """);

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PreviewView",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
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
