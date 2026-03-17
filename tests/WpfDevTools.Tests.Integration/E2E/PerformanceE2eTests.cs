using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP performance tools (get_render_stats, get_visual_count, find_binding_leaks).
/// Validates performance monitoring through the full MCP protocol pipeline.
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class PerformanceE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerformanceE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetRenderStats_ShouldReturnFrameRateAndVisualCount()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_render_stats",
            new { processId = _fixture.TestAppProcessId },
            timeoutMs: 10000);

        _output.WriteLine($"Render stats: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        // First call may return zeros due to warm-up; subsequent calls should have data
        result.TryGetProperty("frameRate", out _).Should().BeTrue(
            "render stats should include frameRate field");
        result.TryGetProperty("totalFrames", out _).Should().BeTrue(
            "render stats should include totalFrames field");
        result.TryGetProperty("visualCount", out _).Should().BeTrue(
            "render stats should include visualCount field");
        result.TryGetProperty("isWarmedUp", out _).Should().BeTrue(
            "render stats should expose warm-up state explicitly");
        result.TryGetProperty("confidence", out _).Should().BeTrue(
            "render stats should include confidence for sample quality");
        result.TryGetProperty("sampleGuidance", out _).Should().BeTrue(
            "render stats should include sample guidance");
    }

    [Fact]
    public async Task GetRenderStats_SecondCall_ShouldHaveFrameData()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        // First call starts monitoring
        await _fixture.Client.CallToolAsync(
            "get_render_stats",
            new { processId = _fixture.TestAppProcessId },
            timeoutMs: 10000);

        // Brief wait for frame data to accumulate
        await Task.Delay(500);

        // Second call should have actual data
        var result = await _fixture.Client.CallToolAsync(
            "get_render_stats",
            new { processId = _fixture.TestAppProcessId },
            timeoutMs: 10000);

        _output.WriteLine($"Render stats (2nd call): {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var totalFrames = result.GetProperty("totalFrames").GetInt32();
        totalFrames.Should().BeGreaterThan(0,
            "after warm-up, frame data should be available");

        _output.WriteLine($"Total frames: {totalFrames}");
    }

    [Fact]
    public async Task GetRenderStats_WithWarmUp_ShouldReturnFrameDataOnFirstCall()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_render_stats",
            new { processId = _fixture.TestAppProcessId, warmUp = true },
            timeoutMs: 10000);

        _output.WriteLine($"Render stats (warm-up): {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warmUpApplied").GetBoolean().Should().BeTrue();
        result.GetProperty("isWarmedUp").GetBoolean().Should().BeTrue();
        result.GetProperty("sampleCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("totalFrames").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FindBindingLeaks_ShouldReturnLeakReport()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "find_binding_leaks",
            new { processId = _fixture.TestAppProcessId, threshold = 100 });

        _output.WriteLine($"Binding leaks: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("hasLeaks", out _).Should().BeTrue(
            "leak report should include hasLeaks indicator");
        result.TryGetProperty("totalTracked", out _).Should().BeTrue(
            "leak report should include totalTracked count");
        result.TryGetProperty("suspects", out _).Should().BeTrue(
            "leak report should include contract-facing suspects array");
        result.TryGetProperty("confidence", out _).Should().BeTrue(
            "leak report should include confidence for sampling quality");
        result.TryGetProperty("minimumRecommendedSamplingDurationMs", out _).Should().BeTrue(
            "leak report should include minimum recommended sampling duration");
    }

    [Fact]
    public async Task FindBindingLeaks_WithWarmUp_ShouldUseRecommendedSamplingWindow()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "find_binding_leaks",
            new { processId = _fixture.TestAppProcessId, threshold = 100, warmUp = true },
            timeoutMs: 15000);

        _output.WriteLine($"Binding leaks (warm-up): {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warmUpApplied").GetBoolean().Should().BeTrue();
        result.GetProperty("samplingDurationMs").GetInt32().Should().Be(
            result.GetProperty("minimumRecommendedSamplingDurationMs").GetInt32());
    }

    [Fact]
    public async Task MeasureElementRenderTime_OnRoot_ShouldReturnTiming()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "measure_element_render_time",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Render time: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("renderTimeMs", out var renderTime).Should().BeTrue(
            "should include renderTimeMs field");

        renderTime.GetDouble().Should().BeGreaterOrEqualTo(0,
            "render time should be a non-negative number");
    }
}
