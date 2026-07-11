using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpResources;
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
        using var screenshots = new EnvironmentVariableScope(McpServerConfiguration.AllowScreenshotsEnvVar, "true");
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
                IncludeRuntimeDiagnostics: true,
                IncludeScreenshotDiagnostics: true,
                ScreenshotOutputMode: "file"),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.PreviewHost.Status.Should().Be("loaded", result.BuildOutput);
        result.PreviewHost.RuntimeDiagnostics.Should().NotBeNull();
        var diagnostics = result.PreviewHost.RuntimeDiagnostics!;
        diagnostics.Should().Contain(diagnostic => diagnostic.Tool == "connect" && diagnostic.Success);
        var summary = GetDiagnosticPayload(diagnostics, "get_ui_summary");

        summary.GetProperty("semanticNodeCount").GetInt32().Should().BeGreaterThanOrEqualTo(6);
        AssertPayloadContains(summary, "Home");
        AssertPayloadContains(summary, "Workspace");
        AssertPayloadContains(summary, "Activity");
        AssertPayloadContains(summary, "Reports");
        AssertPayloadContains(summary, "Settings");
        AssertPayloadContains(summary, "Overview");
        AssertPayloadContains(summary, "Open workspace");
        GetDiagnosticPayload(diagnostics, "get_layout_info").ValueKind.Should().Be(JsonValueKind.Object);
        var screenshot = GetDiagnosticPayload(diagnostics, "element_screenshot");
        screenshot.GetProperty("outputMode").GetString().Should().Be("file");
        var screenshotId = screenshot.GetProperty("screenshotId").GetString();
        var resourceRead = screenshot.GetProperty("resourceRead");
        resourceRead.GetProperty("method").GetString().Should().Be("resources/read");
        resourceRead.GetProperty("params").GetProperty("uri").GetString()
            .Should().Be($"wpf://screenshots/{screenshotId}");
        resourceRead.GetProperty("sameSessionRequired").GetBoolean().Should().BeTrue();
        var resource = ScreenshotResources.GetScreenshotPng(session.SessionManager, screenshotId!);
        var whitePixelRatio = GetWhitePixelRatio(resource.Should().BeOfType<BlobResourceContents>().Subject.DecodedData.ToArray());
        whitePixelRatio.Should().BeLessThan(0.03, "the structural preview should not expose unstyled white title and navigation regions");
    }

    private static JsonElement GetDiagnosticPayload(IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics, string tool)
        => diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Tool == tool && diagnostic.Success)
            .Subject
            .Payload;

    private static void AssertPayloadContains(JsonElement payload, string expected)
        => payload.GetRawText().Should().Contain(expected);

    private static double GetWhitePixelRatio(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var whitePixels = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] >= 245 && pixels[index + 1] >= 245 && pixels[index + 2] >= 245)
            {
                whitePixels++;
            }
        }

        return (double)whitePixels / (pixels.Length / 4);
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
