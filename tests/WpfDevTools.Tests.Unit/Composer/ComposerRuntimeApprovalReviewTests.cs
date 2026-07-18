using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerGenericPreviewContractTests
{
    [Fact]
    public async Task PreviewBlueprint_WithUnapprovedRuntimePack_ShouldReturnStructuredApprovalReview()
    {
        using var trusted = new EnvironmentVariableScope(
            McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar,
            null);
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        var registry = CreateRegistry(projectRoot);
        var pack = registry.ListPacks().Packs.Single(item => item.Id == "sample");
        var expectedToken = UiPreviewRuntimeDependencyPolicy.CreateApprovalToken(pack);
        try
        {
            var result = await new UiBlueprintPreviewService(registry).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            var review = result.RuntimePackApprovalReviews.Should().ContainSingle().Which;
            review.PackId.Should().Be("sample");
            review.PackVersion.Should().Be("1.0.0");
            review.PackScope.Should().Be("project-local");
            review.Fingerprint.Should().Be(pack.Fingerprint);
            review.ApprovalToken.Should().Be(expectedToken);
            review.ApprovalScope.Should().Be("one-preview-call");
            review.Approved.Should().BeFalse();
            review.RuntimeResources.Should().Equal("<sample:Theme />");
            var package = review.PackageClosure.Should().ContainSingle().Which;
            package.Id.Should().Be("Sample.Runtime");
            package.VersionRange.Should().Be("[1.0.0]");
            package.ExactVersion.Should().Be("1.0.0");
            package.ContentHash.Should().Be(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
            result.Diagnostics.Should().ContainSingle(item =>
                item.Code == "PreviewRuntimeDependenciesNotApproved"
                && item.Message.Contains("runtimePackApprovalReviews", StringComparison.Ordinal));
            result.Diagnostics.Should().NotContain(item => item.Message.Contains(expectedToken, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_WithUnapprovedRuntimePack_ShouldExposeApprovalReview()
    {
        using var destructive = new EnvironmentVariableScope(
            McpServerConfiguration.AllowDestructiveToolsEnvVar,
            "true");
        using var trusted = new EnvironmentVariableScope(
            McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar,
            null);
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        using var sessionManager = new SessionManager();
        try
        {
            var result = await UiComposerMcpTools.PreviewUiBlueprint(
                sessionManager,
                Blueprint("sample.panel"),
                restoreEnabled: false,
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var review = result.StructuredContent!.Value
                .GetProperty("runtimePackApprovalReviews")
                .EnumerateArray()
                .Should()
                .ContainSingle()
                .Which;
            review.GetProperty("packId").GetString().Should().Be("sample");
            review.GetProperty("approved").GetBoolean().Should().BeFalse();
            review.GetProperty("packageClosure")[0].GetProperty("contentHash").GetString()
                .Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }
}
