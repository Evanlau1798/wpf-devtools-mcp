using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class TimeoutCleanupContractTests
{
    [Theory]
    [InlineData(
        "tests/WpfDevTools.Tests.Unit.Release/InstallerScripts/InstallerTuiRuntimeTests.cs",
        "Thread.Sleep(TimeSpan.FromMilliseconds(6500))")]
    [InlineData(
        "tests/WpfDevTools.Tests.Unit.Release/InstallerScripts/InstallerCliVerificationTests.cs",
        "Thread.Sleep(TimeSpan.FromMilliseconds(3500))")]
    [InlineData(
        "tests/WpfDevTools.Tests.Unit/McpServer/NamedPipeClientProtocolTests.cs",
        "Task.Delay(TimeSpan.FromMilliseconds(2_300))")]
    public void TimeoutCleanupTests_ShouldUseConditionBasedSynchronization(
        string relativePath,
        string forbiddenDelay)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain(forbiddenDelay,
            "timeout cleanup and protocol grace tests should wait on observable process or transport state instead of fixed sleeps");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
