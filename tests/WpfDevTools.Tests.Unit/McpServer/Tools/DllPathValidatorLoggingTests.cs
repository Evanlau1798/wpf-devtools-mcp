using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TraceState")]
public sealed class DllPathValidatorLoggingTests
{
    [Fact]
    public void ValidateDllPath_WhenSignatureVerificationIsSkipped_ShouldEmitSecurityWarning()
    {
        var trustedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
        using var listener = new CapturingTraceListener();

        Trace.Listeners.Add(listener);
        try
        {
            DllPathValidator.ValidateDllPath(trustedDllPath);
            Trace.Flush();

            listener.Events.Should().ContainSingle(
                entry => entry.Type == TraceEventType.Warning
                         && entry.Message.Contains("DLL signature verification skipped", StringComparison.Ordinal)
                         && entry.Message.Contains("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", StringComparison.Ordinal),
                "signature bypasses must be visible in diagnostics");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        private readonly List<TraceEntry> _events = new();

        public IReadOnlyList<TraceEntry> Events => _events;

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _events.Add(new TraceEntry(eventType, message));
            }
        }
    }

    private sealed record TraceEntry(TraceEventType Type, string Message);
}
