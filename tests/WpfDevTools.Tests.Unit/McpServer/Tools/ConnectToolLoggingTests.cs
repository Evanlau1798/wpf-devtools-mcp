using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolLoggingTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Fact]
    public async Task Execute_WhenPipeConnectionThrows_ShouldWriteDiagnosticTraceBeforeCleanup()
    {
        EnsureDummyBootstrapperExists();
        using var sessionManager = new SessionManager();
        var tool = new ConnectTool(
            sessionManager,
            new SuccessfulProcessInjector(),
            new FakeProcessDetector(),
            _ => { });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var listener = new CapturingTraceListener();

        Trace.Listeners.Add(listener);
        try
        {
            var act = () => tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();

            Trace.Flush();
            listener.Messages.Should().ContainSingle(message =>
                message.Contains("ConnectTool cleanup triggered", StringComparison.Ordinal) &&
                message.Contains("12345", StringComparison.Ordinal));
            sessionManager.HasSession(12345).Should().BeFalse();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    public void Dispose()
    {
        if (_dummyBootstrapperPath != null && File.Exists(_dummyBootstrapperPath))
        {
            try { File.Delete(_dummyBootstrapperPath); } catch { }
        }
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
    }

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId) => new()
        {
            ProcessId = processId,
            ProcessName = "TestApp",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true
        };
    }

    private sealed class SuccessfulProcessInjector : IProcessInjector
    {
        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
            => InjectionResult.CreateSuccess(request.ProcessId, request.InspectorDllPath, bootstrapExitCode: 0, pipeName: request.ExpectedPipeName);
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _messages.Add(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _messages.Add(message);
            }
        }
    }
}
