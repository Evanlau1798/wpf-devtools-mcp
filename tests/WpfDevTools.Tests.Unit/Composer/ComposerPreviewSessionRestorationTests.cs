using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Tests.Unit.Inspector;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("TimingSensitive")]
public sealed class ComposerPreviewSessionRestorationTests
{
    [Fact]
    public async Task CleanupPreviewSession_ShouldRestorePreviouslyActiveConnectedProcess()
    {
        var originalProcessId = NextSyntheticProcessId();
        var previewProcessId = NextSyntheticProcessId();
        using var plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
        using var originalHost = new InspectorHost(originalProcessId);
        using var sessionManager = new SessionManager();
        originalHost.Start();

        sessionManager.AddSession(originalProcessId);
        var originalClient = sessionManager.GetPipeClient(originalProcessId);
        (await originalClient!.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        sessionManager.SetActiveProcess(originalProcessId);
        sessionManager.AddSession(previewProcessId);
        sessionManager.SetActiveProcess(previewProcessId);

        UiBlueprintPreviewDiagnosticsBridge.RemovePreviewSessionAndRestoreActiveProcess(
            sessionManager,
            previewProcessId,
            originalProcessId);

        sessionManager.HasSession(previewProcessId).Should().BeFalse();
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(originalProcessId);
    }
}
