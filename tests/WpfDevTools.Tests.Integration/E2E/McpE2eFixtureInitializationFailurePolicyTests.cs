using System.ComponentModel;
using System.IO;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpE2eFixtureInitializationFailurePolicyTests
{
    [Theory]
    [MemberData(nameof(ExpectedEnvironmentFailures))]
    public void ShouldConvertInitializationFailureToSkip_ShouldAllowExpectedEnvironmentFailures(Exception exception)
    {
        McpE2eFixture.ShouldConvertInitializationFailureToSkip(exception)
            .Should().BeTrue("expected environment startup failures may skip E2E suites without hiding product regressions");
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public void ShouldConvertInitializationFailureToSkip_ShouldRejectUnexpectedFailures(Exception exception)
    {
        McpE2eFixture.ShouldConvertInitializationFailureToSkip(exception)
            .Should().BeFalse("unexpected fixture failures should fail visibly instead of being converted to SkipReason");
    }

    public static TheoryData<Exception> ExpectedEnvironmentFailures() =>
        new()
        {
            new TimeoutException("TestApp main window did not appear."),
            new IOException("Process executable could not be started."),
            new Win32Exception(5, "Access denied while starting a child process."),
            new UnauthorizedAccessException("Access denied while starting a child process.")
        };

    public static TheoryData<Exception> UnexpectedFailures() =>
        new()
        {
            new OutOfMemoryException("fatal"),
            new BadImageFormatException("bad image"),
            new InvalidOperationException("connect returned success=false"),
            new ArgumentException("bad fixture wiring")
        };
}
