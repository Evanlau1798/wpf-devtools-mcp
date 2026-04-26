using FluentAssertions;
using WpfDevTools.Inspector;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("BootstrapState")]
public sealed class BootstrapInitializationStatusTests : IDisposable
{
    public BootstrapInitializationStatusTests()
    {
        Bootstrap.ResetForTesting();
    }

    public void Dispose()
    {
        Bootstrap.ResetForTesting();
    }

    [Fact]
    public void Initialize_WhenApplicationDispatcherIsUnavailable_ShouldExposeStructuredFailureStatus()
    {
        Bootstrap.DispatcherResolver = static () => null;

        Bootstrap.Initialize($"pipeName=WpfDevTools_Status_{Guid.NewGuid():N}");

        var status = Bootstrap.LastInitializationStatus;
        status.State.Should().Be("Failed");
        status.IsInitialized.Should().BeFalse();
        status.ErrorCode.Should().Be("BootstrapDispatcherUnavailable");
        status.ErrorType.Should().Be(nameof(InvalidOperationException));
        status.ErrorMessage.Should().Contain("WPF Application");
        status.Hint.Should().Contain("target-side WPF dispatcher");
    }
}
