using FluentAssertions;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ProjectGraphContractTests
{
    [Fact]
    public void McpServerProject_ShouldReferenceInspectorProject_ForDevelopmentRuns()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj"));

        content.Should().Contain(@"..\WpfDevTools.Inspector\WpfDevTools.Inspector.csproj",
            "dotnet run --project src/WpfDevTools.Mcp.Server must refresh the injected Inspector artifact during development");
    }

    [Fact]
    public void McpServerProject_ShouldTargetNet8WindowsInspectorBuild_WithoutReferencingItsAssembly()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj"));

        content.Should().Contain("<ReferenceOutputAssembly>false</ReferenceOutputAssembly>");
        content.Should().Contain("TargetFramework=net8.0-windows",
            "the server project must build the runtime Inspector target that is injected into .NET 8 WPF apps");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
