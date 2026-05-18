using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class ReleaseReadinessDocumentationTests
{
    private const string PublicEndpointUnavailableWarning = "Public release endpoints are not yet anonymously reachable";
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void Readme_ShouldLinkToReleaseGuideAndPreflightCommand()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("RELEASING.md",
            "maintainers should be able to discover the dedicated release guide from the README");
        content.Should().Contain("Preflight-Release.ps1",
            "the README should point maintainers to the no-upload local release validation command");
    }

    [Fact]
    public void ReleasingGuide_ShouldDocumentLocalPreflightAndGitHubWorkflow()
    {
        var content = File.ReadAllText(GetRepoFilePath("RELEASING.md"));

        content.Should().Contain("Preflight-Release.ps1",
            "the release guide should document the local preflight script");
        content.Should().Contain("scripts/tools/packaging/Preflight-Release.ps1",
            "the release guide should point maintainers at the current preflight script path");
        content.Should().Contain("scripts/tools/build-release.ps1",
            "the release guide should point maintainers at the current package-generation entrypoint");
        content.Should().Contain("scripts/tools/packaging/Publish-Release.ps1",
            "the release guide should explain which packaging script the build wrapper delegates to today");
        content.Should().Contain("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1",
            "the release guide should explain which script stages GitHub Release upload metadata");
        content.Should().NotContain("scripts/release/Preflight-Release.ps1",
            "the guide should not reference the retired release script path");
        content.Should().Contain("workflow_dispatch",
            "the release guide should explain how to manually rerun the GitHub release workflow");
        content.Should().Contain("release.yml",
            "the release guide should point maintainers to the GitHub release automation workflow");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            "maintainers need the exact signer pin variables called out before running signed Release packaging");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH",
            "the guide should explain how hosted or local packaging supplies the signing certificate to Publish-Release.ps1");
        content.Should().Contain("without uploading to GitHub",
            "the guide should include a local validation path that stops before publication");
        content.Should().Contain("Desktop development with C++",
            "maintainers need the native bootstrapper toolchain prerequisites called out before running release packaging");
        content.Should().Contain("ARM64",
            "the guide should mention that ARM64 release packaging requires the ARM64 native build tools workload/components");
        content.Should().Contain("WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE",
            "maintainers need the release guide to call out the fail-closed ARM64 runtime validation gate used by GitHub release automation");
        content.Should().Contain("self-hosted Windows ARM64 runner",
            "the release guide should explain that public ARM64 publication now requires a runner that can actually launch the packaged ARM64 executable");
    }

    [Fact]
    public void PublicReleaseChecklist_ShouldNotClaimInstallerCommandIsDocumentedBeforeEndpointPublication()
    {
        var checklist = File.ReadAllText(GetRepoFilePath("PUBLIC_RELEASE_READINESS_CHECKLIST.md"));
        var warningFiles = GetPublicEndpointWarningFiles();
        var completedClaims = GetCompletedPublicInstallerOnboardingClaims(checklist);

        warningFiles.Should().NotBeEmpty(
            "this contract applies while README or DocFX quickstarts still warn that public endpoints are unavailable");
        completedClaims.Should().BeEmpty(
            "the checklist must not mark public installer onboarding complete while README and DocFX still warn that public endpoints are unavailable");
    }

    [Fact]
    public void PublicReleaseChecklistGuard_ShouldRejectRewordedCheckedInstallerOnboardingClaims()
    {
        const string rewordedCompletedClaim = "- [x] Publish the public installer alias and document the one-line installer in README and DocFX quickstarts.";
        const string uncheckedRemainingClaim = "- [ ] Publish the public installer alias and document `irm https://wpf-mcptools.evanlau1798.com | iex` in README and DocFX quickstart pages after anonymous endpoint smoke checks pass.";
        const string releaseGateClaim = "- [x] Document release preflight gates for signing and the public installer alias in `RELEASING.md`.";

        GetCompletedPublicInstallerOnboardingClaims(rewordedCompletedClaim).Should().ContainSingle(
            "the guard should reject reworded checked public installer onboarding claims");
        GetCompletedPublicInstallerOnboardingClaims(uncheckedRemainingClaim).Should().BeEmpty(
            "unchecked remaining-work items are allowed to mention the public installer");
        GetCompletedPublicInstallerOnboardingClaims(releaseGateClaim).Should().BeEmpty(
            "release preflight documentation is not the same as public onboarding documentation");
    }

    [Fact]
    public void CodeSigningGuide_ShouldMatchCurrentToolPathsAndParameters()
    {
        var content = File.ReadAllText(GetRepoFilePath("CODE_SIGNING.md"));

        content.Should().Contain("scripts\\tools\\Create-SelfSignedCert.ps1",
            "the code-signing guide should use the current self-signed certificate helper path");
        content.Should().Contain("scripts\\tools\\Sign-Binaries.ps1",
            "the code-signing guide should use the current signing script path");
        content.Should().Contain("tmp/cert/WpfDevTools.pfx",
            "the guide should document the current default output path for development certificates");
        content.Should().Contain($"Double-click `{GetDefaultSelfSignedCertificateCerPath()}`",
            "the guide should install the public certificate from the helper's default output path");
        content.Should().Contain("CertificateThumbprint",
            "the guide should document the supported certificate-store signing parameter");
        content.Should().Contain("WPFDEVTOOLS_PFX_PASSWORD",
            "the guide should describe the password environment variable used by the signing script");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64",
            "the CI signing guide should describe how GitHub Actions materializes the release certificate on hosted runners");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            "the guide should document the signer-pinning secret used by release packaging validation");
        content.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA",
            "the release guide should explain the explicit test-only trust hook used by the hosted smoke lane for synthetic local archive validation");
        content.Should().NotContain(".\\scripts\\Create-SelfSignedCert.ps1",
            "the guide should not reference the retired root-level helper path");
        content.Should().NotContain("-Password \"YourPassword\"",
            "the guide should not describe a PFX password parameter that the current signing script does not expose");
        content.Should().NotContain("CERT_PATH",
            "the GitHub Actions guidance should not rely on a runner-local certificate path secret that does not exist on hosted runners");
    }

    private static string GetDefaultSelfSignedCertificateCerPath()
    {
        var script = File.ReadAllText(GetRepoFilePath("scripts/tools/Create-SelfSignedCert.ps1"));
        var match = Regex.Match(script, @"\[string\]\$OutputPath\s*=\s*""(?<path>[^""]+)""");
        match.Success.Should().BeTrue("Create-SelfSignedCert.ps1 should keep a discoverable OutputPath default");

        return $"{match.Groups["path"].Value.Replace("\\", "/").TrimStart('.', '/')}/WpfDevTools.cer";
    }

    private static IReadOnlyList<string> GetCompletedPublicInstallerOnboardingClaims(string checklist)
        => checklist.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => Regex.IsMatch(line, @"^-\s*\[x\]\s+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Where(IsPublicInstallerOnboardingClaim)
            .ToList();

    private static bool IsPublicInstallerOnboardingClaim(string checkedLine)
    {
        var normalized = Regex.Replace(checkedLine, @"\s+", " ");
        if (Regex.IsMatch(normalized,
            @"https://wpf-mcptools\.evanlau1798\.com|irm\s+https://wpf-mcptools\.evanlau1798\.com\s*\|\s*iex",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        var mentionsPublicInstaller = Regex.IsMatch(normalized,
            @"public\s+installer|installer\s+alias|installer\s+command|one-line\s+installer",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var mentionsOnboardingDocs = Regex.IsMatch(normalized,
            @"README|DocFX|quickstart|onboarding",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return mentionsPublicInstaller && mentionsOnboardingDocs;
    }

    private static IReadOnlyList<string> GetPublicEndpointWarningFiles()
    {
        var paths = new List<string> { GetRepoFilePath("README.md") };
        paths.AddRange(Directory.EnumerateFiles(GetRepoFilePath("docfx/quickstart"), "*.md"));
        paths.AddRange(Directory.EnumerateFiles(GetRepoFilePath("docfx/zh-tw/quickstart"), "*.md"));

        return paths
            .Where(path => File.ReadAllText(path).Contains(
                PublicEndpointUnavailableWarning,
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
            .ToList();
    }

    [Theory]
    [InlineData(
        "docfx/production/deployment.md",
        "Signed payload provenance checklist",
        "package-local smoke",
        "installed path")]
    [InlineData(
        "docfx/zh-tw/production/deployment.md",
        "已簽章 payload provenance 檢查清單",
        "package-local smoke",
        "已安裝路徑")]
    public void DeploymentGuides_ShouldPublishSignedPayloadProvenanceChecklist(
        string relativePath,
        string checklistHeading,
        string packageSmokePhrase,
        string installedPathPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(checklistHeading,
            $"{relativePath} should make signed payload provenance a first-class production gate");
        content.Should().Contain("SHA256SUMS.txt",
            $"{relativePath} should require release checksum sidecars in the production checklist");
        content.Should().Contain("release-assets.json",
            $"{relativePath} should require canonical release metadata in the production checklist");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            $"{relativePath} should require an explicit signer pin when adjacent sidecars are absent");
        content.Should().Contain("wpf-devtools-<arch>.exe",
            $"{relativePath} should tie the checklist to the installed release executable");
        content.Should().Contain(packageSmokePhrase,
            $"{relativePath} should require a package-local smoke check before trusting the install");
        content.Should().Contain(installedPathPhrase,
            $"{relativePath} should require validation from the final installed location");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
