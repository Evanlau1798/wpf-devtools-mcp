using System.IO;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpE2eFixturePathSelectionTests
{
    [Fact]
    public void TryGetBuildConfiguration_ShouldReturnCurrentTestConfiguration()
    {
        var configuration = IntegrationExecutableLocator.TryGetBuildConfiguration(
            @"G:\wpf-devtools-mcp\tests\WpfDevTools.Tests.Integration\bin\x64\Task2\net8.0-windows\");

        configuration.Should().Be("Task2");
    }

    [Fact]
    public void TryGetBuildPlatform_ShouldReturnCurrentTestPlatform()
    {
        var platform = IntegrationExecutableLocator.TryGetBuildPlatform(
            @"G:\wpf-devtools-mcp\tests\WpfDevTools.Tests.Integration\bin\x64\Debug\net8.0-windows\");

        platform.Should().Be("x64");
    }

    [Fact]
    public void FindExecutable_ShouldReturnCurrentConfigurationArtifact_WhenItExists()
    {
        using var sandbox = new LocatorSandbox();
        var appBaseDirectory = sandbox.CreateFakeLayout(currentConfiguration: "Debug", createDebugArtifact: true, createReleaseArtifact: false);

        var executablePath = IntegrationExecutableLocator.FindExecutable(
            appBaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");

        executablePath.Should().NotBeNull();
        executablePath.Should().Contain(Path.Combine("bin", "Debug", "net8.0"));
    }

    [Fact]
    public void FindExecutable_ShouldReturnCurrentPlatformArtifact_WhenItExists()
    {
        using var sandbox = new LocatorSandbox();
        var appBaseDirectory = sandbox.CreateFakeLayout(
            currentConfiguration: "Debug",
            createDebugArtifact: false,
            createReleaseArtifact: false,
            createPlatformArtifact: true,
            currentPlatform: "x64");

        var executablePath = IntegrationExecutableLocator.FindExecutable(
            appBaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");

        executablePath.Should().NotBeNull();
        executablePath.Should().Contain(Path.Combine("bin", "x64", "Debug", "net8.0"));
    }

    [Fact]
    public void FindExecutable_ShouldNotFallbackFromPlatformBuildToStaleNonPlatformArtifact()
    {
        using var sandbox = new LocatorSandbox();
        var appBaseDirectory = sandbox.CreateFakeLayout(
            currentConfiguration: "Debug",
            createDebugArtifact: true,
            createReleaseArtifact: false,
            createPlatformArtifact: false,
            currentPlatform: "x64");

        var executablePath = IntegrationExecutableLocator.FindExecutable(
            appBaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");

        executablePath.Should().BeNull(
            "x64 no-build integration runs must not reuse stale non-platform artifacts with a different build fingerprint");
    }

    [Fact]
    public void FindExecutable_ShouldNotFallbackAcrossConfigurations()
    {
        using var sandbox = new LocatorSandbox();
        var appBaseDirectory = sandbox.CreateFakeLayout(currentConfiguration: "Debug", createDebugArtifact: false, createReleaseArtifact: true);

        var executablePath = IntegrationExecutableLocator.FindExecutable(
            appBaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");

        executablePath.Should().BeNull(
            "no-build integration flows should fail fast instead of silently reusing another configuration's artifact");
    }

    [Fact]
    public void FindExecutable_ShouldNotFallbackAcrossConfigurations_ForTestAppArtifacts()
    {
        using var sandbox = new LocatorSandbox();
        var appBaseDirectory = sandbox.CreateFakeLayout(currentConfiguration: "Debug", createDebugArtifact: false, createReleaseArtifact: true, projectName: "WpfDevTools.Tests.TestApp", projectDir: "tests", framework: "net8.0-windows", exeName: "WpfDevTools.Tests.TestApp.exe");

        var executablePath = IntegrationExecutableLocator.FindExecutable(
            appBaseDirectory,
            "tests",
            "WpfDevTools.Tests.TestApp",
            "net8.0-windows",
            "WpfDevTools.Tests.TestApp.exe");

        executablePath.Should().BeNull(
            "TestApp lookup should also stay within the current test configuration during no-build runs");
    }

    private sealed class LocatorSandbox : IDisposable
    {
        private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"WpfDevTools_McpE2eFixture_{Guid.NewGuid():N}");

        public string CreateFakeLayout(
            string currentConfiguration,
            bool createDebugArtifact,
            bool createReleaseArtifact,
            bool createPlatformArtifact = false,
            string? currentPlatform = null,
            string projectName = "WpfDevTools.Mcp.Server",
            string projectDir = "src",
            string framework = "net8.0",
            string exeName = "WpfDevTools.Mcp.Server.exe")
        {
            Directory.CreateDirectory(_rootDirectory);
            File.WriteAllText(Path.Combine(_rootDirectory, "WpfDevTools.sln"), string.Empty);

            if (createDebugArtifact)
            {
                var debugDirectory = Path.Combine(_rootDirectory, projectDir, projectName, "bin", "Debug", framework);
                Directory.CreateDirectory(debugDirectory);
                File.WriteAllText(Path.Combine(debugDirectory, exeName), string.Empty);
            }

            if (createReleaseArtifact)
            {
                var releaseDirectory = Path.Combine(_rootDirectory, projectDir, projectName, "bin", "Release", framework);
                Directory.CreateDirectory(releaseDirectory);
                File.WriteAllText(Path.Combine(releaseDirectory, exeName), string.Empty);
            }

            if (createPlatformArtifact)
            {
                currentPlatform.Should().NotBeNullOrWhiteSpace();
                var platformDirectory = Path.Combine(_rootDirectory, projectDir, projectName, "bin", currentPlatform!, currentConfiguration, framework);
                Directory.CreateDirectory(platformDirectory);
                File.WriteAllText(Path.Combine(platformDirectory, exeName), string.Empty);
            }

            var appBaseDirectory = string.IsNullOrWhiteSpace(currentPlatform)
                ? Path.Combine(_rootDirectory, "tests", "WpfDevTools.Tests.Integration", "bin", currentConfiguration, "net8.0-windows")
                : Path.Combine(_rootDirectory, "tests", "WpfDevTools.Tests.Integration", "bin", currentPlatform, currentConfiguration, "net8.0-windows");
            Directory.CreateDirectory(appBaseDirectory);
            return appBaseDirectory;
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
    }
}
