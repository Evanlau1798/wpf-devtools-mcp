using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class NamedPipeClientResilienceTests
{
    [Fact]
    public async Task ConnectAsync_WhenPipeAccessIsDenied_ShouldReturnFalse()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var currentUser = WindowsIdentity.GetCurrent().User;

        currentUser.Should().NotBeNull();

        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            currentUser!,
            PipeAccessRights.ReadWrite,
            AccessControlType.Deny));

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        using var server = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);

        using var client = new NamedPipeClient(processId, pipeName);

        var connected = await client.ConnectAsync(TimeSpan.FromMilliseconds(250), maxRetries: 1);

        connected.Should().BeFalse();
        client.IsConnected.Should().BeFalse();
    }
}
