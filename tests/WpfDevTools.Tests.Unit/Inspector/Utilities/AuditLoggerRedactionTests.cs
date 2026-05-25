using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Utilities;
using WpfDevTools.Tests.Unit.Execution;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

[Collection("TraceState")]
public sealed class AuditLoggerRedactionTests
{
    [Fact]
    public void TraceAuditLogger_ShouldRedactSensitiveMessages()
    {
        var logger = new TraceAuditLogger();
        using var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            logger.Log(
                "Security",
                @"password=cert-password windowTitle=Payroll C:\Temp\WpfDevTools_AuthSecret_1234_abcd.txt",
                AuditSeverity.Warning);
            Trace.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        listener.Messages.Should().ContainSingle();
        listener.Messages[0].Should().NotContain("cert-password");
        listener.Messages[0].Should().NotContain("Payroll");
        listener.Messages[0].Should().NotContain("WpfDevTools_AuthSecret_1234_abcd.txt");
        listener.Messages[0].Should().Contain(SensitiveLogRedactor.RedactedValue);
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Messages.Add(message);
            }
        }
    }
}
