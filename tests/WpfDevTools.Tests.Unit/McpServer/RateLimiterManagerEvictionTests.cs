using System.Collections;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class RateLimiterManagerEvictionTests
{
    [Fact]
    public void EvictOldestEntries_ShouldUseBoundedPriorityQueueInsteadOfRepeatedSorts()
    {
        var source = File.ReadAllText(TestSupport.TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/RateLimiter.cs"));
        var methodBody = ExtractMethodBody(source, "private void EvictOldestEntries");

        methodBody.Should().Contain("PriorityQueue<");
        methodBody.Should().NotContain(".Sort(");
    }

    [Fact]
    public void TryAcquire_WhenEntryLimitIsExceeded_ShouldEvictLeastRecentlyAccessedSession()
    {
        using var manager = new RateLimiterManager(maxRequestsPerMinute: 2);

        for (var processId = 1; processId <= 1000; processId++)
        {
            manager.TryAcquire(processId).Should().BeTrue();
        }

        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var processId = 1; processId <= 1000; processId++)
        {
            SetLastAccessed(manager, processId, baseTime.AddSeconds(processId));
        }

        manager.TryAcquire(1001).Should().BeTrue();

        manager.GetAvailableTokens(1).Should().Be(2, "process 1 was the oldest entry and should be evicted");
        manager.GetAvailableTokens(2).Should().Be(1, "newer entries should remain in the manager");
    }

    private static void SetLastAccessed(
        RateLimiterManager manager,
        int processId,
        DateTimeOffset lastAccessed)
    {
        var limiters = GetLimiters(manager);
        limiters.Contains(processId).Should().BeTrue();

        var entry = limiters[processId];
        entry.Should().NotBeNull();

        var lastAccessedProperty = entry!.GetType().GetProperty(
            "LastAccessed",
            BindingFlags.Instance | BindingFlags.Public);
        lastAccessedProperty.Should().NotBeNull();

        lastAccessedProperty!.SetValue(entry, lastAccessed);
    }

    private static IDictionary GetLimiters(RateLimiterManager manager)
    {
        var field = typeof(RateLimiterManager).GetField(
            "_limiters",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var value = field!.GetValue(manager);
        value.Should().BeAssignableTo<IDictionary>();

        return (IDictionary)value!;
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        signatureStart.Should().BeGreaterThanOrEqualTo(0);

        var bodyStart = source.IndexOf('{', signatureStart);
        bodyStart.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for {signature}.");
    }
}