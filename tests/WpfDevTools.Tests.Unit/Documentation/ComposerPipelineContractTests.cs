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
        ciWorkflow.Should().Contain("Category=ComposerCompile|Category=ComposerRuntime");
        ciWorkflow.Should().Contain("Category=ComposerAcceptance");
        ciWorkflow.Should().NotContain("FullyQualifiedName!~ComposerPreview",
            "Composer lanes should be selected by capabilities instead of test class names");
        ciWorkflow.Should().NotContain("FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation",
            "this Composer-owned contract must independently prevent Documentation tests from disappearing from coverage");
    }

    [Fact]
    public void EnglishUiComposerDocs_ShouldCoverStablePublicContracts()
    {
        var documentation = ReadRepoFile("docfx/reference/tools/ui-composer.md");

        documentation.Should().ContainAll(
            "Composer observability",
            "WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true",
            "blueprint JSON",
            "`confirmApply`",
            "`dryRun`",
            "`reviewedArchiveSha256`",
            "`blueprintJson`",
            "65,536",
            "ConvertTo-Json -Depth 100 -Compress",
            "`compositionSkeleton`",
            "allowedPackRoles",
            "`kind`",
            "`themeTokens`",
            "`resourceVariants`",
            "`visualRole`",
            "SurfaceThemeContrastRisk",
            "`required=true`",
            "`compose_ui_blueprint`",
            "`create_ui_blueprint_draft`",
            "`patch_ui_blueprint_draft`",
            "opaque `draftRef`",
            "JSON Merge Patch",
            "`operations`",
            "`operationIndex`",
            "16",
            "all-or-nothing",
            "immutable",
            "process-local",
            "30 minutes",
            "32 drafts",
            "BlueprintDraftNotFound",
            "targetPath",
            "@ElementName.slots",
            "@ElementName.properties",
            "`insertedNodeSummary`",
            "`invalidCandidate`",
            "`candidateBlueprintJson`",
            "`tabControl`",
            "`tabItem`",
            "Foreground",
            "`elementCorrelations`",
            "renderer root",
            "`x:Name`",
            "`elementName`",
            "`automationId`",
            "DuplicateAutomationId",
            "WPFDEVTOOLS_COMPOSER_TRUSTED_RUNTIME_PACKS",
            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true",
            "UNC, device, and other remote roots are rejected",
            "rejects external or rooted image, navigation, media, XML, and resource-dictionary locations",
            "executable third-party dependencies",
            "content-bound approval token",
            "SHA-512 `contentHash`",
            "preview-local NuGet cache",
            "## Apply-to-build workflow",
            "`apply_ui_project_integration`",
            "`projectIntegrationPlan`",
            "`reviewedPlanHash`",
            "IntegrationPlanChanged",
            "`backupPath`",
            "rollback",
            "behaviorIntegrationContract",
            "packageIntegrationGuidance",
            "inspectionConfidence",
            "inspectedFiles",
            "inspectionLimitations",
            "Directory.Packages.props",
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>",
            "projectPackageReference",
            "centralPackageVersion",
            "codeBehindBaseType",
             "dotnet build",
             "capture_state_snapshot");
        documentation.Should().Contain(
            "retry `preview_ui_blueprint` with `screenshotMaxWidth=1024` and `screenshotMaxHeight=1024`");
        documentation.Should().NotContain("retry `element_screenshot`",
            "the temporary preview host has exited before the tool returns");
        AssertPackNeutralExamples(documentation);
    }

    [Fact]
    public void ComposerPipeline_ShouldClassifyExpensiveTestsByCapability()
    {
        ReadRepoFile("tests/WpfDevTools.Tests.Unit/Composer/ComposerPreviewCompileTests.cs")
            .Should().Contain("[Trait(\"Category\", \"ComposerCompile\")]")
            .And.Contain("[Trait(\"Category\", \"ComposerRuntime\")]");
        ReadRepoFile("tests/WpfDevTools.Tests.Unit/Composer/ComposerGenericPreviewContractTests.cs")
            .Should().Contain("[Trait(\"Category\", \"ComposerCompile\")]");
        ReadRepoFile("tests/WpfDevTools.Tests.Unit/Composer/ComposerPreviewRecipeRuntimeTests.cs")
            .Should().Contain("[Trait(\"Category\", \"ComposerRuntime\")]");
        ReadRepoFile("tests/WpfDevTools.Tests.Integration/Composer/ComposerThirdPartyAcceptanceTests.cs")
            .Should().Contain("[Trait(\"Category\", \"ComposerAcceptance\")]");
    }

    [Fact]
    public void TraditionalChineseUiComposerDocs_ShouldCoverStablePublicContracts()
    {
        var documentation = ReadRepoFile("docfx/zh-tw/reference/tools/ui-composer.md");

        documentation.Should().ContainAll(
            "Composer observability",
            "WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true",
            "blueprint JSON",
            "`confirmApply`",
            "`dryRun`",
            "`reviewedArchiveSha256`",
            "`blueprintJson`",
            "65,536",
            "ConvertTo-Json -Depth 100 -Compress",
            "`compositionSkeleton`",
            "allowedPackRoles",
            "`kind`",
            "`themeTokens`",
            "`resourceVariants`",
            "`visualRole`",
            "SurfaceThemeContrastRisk",
            "`required=true`",
            "`compose_ui_blueprint`",
            "`create_ui_blueprint_draft`",
            "`patch_ui_blueprint_draft`",
            "opaque `draftRef`",
            "JSON Merge Patch",
            "`operations`",
            "16",
            "all-or-nothing",
            "immutable",
            "process-local",
            "30 minutes",
            "32 drafts",
            "BlueprintDraftNotFound",
            "targetPath",
            "@ElementName.slots",
            "@ElementName.properties",
            "`insertedNodeSummary`",
            "`invalidCandidate`",
            "`candidateBlueprintJson`",
            "`tabControl`",
            "`tabItem`",
            "Foreground",
            "`elementCorrelations`",
            "renderer root",
            "`x:Name`",
            "`elementName`",
            "`automationId`",
            "DuplicateAutomationId",
            "WPFDEVTOOLS_COMPOSER_TRUSTED_RUNTIME_PACKS",
            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true",
            "UNC、device 與其他 remote roots",
            "拒絕外部或 rooted image、navigation、media、XML 與 resource-dictionary locations",
            "可執行的第三方相依套件",
            "綁定內容的 approval token",
            "SHA-512 `contentHash`",
            "preview-local NuGet cache",
            "## 從 apply 到可執行應用程式",
            "`apply_ui_project_integration`",
            "`projectIntegrationPlan`",
            "`reviewedPlanHash`",
            "IntegrationPlanChanged",
            "`backupPath`",
            "rollback",
            "behaviorIntegrationContract",
            "packageIntegrationGuidance",
            "inspectionConfidence",
            "inspectedFiles",
            "inspectionLimitations",
            "Directory.Packages.props",
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>",
            "projectPackageReference",
            "centralPackageVersion",
            "codeBehindBaseType",
             "dotnet build",
             "capture_state_snapshot");
        documentation.Should().Contain(
            "以 `screenshotMaxWidth=1024` 與 `screenshotMaxHeight=1024` 重跑 `preview_ui_blueprint`");
        documentation.Should().NotContain("重試 `element_screenshot`",
            "tool 回傳前 temporary preview host 已結束");
        AssertPackNeutralExamples(documentation);
    }

    [Fact]
    public void PublicPrereleaseE2eGuidance_ShouldKeepComposerDiscoveryAndVisualReviewAuthoritative()
    {
        var documentation = ReadRepoFile("docfx/contributors/testing-and-tdd.md");

        documentation.Should().ContainAll(
            "release-specific expected count",
            "77 tools",
            "`create_ui_blueprint_draft`",
            "`compose_ui_blueprint`",
            "`validate_ui_blueprint`",
            "`render_ui_blueprint`",
            "`preview_ui_blueprint`",
            "`repair_ui_blueprint`",
            "`apply_ui_blueprint`",
            "`apply_ui_project_integration`",
            "Do not use Computer Use",
            "Agent-selected",
            "contrast and readability",
            "puzzle-like block and slot workflow",
            "product, harness, and external-environment friction");
    }

    private static void AssertPackNeutralExamples(string documentation)
    {
        documentation.Should().NotContain("<PackageReference Include=\"WPF-UI\"");
        documentation.Should().NotContain("public partial class MainWindow : Wpf.Ui.Controls.FluentWindow");
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
