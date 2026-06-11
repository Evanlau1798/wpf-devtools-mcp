using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TraceState")]
public sealed class SwallowedExceptionLoggingTests
{
    [Fact]
    public void TryNormalizeAbsolutePath_WhenResolverThrows_ShouldTraceWarning()
    {
        using var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var result = RawInjectionTargetPolicy.TryNormalizeAbsolutePath(
                @"C:\target\TestApp.exe",
                (Func<string, string?>)(_ => throw new IOException("resolver failed")),
                out _);
            Trace.Flush();

            result.Should().BeFalse();
            listener.Messages.Should().ContainSingle(message =>
                message.Contains("RawInjectionTargetPolicy path normalization failed", StringComparison.Ordinal)
                && message.Contains("resolver failed", StringComparison.Ordinal));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void ConnectToolSdkOnlyPackagingHeuristic_ShouldLogCatchAllExceptions()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.AutoDiscovery.cs"));

        source.Should().Contain("ConnectTool SDK-only packaging heuristic failed",
            "catch-all auto-discovery heuristics must leave a diagnostic trace before returning false");
    }

    [Fact]
    public void IsLikelySdkOnlyPackaging_ShouldStillReturnFalseForNonNetCoreProcesses()
    {
        var result = ConnectTool.IsLikelySdkOnlyPackaging(new WpfProcessInfo
        {
            ProcessId = 1,
            ProcessName = "LegacyApp",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetFramework,
            IsWpfApplication = true,
            ExecutablePath = @"C:\target\LegacyApp.exe"
        });

        result.Should().BeFalse();
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _messages.Add(message);
            }
        }
    }
}
