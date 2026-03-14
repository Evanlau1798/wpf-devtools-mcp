using System.IO;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpE2eFixturePathSelectionTests
{
    [Fact]
    public void GetPreferredBuildConfigurations_ShouldPreferCurrentTestConfigurationBeforeDebugAndRelease()
    {
        var configurations = McpE2eFixture.GetPreferredBuildConfigurations(
            @"G:\wpf-devtools-mcp\tests\WpfDevTools.Tests.Integration\bin\Task2\net8.0-windows\");

        configurations.Should().Equal("Task2", "Debug", "Release");
    }

    [Fact]
    public void SelectPreferredExecutable_ShouldReturnFirstExistingCandidate()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"WpfDevTools_McpE2eFixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var task2Path = CreateCandidate(tempRoot, "Task2", "server.exe");
            var debugPath = CreateCandidate(tempRoot, "Debug", "server.exe");
            var missingPath = Path.Combine(tempRoot, "Missing", "server.exe");

            var selected = McpE2eFixture.SelectPreferredExecutable(missingPath, task2Path, debugPath);

            selected.Should().Be(task2Path);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateCandidate(string tempRoot, string configuration, string fileName)
    {
        var path = Path.Combine(tempRoot, configuration, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, configuration);
        return path;
    }
}
