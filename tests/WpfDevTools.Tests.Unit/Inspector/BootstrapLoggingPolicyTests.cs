using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("BootstrapState")]
public sealed class BootstrapLoggingPolicyTests : IDisposable
{
    public BootstrapLoggingPolicyTests()
    {
        Bootstrap.ResetForTesting();
    }

    public void Dispose()
    {
        Bootstrap.ResetForTesting();
    }

    [Fact]
    public void LogError_WhenTempFileLoggingIsNotOptedIn_ShouldNotWriteFileEntry()
    {
        var writes = new List<string>();
        Bootstrap.FileLogOptInEvaluator = static () => false;
        Bootstrap.FileLogAppendAction = writes.Add;

        InvokeLogError("Bootstrap initialization failed: secret-token=abc123");

        writes.Should().BeEmpty(
            "bootstrap temp-file logging must be opt-in so injected failure details are not dumped to disk by default");
    }

    [Fact]
    public void LogError_WhenTempFileLoggingIsOptedIn_ShouldWriteRedactedFileEntry()
    {
        string? entry = null;
        Bootstrap.FileLogOptInEvaluator = static () => true;
        Bootstrap.FileLogAppendAction = value => entry = value;

        InvokeLogError("Bootstrap initialization failed: secret-token=abc123");

        entry.Should().NotBeNull();
        entry.Should().Contain("[ERROR]");
        entry.Should().Contain("secret-token=[redacted]");
        entry.Should().NotContain("abc123");
    }

    private static void InvokeLogError(string message)
    {
        var method = typeof(Bootstrap).GetMethod(
            "LogError",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [message]);
    }
}
