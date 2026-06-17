using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SdkPreferredDocumentationTests
{
    private static readonly string[] SdkUsageFiles =
    [
        "docfx/quickstart/sdk-hosted-inspector.md",
        "docfx/zh-tw/quickstart/sdk-hosted-inspector.md",
        "src/WpfDevTools.Inspector.Sdk/README.md"
    ];

    [Fact]
    public void SdkHostedQuickstart_ShouldExistAndBeLinkedFromPublicEntrypoints()
    {
        File.Exists(GetRepoFilePath("docfx/quickstart/sdk-hosted-inspector.md")).Should().BeTrue();
        File.Exists(GetRepoFilePath("docfx/zh-tw/quickstart/sdk-hosted-inspector.md")).Should().BeTrue();

        File.ReadAllText(GetRepoFilePath("README.md"))
            .Should().Contain("https://wpf-mcptools.evanlau1798.com/quickstart/sdk-hosted-inspector.html");
        File.ReadAllText(GetRepoFilePath("docfx/quickstart/toc.yml"))
            .Should().Contain("sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/toc.yml"))
            .Should().Contain("quickstart/sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/quickstart/index.md"))
            .Should().Contain("sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/index.md"))
            .Should().Contain("sdk-hosted-inspector.md");
    }

    [Fact]
    public void SdkHostedQuickstart_ShouldDocumentRequiredWorkflowAndBoundaries()
    {
        foreach (var file in new[]
                 {
                     "docfx/quickstart/sdk-hosted-inspector.md",
                     "docfx/zh-tw/quickstart/sdk-hosted-inspector.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("WpfDevTools.Inspector.Sdk");
            content.Should().Contain("InspectorSdkOptions");
            content.Should().Contain("AuthenticationSecretBase64");
            content.Should().Contain("CertificateDirectory");
            content.Should().Contain("InspectorSdk.Initialize()");
            content.Should().Contain("InspectorSdk.Shutdown()");
            content.Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
            content.Should().Contain("WPFDEVTOOLS_CERT_DIR");
            content.Should().Contain("absolute");
            content.Should().Contain("local absolute directory");
            content.Should().Contain("connect()");
            content.Should().Contain("SDK-hosted");
            content.Should().Contain("raw injection");
            content.Should().Contain("fallback");
            content.Should().Contain("zero-instrumentation");
            content.Should().Contain("single-file");
            content.Should().Contain("Native AOT");
            content.Should().Contain("trimmed");
            content.Should().Contain("AV");
            content.Should().Contain("deployment policy");
            content.Should().Contain("net8.0-windows");
            content.Should().Contain("NuGet");
            (content.Contains("published", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("正式發布", StringComparison.Ordinal)).Should().BeTrue(
                $"{file} should explain the pre-NuGet-publication package flow");
            content.Should().Contain("dotnet pack");
        }
    }

    [Fact]
    public void SdkUsageDocs_ShouldShowExplicitOptionsExampleAndBoundaries()
    {
        foreach (var file in SdkUsageFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("InspectorSdk.InitializeWithOptions(new InspectorSdkOptions");
            content.Should().Contain("ProcessId = Environment.ProcessId");
            content.Should().Contain("AuthenticationSecretBase64 = authSecretBase64");
            content.Should().Contain("CertificateDirectory = certificateDirectory");
            (content.Contains("Partial explicit SDK transport configuration is rejected", StringComparison.Ordinal) ||
             content.Contains("Partial explicit SDK transport configuration 會被拒絕", StringComparison.Ordinal)).Should().BeTrue(
                $"{file} should document that partial explicit SDK transport configuration is rejected");
            (content.Contains("not mixed with environment variables", StringComparison.Ordinal) ||
             content.Contains("不會與 environment variables 混用", StringComparison.Ordinal)).Should().BeTrue(
                $"{file} should document that explicit options are not mixed with environment variables");
            (content.Contains("The MCP server must use the same secret and certificate directory", StringComparison.Ordinal) ||
             content.Contains("MCP server 必須使用相同的 secret 與 certificate directory", StringComparison.Ordinal)).Should().BeTrue(
                $"{file} should require the MCP server to use the same secret and certificate directory");
            content.Should().Contain("local absolute directory");
        }
    }

    [Fact]
    public void SdkUsageDocs_ShouldShowCopyPasteTransportEnvironmentAndFailClosedCases()
    {
        foreach (var file in SdkUsageFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));

            content.Should().Contain("$env:WPFDEVTOOLS_AUTH_SECRET =",
                $"{file} should provide a copy-paste PowerShell snippet for the shared auth secret");
            content.Should().Contain("$env:WPFDEVTOOLS_CERT_DIR =",
                $"{file} should provide a copy-paste PowerShell snippet for the shared certificate directory");
            content.Should().Contain("InspectorSdk.Initialize()",
                $"{file} should tie the environment snippet to the SDK-host entrypoint");
            content.Should().Contain("Expected fail-closed cases",
                $"{file} should list the SDK-host transport failures operators should expect");
            content.Should().Contain("missing `WPFDEVTOOLS_AUTH_SECRET`",
                $"{file} should document the missing-auth-secret failure");
            content.Should().Contain("missing `WPFDEVTOOLS_CERT_DIR`",
                $"{file} should document the missing-certificate-directory failure");
            content.Should().Contain("mismatched `WPFDEVTOOLS_AUTH_SECRET`",
                $"{file} should document the mismatched-auth-secret failure");
            content.Should().Contain("mismatched `WPFDEVTOOLS_CERT_DIR`",
                $"{file} should document the mismatched-certificate-directory failure");
            content.Should().Contain("fail closed",
                $"{file} should describe the security posture in operator-facing terms");
        }
    }

    [Fact]
    public void SdkReadmeBasicUsage_ShouldIncludeWpfApplicationNamespace()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        content.Should().Contain("using System.Windows;");
        content.Should().Contain("public partial class App : Application");
        content.Should().Contain("protected override void OnStartup(StartupEventArgs e)");
    }

    [Fact]
    public void SdkReadme_ShouldWarnThatTrimmedAppsNeedStartupVerification()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        content.Should().Contain("Trimmed apps are still risky");
        content.Should().Contain("preferred fallback rather than a guarantee");
        content.Should().Contain("- Your application is trimmed (verify startup behavior");
    }

    [Fact]
    public void SdkPreferredPositioning_ShouldStayConsistentAcrossPublicDocs()
    {
        foreach (var file in new[]
                 {
                     "docfx/architecture/overview.md",
                     "docfx/quickstart/sdk-hosted-inspector.md",
                     "docfx/production/compatibility-matrix.md",
                     "docfx/production/bootstrap-and-injection.md",
                     "src/WpfDevTools.Inspector.Sdk/README.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("prefer SDK-hosted", $"{file} should describe the target-app-owned path as SDK-preferred");
            content.Should().Contain("raw injection", $"{file} should keep the fallback path explicit");
            content.Should().Contain("fallback", $"{file} should avoid implying raw injection is removed");
        }

        foreach (var file in new[]
                 {
                     "docfx/zh-tw/architecture/overview.md",
                     "docfx/zh-tw/quickstart/sdk-hosted-inspector.md",
                     "docfx/zh-tw/production/compatibility-matrix.md",
                     "docfx/zh-tw/production/bootstrap-and-injection.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("prefer SDK-hosted", $"{file} should keep the SDK-preferred positioning searchable");
            content.Should().Contain("raw injection");
            content.Should().Contain("fallback");
        }
    }

    [Fact]
    public void SdkProject_ShouldStayNet8WindowsOnlyUntilTargetExpansionIsImplemented()
    {
        var project = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj"));
        project.Should().Contain("<TargetFramework>net8.0-windows</TargetFramework>");
        project.Should().NotContain("<TargetFrameworks>");

        File.ReadAllText(GetRepoFilePath("docfx/quickstart/sdk-hosted-inspector.md"))
            .Should().Contain(".NET Framework WPF apps should keep using the raw injection path");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/sdk-hosted-inspector.md"))
            .Should().Contain(".NET Framework WPF app 應維持使用 raw injection path");
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
