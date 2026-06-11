namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// Shared-session McpE2E tests that mutate app or UI state inherit this base so each test
/// starts and ends from the same TestApp baseline.
/// </summary>
public abstract class SharedStateMcpE2eTestBase : IAsyncLifetime
{
    private readonly Func<McpE2eFixture, Task> _resetSharedSessionStateAsync;

    protected SharedStateMcpE2eTestBase(McpE2eFixture fixture)
        : this(fixture, E2eTestHelpers.ResetSharedSessionStateAsync)
    {
    }

    internal SharedStateMcpE2eTestBase(
        McpE2eFixture fixture,
        Func<McpE2eFixture, Task> resetSharedSessionStateAsync)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(resetSharedSessionStateAsync);

        Fixture = fixture;
        _resetSharedSessionStateAsync = resetSharedSessionStateAsync;
    }

    protected McpE2eFixture Fixture { get; }

    public async Task InitializeAsync()
    {
        if (Fixture.SkipReason != null)
        {
            return;
        }

        E2eTestHelpers.AssertFixtureReady(Fixture);

        try
        {
            await _resetSharedSessionStateAsync(Fixture);
        }
        catch (Exception ex)
        {
            Fixture.Quarantine($"Shared E2E reset failed during test initialization: {ex.Message}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Fixture.SkipReason != null || Fixture.QuarantineReason != null)
        {
            return;
        }

        try
        {
            await _resetSharedSessionStateAsync(Fixture);
        }
        catch (Exception ex)
        {
            Fixture.Quarantine($"Shared E2E reset failed during test cleanup: {ex.Message}");
            throw;
        }
    }
}
