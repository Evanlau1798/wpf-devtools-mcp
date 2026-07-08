using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerObservabilityTests
{
    [Fact]
    public async Task ValidateUiBlueprintTool_ShouldReturnSanitizedDiagnosticsLogsAndMetrics()
    {
        var tempRoot = CreateTempDirectory();
        var secret = "sk_test_composer_secret";
        try
        {
            var result = await UiComposerMcpTools.ValidateUiBlueprint(
                BlueprintWithLayout("""{ "kind": "button", "properties": { "token": "sk_test_composer_secret" } }"""),
                projectRoot: Path.Combine(tempRoot, "project"),
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("valid").GetBoolean().Should().BeFalse();
            payload.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("UnqualifiedBlockKind");
            payload.GetProperty("errors")[0].GetProperty("repairSuggestion").GetString()
                .Should().NotBeNullOrWhiteSpace();

            var observability = payload.GetProperty("observability");
            var validationLog = observability.GetProperty("logs").EnumerateArray().Should().ContainSingle(log =>
                log.GetProperty("eventName").GetString() == "blueprint_validation_log"
                && log.GetProperty("outcome").GetString() == "failed").Subject;
            validationLog.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            validationLog.GetProperty("remediation").GetString().Should().NotBeNullOrWhiteSpace();
            validationLog.GetProperty("blueprintPath").GetString().Should().Be("$.layout");
            observability.GetProperty("metrics").GetProperty("blueprintValidationFailureRate").GetDouble()
                .Should().Be(1);
            observability.GetProperty("metrics").GetProperty("topDiagnosticCodes")[0].GetProperty("code").GetString()
                .Should().Be("UnqualifiedBlockKind");
            observability.GetProperty("privacy").GetProperty("telemetryEnabled").GetBoolean().Should().BeFalse();
            observability.GetProperty("privacy").GetProperty("disableTelemetrySetting").GetString()
                .Should().Be("WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true");

            var serialized = JsonSerializer.Serialize(observability);
            serialized.Should().NotContain(secret);
            serialized.Should().NotContain(tempRoot);
            serialized.Should().NotContain("\"blueprintJson\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ApplyUiBlueprintTool_WhenWritesAreBlocked_ShouldReportSanitizedSecurityRejection()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var result = await UiComposerMcpTools.ApplyUiBlueprint(
                BlueprintWithLayout("""{ "kind": "wpfui.button", "properties": { "text": "Apply" } }"""),
                projectRoot,
                dryRun: false,
                confirmApply: true,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeFalse();
            payload.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("ProjectWritesDisabled");

            var observability = payload.GetProperty("observability");
            var rejectionLog = observability.GetProperty("logs").EnumerateArray().Should().ContainSingle(log =>
                log.GetProperty("eventName").GetString() == "security_rejection_log"
                && log.GetProperty("code").GetString() == "ProjectWritesDisabled").Subject;
            rejectionLog.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            rejectionLog.GetProperty("remediation").GetString().Should().NotBeNullOrWhiteSpace();
            observability.GetProperty("metrics").GetProperty("rollbackRate").GetDouble().Should().Be(0);

            var serialized = JsonSerializer.Serialize(observability);
            serialized.Should().NotContain(projectRoot);
            serialized.Should().NotContain(tempRoot);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ListUiBlockPacksTool_ShouldSanitizeRegistryDiagnosticsInStructuredContent()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var brokenPackRoot = Path.Combine(ComposerPackPaths.UserGlobalRoot(tempRoot), "broken", "1.0.0");
            Directory.CreateDirectory(brokenPackRoot);
            File.WriteAllText(Path.Combine(brokenPackRoot, "pack.json"), "{}");

            var result = await UiComposerMcpTools.ListUiBlockPacks(
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("diagnostics").EnumerateArray().Should().NotBeEmpty();

            var serialized = JsonSerializer.Serialize(payload);
            serialized.Should().NotContain("wpfdevtools-composer-");
            serialized.Should().NotContain("broken");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("C:/Users/alice/project/renderers/button.xaml.sbn", "C:/Users")]
    [InlineData("D:/a/wpf-devtools/project/renderers/button.xaml.sbn", "D:/a")]
    [InlineData("/workspace/project/renderers/button.xaml.sbn", "/workspace")]
    public async Task RepairUiBlueprintTool_ShouldRedactAbsoluteRendererTemplatePathsInObservability(
        string rendererTemplatePath,
        string leakedPrefix)
    {
        var result = await UiComposerMcpTools.RepairUiBlueprint(
            BlueprintWithLayout("""{ "kind": "wpfui.button" }"""),
            diagnosticsJson: CreatePreviewDiagnosticJson(rendererTemplatePath),
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var observability = result.StructuredContent!.Value.GetProperty("observability");
        var repairLog = observability.GetProperty("logs").EnumerateArray().Single(log =>
            log.GetProperty("eventName").GetString() == "repair_log");
        var serialized = JsonSerializer.Serialize(observability);
        serialized.Should().NotContain(leakedPrefix);
        repairLog.GetProperty("filePath").GetString().Should().Be("<absolute-path-redacted>");
    }

    [Fact]
    public void PackImportService_ShouldAttachSanitizedImportObservability()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "sample.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "sample/1.0.0/pack.json", """
                    {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","displayName":"Sample","version":"1.0.0","blocks":[],"recipes":[]}
                    """);
                WriteEntry(archive, "sample/1.0.0/source.lock.json", """
                    {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
                    """);
            }

            var plan = PackImportService.CreateDryRunPlan(archivePath, Path.Combine(tempRoot, "packs"));
            var payload = JsonSerializer.SerializeToElement(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var observability = payload.GetProperty("observability");

            observability.GetProperty("logs").EnumerateArray().Should().Contain(log =>
                log.GetProperty("eventName").GetString() == "pack_import_log"
                && log.GetProperty("outcome").GetString() == "succeeded");
            observability.GetProperty("metrics").GetProperty("packImportSuccessRate").GetDouble()
                .Should().Be(1);
            var serialized = JsonSerializer.Serialize(observability);
            serialized.Should().NotContain(tempRoot);
            serialized.Should().NotContain(archivePath);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreatePreviewDiagnosticJson(string rendererTemplatePath)
        => JsonSerializer.Serialize(new
        {
            diagnostics = new[]
            {
                new
                {
                    code = "XamlCompileFailed",
                    message = "Generated preview XAML did not compile.",
                    jsonPath = "$.layout",
                    rendererTemplatePath
                }
            }
        });

    private static string BlueprintWithLayout(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "GeneratedView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
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
}
