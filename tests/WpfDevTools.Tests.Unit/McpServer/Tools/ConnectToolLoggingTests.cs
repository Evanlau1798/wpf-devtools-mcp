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

[Collection("TraceState")]
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
            _ => { },
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var listener = new CapturingTraceListener();

        Trace.Listeners.Add(listener);
        try
        {
            var act = () => tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();

            SpinWait.SpinUntil(
                () => listener.Messages.Any(message =>
                    message.Contains("ConnectTool cleanup triggered", StringComparison.Ordinal) &&
                    message.Contains("12345", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(2)).Should().BeTrue();

            Trace.Flush();
            listener.Messages.Should().ContainSingle(message =>
                message.Contains("ConnectTool cleanup triggered", StringComparison.Ordinal) &&
                message.Contains("12345", StringComparison.Ordinal));
            SpinWait.SpinUntil(() => !sessionManager.HasSession(12345), TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    public void Dispose()
    {
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
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
        private readonly object _gate = new();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return _messages.ToArray();
                }
            }
        }

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                lock (_gate)
                {
                    _messages.Add(message);
                }
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                lock (_gate)
                {
                    _messages.Add(message);
                }
            }
        }
    }
}
