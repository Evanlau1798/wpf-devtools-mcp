using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpToolAnnotationSemanticsTests
{
    [Fact]
    public void WatchDpChanges_ShouldAdvertiseStatefulNonDestructiveRegistration()
    {
        var method = typeof(DependencyPropertyMcpTools).GetMethod(nameof(DependencyPropertyMcpTools.WatchDpChanges));
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse(
            "watch_dp_changes registers transient watcher state and is not a pure read");
        attribute.Destructive.Should().BeFalse(
            "watch_dp_changes does not perform destructive updates to the inspected application");
    }
}
