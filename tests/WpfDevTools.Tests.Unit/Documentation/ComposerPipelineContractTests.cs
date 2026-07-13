using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ComposerPipelineContractTests
{
    [Fact]
    public void SecurityPipeline_ShouldCoverComposerProductionSecurityGates()
    {
        var securityWorkflow = ReadRepoFile(".github/workflows/security-scan.yml");
        var codeQlWorkflow = ReadRepoFile(".github/workflows/codeql.yml");
        var buildProps = ReadRepoFile("Directory.Build.props");

        securityWorkflow.Should().Contain("dotnet restore");
        securityWorkflow.Should().Contain("--locked-mode");
        securityWorkflow.Should().Contain("NuGetAudit=true");
        buildProps.Should().Contain("<WarningsAsErrors>$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>");
        securityWorkflow.Should().Contain("Run repository secret pattern scan");
        securityWorkflow.Should().Contain("Invoke-RepositorySecretScan.ps1");
        securityWorkflow.Should().Contain("Run .NET analyzer gate");
        securityWorkflow.Should().Contain("Run native bootstrapper security analysis");
        codeQlWorkflow.Should().Contain("github/codeql-action/analyze");
        codeQlWorkflow.Should().Contain("dotnet build WpfDevTools.sln");
        codeQlWorkflow.Should().Contain("src/WpfDevTools.Bootstrapper");

        securityWorkflow.Should().Contain("Run Composer pack fixture policy scan");
        securityWorkflow.Should().Contain("packs/builtin/wpfui/0.1.0/source.lock.json");
        securityWorkflow.Should().Contain("packs/baselines/wpfui/0.1.0/archives/wpfui-0.1.0.zip");
        securityWorkflow.Should().Contain("$allowedLicenses = @('MIT')");
        securityWorkflow.Should().Contain("Get-NormalizedTextSha256");
        securityWorkflow.Should().Contain("[System.IO.Compression.ZipFile]::OpenRead($baselineArchivePath)");
        securityWorkflow.Should().Contain("[System.Security.Cryptography.SHA256]::Create()");
        securityWorkflow.Should().Contain("localPath");
        securityWorkflow.Should().Contain("$localPath.StartsWith('/')");
        securityWorkflow.Should().Contain("Composer pack source uses a local absolute path");
        securityWorkflow.Should().Contain("Composer pack fixture file set does not match the reviewed baseline archive.");
        securityWorkflow.Should().Contain("Composer pack fixture file hash mismatch");
    }

    [Fact]
    public void ReleasePipeline_ShouldGatePublishingAndIndependentCiLaneOwnership()
    {
        var releaseWorkflow = ReadRepoFile(".github/workflows/release.yml");
        var releaseExportScript = ReadRepoFile("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1");
        var ciWorkflow = ReadRepoFile(".github/workflows/ci-cd.yml");

        releaseWorkflow.Should().Contain("Resolve release tag");
        releaseWorkflow.Should().Contain("must be a v-prefixed SemVer tag");
        releaseWorkflow.Should().Contain("Build release packages");
        releaseWorkflow.Should().Contain("Publish-Release.ps1");
        releaseWorkflow.Should().Contain("Stage GitHub Release assets");
        releaseWorkflow.Should().Contain("SHA256SUMS.txt");
        releaseWorkflow.Should().Contain("release-sbom.spdx.json");
        releaseWorkflow.Should().Contain("package-sbom.spdx.json");
        releaseExportScript.Should().Contain("release-notes.md");
        releaseExportScript.Should().Contain("gh release edit `$ReleaseTag --notes-file");
        releaseWorkflow.Should().Contain("Install staged x64 package smoke test");
        releaseWorkflow.Should().Contain("Start staged installed x64 runtime live smoke test");
        var uploadReleaseAssetsJob = GetWorkflowJob(releaseWorkflow, "upload-release-assets");
        uploadReleaseAssetsJob.Should().Contain("environment: production");
        uploadReleaseAssetsJob.Should().Contain("gh release create");
        uploadReleaseAssetsJob.Should().Contain("Upload staged assets to GitHub Release");
        releaseWorkflow.Should().Contain("Upload staged assets to GitHub Release");
        ciWorkflow.Should().Contain("--filter FullyQualifiedName~Composer");
        ciWorkflow.Should().NotContain("FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation",
            "this Composer-owned contract must independently prevent Documentation tests from disappearing from coverage");
    }

    [Fact]
    public void UiComposerDocs_ShouldPublishObservabilityPrivacyPolicy()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().Contain("Composer observability");
        english.Should().Contain("WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true");
        english.Should().Contain("does not include blueprint JSON, generated XAML, full user file content, secrets, or absolute local paths");

        zhTw.Should().Contain("Composer observability");
        zhTw.Should().Contain("WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true");
        zhTw.Should().Contain("不包含 blueprint JSON、generated XAML、完整使用者檔案內容、secrets 或 absolute local paths");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentApplyConfirmationGuard()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().Contain("- `confirmApply`: optional boolean");
        english.Should().Contain("Non-dry-run writes require `confirmApply=true`");
        zhTw.Should().Contain("- `confirmApply`: optional boolean");
        zhTw.Should().Contain("非 dry-run 寫入需要 `confirmApply=true`");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentBlueprintObjectHandoff()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().Contain("Serialize the `blueprint` object");
        english.Should().Contain("pass it under the `blueprintJson` parameter name");
        english.Should().Contain("65,536 characters");
        english.Should().Contain("host-backed controls");
        english.Should().Contain("ConvertTo-Json -Depth 100 -Compress");
        zhTw.Should().Contain("將 `blueprint` object 序列化");
        zhTw.Should().Contain("以 `blueprintJson` 參數名稱傳入");
        zhTw.Should().Contain("65,536 字元");
        zhTw.Should().Contain("需要 host 的控制項");
        zhTw.Should().Contain("ConvertTo-Json -Depth 100 -Compress");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentPackNeutralCompositionSkeletonContract()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().ContainAll("pack-neutral `compositionSkeleton`", "required", "declared slots", "allowedPackRoles");
        zhTw.Should().ContainAll("pack-neutral", "`compositionSkeleton`", "declared slots", "allowedPackRoles");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentPathAwarePackNeutralComposition()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().ContainAll("`compose_ui_blueprint`", "targetPath", "pack-defined", "never writes files");
        zhTw.Should().ContainAll("`compose_ui_blueprint`", "targetPath", "pack-defined", "不會寫入檔案");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentPackNeutralTabPreviewInheritance()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().ContainAll("`tabControl`", "`tabItem`", "inherited properties", "Foreground");
        zhTw.Should().ContainAll("`tabControl`", "`tabItem`", "繼承屬性", "Foreground");
    }

    [Fact]
    public void UiComposerDocs_ShouldDocumentTransientPreviewElementCorrelation()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().ContainAll("`elementCorrelations`", "renderer root", "transient `x:Name`", "not stored in the blueprint");
        zhTw.Should().ContainAll("`elementCorrelations`", "renderer root", "transient `x:Name`", "不會寫入或儲存在 blueprint");
    }

    [Fact]
    public void UiComposerDocs_ShouldPublishCompleteApplyToRunningAppWorkflow()
    {
        var english = ReadRepoFile("docfx/reference/tools/ui-composer.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        english.Should().Contain("## Apply-to-build workflow");
        english.Should().ContainAll(
            "behaviorIntegrationContract",
            "packageIntegrationGuidance",
            "inspectionConfidence",
            "Directory.Packages.props",
            "projectPackageReference",
            "centralPackageVersion",
            "codeBehindBaseType",
            "dotnet build",
            "capture_state_snapshot");
        english.Should().NotContain("<PackageReference Include=\"WPF-UI\"");
        english.Should().NotContain("public partial class MainWindow : Wpf.Ui.Controls.FluentWindow");

        zhTw.Should().Contain("## 從 apply 到可執行應用程式");
        zhTw.Should().ContainAll(
            "behaviorIntegrationContract",
            "packageIntegrationGuidance",
            "inspectionConfidence",
            "Directory.Packages.props",
            "projectPackageReference",
            "centralPackageVersion",
            "codeBehindBaseType",
            "dotnet build",
            "capture_state_snapshot");
        zhTw.Should().NotContain("<PackageReference Include=\"WPF-UI\"");
        zhTw.Should().NotContain("public partial class MainWindow : Wpf.Ui.Controls.FluentWindow");
    }

    private static string GetWorkflowJob(string workflow, string name)
    {
        var start = workflow.IndexOf($"  {name}:", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain job {name}");
        var next = workflow.IndexOf("\n  ", start + 1, StringComparison.Ordinal);
        while (next >= 0 && next + 3 < workflow.Length && char.IsWhiteSpace(workflow[next + 3]))
        {
            next = workflow.IndexOf("\n  ", next + 1, StringComparison.Ordinal);
        }

        return next < 0 ? workflow[start..] : workflow[start..next];
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
