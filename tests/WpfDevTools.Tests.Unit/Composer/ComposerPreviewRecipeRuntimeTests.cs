using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerPreviewRecipeRuntimeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PreviewBlueprintAsync_WhenShellRecipeExpandedWithRuntimeDiagnostics_ShouldLoadGeneratedView()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var session = SecurePreviewSession.Create();
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var recipe = new RecipeExpansionService(registry)
            .Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        recipe.Success.Should().BeTrue();
        var result = await new UiBlueprintPreviewService(registry, session.SessionManager).PreviewAsync(
            new PreviewBlueprintRequest(
                JsonSerializer.Serialize(recipe.Blueprint, JsonOptions),
                RestoreEnabled: true,
                StartHost: true,
                IncludeRuntimeDiagnostics: true),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.Status.Should().Be("loaded", result.BuildOutput);
        result.PreviewHost.RuntimeDiagnostics.Should().NotBeNull();
        var diagnostics = result.PreviewHost.RuntimeDiagnostics!;
        diagnostics.Should().Contain(diagnostic => diagnostic.Tool == "connect" && diagnostic.Success);
        var summary = GetDiagnosticPayload(diagnostics, "get_ui_summary");

        summary.GetProperty("semanticNodeCount").GetInt32().Should().BeGreaterThanOrEqualTo(6);
        AssertPayloadContains(summary, "Home");
        AssertPayloadContains(summary, "Items");
        AssertPayloadContains(summary, "Settings");
        AssertPayloadContains(summary, "NavigationView");
        AssertPayloadContains(summary, "WPF UI NavigationView.");
        AssertPayloadContains(summary, "All Controls");
        AssertPayloadContains(summary, "Pane Header");
        AssertPayloadContains(summary, "IsBackButtonVisible");
        AssertPayloadContains(summary, "WPF UI Fluent NavigationView.");
        GetDiagnosticPayload(diagnostics, "get_layout_info").ValueKind.Should().Be(JsonValueKind.Object);
    }

    private static JsonElement GetDiagnosticPayload(IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics, string tool)
        => diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Tool == tool && diagnostic.Success)
            .Subject
            .Payload;

    private static void AssertPayloadContains(JsonElement payload, string expected)
        => payload.GetRawText().Should().Contain(expected);

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
            var secretBytes = RandomNumberGenerator.GetBytes(32);
            var secret = Convert.ToBase64String(secretBytes);
            CryptographicOperations.ZeroMemory(secretBytes);
            var authManager = new AuthenticationManager(() => secret);
            var certificateDirectory = Path.Combine(
                TestRepositoryPaths.GetRepoFilePath("tmp"),
                "preview-recipe-certs-" + Guid.NewGuid().ToString("N"));
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
