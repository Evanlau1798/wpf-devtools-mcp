using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ToolRegistryTests
{
    [Fact]
    public void RegisterTool_WithValidTool_ShouldAddToRegistry()
    {
        // Arrange
        var registry = new ToolRegistry();
        var toolDef = new ToolDefinition
        {
            Name = "ping",
            Description = "Test connectivity",
            Parameters = new { }
        };

        // Act
        registry.RegisterTool(toolDef);

        // Assert
        var tools = registry.GetAllTools();
        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("ping");
    }

    [Fact]
    public void RegisterTool_WithDuplicateName_ShouldThrowException()
    {
        // Arrange
        var registry = new ToolRegistry();
        var toolDef1 = new ToolDefinition
        {
            Name = "ping",
            Description = "Test connectivity",
            Parameters = new { }
        };
        var toolDef2 = new ToolDefinition
        {
            Name = "ping",
            Description = "Duplicate",
            Parameters = new { }
        };

        registry.RegisterTool(toolDef1);

        // Act & Assert
        var act = () => registry.RegisterTool(toolDef2);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void GetTool_WithExistingName_ShouldReturnTool()
    {
        // Arrange
        var registry = new ToolRegistry();
        var toolDef = new ToolDefinition
        {
            Name = "ping",
            Description = "Test connectivity",
            Parameters = new { }
        };
        registry.RegisterTool(toolDef);

        // Act
        var result = registry.GetTool("ping");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("ping");
    }

    [Fact]
    public void GetTool_WithNonExistingName_ShouldReturnNull()
    {
        // Arrange
        var registry = new ToolRegistry();

        // Act
        var result = registry.GetTool("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllTools_WithMultipleTools_ShouldReturnAllInOrder()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(new ToolDefinition { Name = "ping", Description = "Test", Parameters = new { } });
        registry.RegisterTool(new ToolDefinition { Name = "connect", Description = "Connect", Parameters = new { } });
        registry.RegisterTool(new ToolDefinition { Name = "get_processes", Description = "List", Parameters = new { } });

        // Act
        var tools = registry.GetAllTools();

        // Assert
        tools.Should().HaveCount(3);
        tools.Select(t => t.Name).Should().ContainInOrder("ping", "connect", "get_processes");
    }
}
