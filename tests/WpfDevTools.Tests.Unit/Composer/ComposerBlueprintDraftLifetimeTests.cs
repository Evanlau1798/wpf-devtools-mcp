using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Drafts;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintDraftLifetimeTests
{
    [Fact]
    public void DefaultLifetime_ShouldCoverLongInteractiveAuthoringSessions()
    {
        var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        var store = new BlueprintDraftStore(utcNow: () => now);

        var created = store.Create("{}");
        now = now.AddHours(2);

        created.ExpiresAt.Should().Be(new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero));
        store.Resolve(created.DraftRef!).Success.Should().BeTrue();
    }
}
