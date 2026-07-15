namespace WpfDevTools.Mcp.Server.McpTools;

internal static class PerformanceMcpToolDescriptions
{
    private const string PerformanceMetadata = "CATEGORY: Performance\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetRenderStats =
        "Use this tool to inspect WPF render statistics when runtime UI performance feels slow.\n\n" +
        PerformanceMetadata + "Get render statistics from a WPF application. Returns frame rate, " +
        "render time, dirty region count, and other WPF rendering pipeline metrics.\n\n" +
        "USE WHEN: UI feels slow or laggy; investigating rendering performance issues.\n" +
        "DO NOT USE: For memory leaks (use find_binding_leaks instead).\n\n" +
        "PERFORMANCE: This tool measures rendering metrics over a short period (1-2 seconds).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - isWarmedUp: boolean,\n" +
        "  - confidence: 'low'|'medium'|'high',\n" +
        "  - warmUpApplied: boolean,\n" +
        "  - minimumRecommendedSampleCount: number,\n" +
        "  - minimumRecommendedMonitoringDurationMs: number,\n" +
        "  - sampleGuidance: string,\n" +
        "  - sampleCount: number,\n" +
        "  - sampleWindowSize: number,\n" +
        "  - frameRate: number,\n" +
        "  - avgRenderTime: number (ms),\n" +
        "  - dirtyRegionCount: number,\n" +
        "  - totalFrames: number,\n" +
        "  - monitoringDuration: number,\n" +
        "  - visualCount: number,\n" +
        "  - visualCountLimit: number,\n" +
        "  - visualCountTruncated: boolean\n\n" +
        "NOTE: The first call may return zeros with a 'Monitoring started' message because the render stats " +
        "listener needs time to collect data. Set warmUp=true to wait for a baseline sample window on the same call, or call again after 1-2 seconds.\n\n";

    public const string FindBindingLeaks =
        "Use this tool to inspect suspected WPF binding leaks in long-lived runtime sessions.\n\n" +
        PerformanceMetadata + "Detect potential binding memory leaks by tracking live binding references. " +
        "Threshold is the minimum number of live bindings on a single element to flag as suspicious.\n\n" +
        "USE WHEN: Memory usage grows over time; suspecting binding-related memory leaks.\n" +
        "DO NOT USE: On apps with legitimately many bindings per element (e.g., data grids).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - confidence: 'low'|'medium'|'high',\n" +
        "  - warmUpApplied: boolean,\n" +
        "  - samplingDurationMs: number,\n" +
        "  - minimumRecommendedSamplingDurationMs: number,\n" +
        "  - sampleGuidance: string,\n" +
        "  - totalTracked: number,\n" +
        "  - aliveBindings: number,\n" +
        "  - deadBindings: number,\n" +
        "  - threshold: number,\n" +
        "  - hasLeaks: boolean,\n" +
        "  - suspects: [{\n" +
        "    - elementId, elementType, bindingCount: number\n" +
        "  - potentialLeaks: [{ type, hashCode, toString }]\n\n" +
        "Empty suspects array means no leak candidates crossed the threshold. potentialLeaks retains raw diagnostic samples for backward compatibility. " +
        "Use samplingDurationMs>=3000 for higher-confidence leak diagnostics, or set warmUp=true to automatically use the minimum recommended sampling window when one is not supplied.\n\n";

    public const string MeasureElementRenderTime =
        "Use this tool to measure WPF element render time for targeted runtime performance diagnosis.\n\n" +
        PerformanceMetadata + "Measure the render time of a WPF element in milliseconds. " +
        "Forces a re-render and measures the time taken.\n\n" +
        "USE WHEN: Identifying slow-rendering elements; profiling UI performance.\n" +
        "DO NOT USE: Repeatedly in a loop (causes performance overhead).\n\n" +
        "PERFORMANCE: This tool forces a re-render, which may briefly impact UI responsiveness.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - renderTimeMs: number,\n" +
        "  - confidence: 'low',\n" +
        "  - recommendedSampleCount: number,\n" +
        "  - sampleGuidance: string,\n" +
        "  - elementId\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n\n";

    public const string GetVisualCount =
        "Use this tool to count WPF visual elements in a runtime subtree and detect UI complexity hot spots.\n\n" +
        PerformanceMetadata + "Get the count of visual elements in a WPF element subtree. " +
        "High counts (>5000) may indicate performance issues.\n\n" +
        "USE WHEN: UI feels slow; need to identify overly complex subtrees.\n" +
        "DO NOT USE: For memory usage (use find_binding_leaks instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - count: number,\n" +
        "  - elementId\n\n" +
        "- Guideline: <1000 = good, 1000-5000 = acceptable, >5000 = potential performance issue.\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n\n";
}
