using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Tests.Unit.Inspector;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("TimingSensitive")]
public sealed class ComposerPreviewSessionRestorationTests
{
    [Fact]
    public async Task PreviewHostConnection_ShouldNotChangeActiveProcess()
    {
        var originalProcessId = NextSyntheticProcessId();
        var previewProcessId = NextSyntheticProcessId();
        using var plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
        using var originalHost = new InspectorHost(originalProcessId);
        using var previewHost = new InspectorHost(previewProcessId);
        using var sessionManager = new SessionManager();
        originalHost.Start();
        previewHost.Start();

        sessionManager.AddSession(originalProcessId);
        var originalClient = sessionManager.GetPipeClient(originalProcessId);
        (await originalClient!.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        sessionManager.SetActiveProcess(originalProcessId);

        var failure = await sessionManager.ConnectExistingHostSessionAsync(
            previewProcessId,
            TimeSpan.FromSeconds(5),
            CancellationToken.None,
            selectAsActive: false);

        failure.Should().Be(NamedPipeConnectFailure.None);
        sessionManager.HasSession(previewProcessId).Should().BeTrue();
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(originalProcessId);

        sessionManager.RemoveSession(previewProcessId);
        sessionManager.TryGetActiveProcessId(out activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(originalProcessId);
    }
}
