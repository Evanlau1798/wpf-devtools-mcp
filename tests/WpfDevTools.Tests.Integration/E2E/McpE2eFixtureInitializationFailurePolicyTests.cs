using System.ComponentModel;
using System.IO;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpE2eFixtureInitializationFailurePolicyTests
{
    [Fact]
    public void CreateServerEnvironment_ShouldIncludeIsolatedSecurityCredentials()
    {
        var testAppPath = Path.Combine(Path.GetTempPath(), "WpfDevTools.Tests.TestApp.exe");
        var authSecret = Convert.ToBase64String(new byte[32]);
        var certDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var environment = McpE2eFixture.CreateServerEnvironment(testAppPath, authSecret, certDirectory);

        environment.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar, testAppPath);
        environment.Should().Contain(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar, testAppPath);
        environment.Should().Contain(McpServerConfiguration.AllowDestructiveToolsEnvVar, "true");
        environment.Should().Contain(McpServerConfiguration.AllowScreenshotsEnvVar, "true");
        environment.Should().Contain(McpServerConfiguration.AllowViewModelInspectionEnvVar, "true");
        environment.Should().Contain("WPFDEVTOOLS_AUTH_SECRET", authSecret);
        environment.Should().Contain("WPFDEVTOOLS_CERT_DIR", certDirectory);
        environment.Should().Contain("TEMP", certDirectory,
            "live MCP server runs should use an isolated temp root so short-lived temp logs and auth handoff files are cleaned up with the fixture cert directory");
        environment.Should().Contain("TMP", certDirectory,
            "Windows temp resolution should stay inside the same isolated fixture root for live runs");
        environment.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_RELEASE_SIGNATURE_SKIP", "1");
        environment.Should().NotContainKey("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");
    }

    [Fact]
    public void CreateIsolatedTempEnvironment_ShouldRouteScreenshotsUnderFixtureTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WpfDevTools.McpE2e." + Guid.NewGuid().ToString("N"));
        var environment = McpE2eFixture.CreateIsolatedTempEnvironment(tempRoot);

        environment.Should().Contain(ScreenshotStorage.DirectoryEnvironmentVariable, Path.Combine(tempRoot, "screenshots"),
            "file screenshot E2E output should be isolated from real LocalApplicationData and cleaned up with the fixture root");
    }

    [Fact]
    public void RecordInitializationFailureForTesting_ShouldCleanFixtureTempRootBeforePublishingSkipReason()
    {
        var fixture = new McpE2eFixture();
        var certDirectory = fixture.CertificateDirectoryForTesting;
        Directory.CreateDirectory(certDirectory);
        File.WriteAllText(Path.Combine(certDirectory, "sentinel.txt"), "created before reconnect failure");

        fixture.RecordInitializationFailureForTesting(new TimeoutException("connect timed out"));

        fixture.SkipReason.Should().Contain("connect timed out");
        Directory.Exists(certDirectory).Should().BeFalse(
            "partial-start fixture resources should be cleaned before tests continue with SkipReason");
    }

    [Theory]
    [MemberData(nameof(ExpectedEnvironmentFailures))]
    public void ShouldConvertInitializationFailureToSkip_ShouldAllowExpectedEnvironmentFailures(Exception exception)
    {
        McpE2eFixture.ShouldConvertInitializationFailureToSkip(exception)
            .Should().BeTrue("expected environment startup failures may skip E2E suites without hiding product regressions");
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public void ShouldConvertInitializationFailureToSkip_ShouldRejectUnexpectedFailures(Exception exception)
    {
        McpE2eFixture.ShouldConvertInitializationFailureToSkip(exception)
            .Should().BeFalse("unexpected fixture failures should fail visibly instead of being converted to SkipReason");
    }

    public static TheoryData<Exception> ExpectedEnvironmentFailures() =>
        new()
        {
            new TimeoutException("TestApp main window did not appear."),
            new IOException("Process executable could not be started."),
            new Win32Exception(5, "Access denied while starting a child process."),
            new UnauthorizedAccessException("Access denied while starting a child process.")
        };

    public static TheoryData<Exception> UnexpectedFailures() =>
        new()
        {
            new OutOfMemoryException("fatal"),
            new BadImageFormatException("bad image"),
            new InvalidOperationException("connect returned success=false"),
            new ArgumentException("bad fixture wiring")
        };
}
