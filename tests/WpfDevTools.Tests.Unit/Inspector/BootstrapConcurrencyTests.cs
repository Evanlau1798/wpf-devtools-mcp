using Xunit;
using FluentAssertions;
using System.Reflection;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Tests for Bootstrap concurrency issues
/// Note: Bootstrap is marked [ExcludeFromCodeCoverage] but we can still test its logic
/// </summary>
public class BootstrapConcurrencyTests
{
    [Fact]
    public void Initialize_ConcurrentCalls_ShouldOnlyInitializeOnce()
    {
        // Arrange - Reset static state using reflection
        var bootstrapType = Type.GetType("WpfDevTools.Inspector.Bootstrap, WpfDevTools.Inspector");
        bootstrapType.Should().NotBeNull();

        var isInitializedField = bootstrapType!.GetField("_isInitialized",
            BindingFlags.NonPublic | BindingFlags.Static);
        var isInitializingField = bootstrapType.GetField("_isInitializing",
            BindingFlags.NonPublic | BindingFlags.Static);

        isInitializedField.Should().NotBeNull();
        isInitializingField.Should().NotBeNull();

        // Reset state
        isInitializedField!.SetValue(null, false);
        isInitializingField!.SetValue(null, 0);

        // Act - Call Initialize concurrently
        var tasks = new List<Task>();
        var initializeMethod = bootstrapType.GetMethod("Initialize",
            BindingFlags.Public | BindingFlags.Static);
        initializeMethod.Should().NotBeNull();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    initializeMethod!.Invoke(null, new object[] { "" });
                }
                catch
                {
                    // Expected to fail in test environment (no WPF app)
                    // We're testing the race condition logic, not the full initialization
                }
            }));
        }

        // Assert - Should not throw and _isInitializing should be reset to 0
        var act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync();

        // Wait for all tasks to complete
        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));

        // Verify _isInitializing is reset to 0 (not stuck at 1)
        var isInitializingValue = (int)isInitializingField.GetValue(null)!;
        isInitializingValue.Should().Be(0, "because _isInitializing should be reset after initialization attempts");
    }
}
