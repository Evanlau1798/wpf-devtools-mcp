using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class CompatibilityDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/compatibility-matrix.md", "| Self-contained single-file WPF apps | Not supported | Supported through SDK-host reuse |", "InspectorSdk.Initialize()")]
    [InlineData("docfx/production/compatibility-matrix.md", "| Native AOT | Not supported | Supported through SDK-host reuse |", "target-side SDK host")]
    [InlineData("docfx/production/compatibility-matrix.md", "| Trimmed apps | Risky / partial | Prefer SDK-host reuse |", "raw injection or inspector startup unreliable")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "| Self-contained single-file WPF app | 不支援 | 可透過 SDK-host reuse 支援 |", "InspectorSdk.Initialize()")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "| Native AOT | 不支援 | 可透過 SDK-host reuse 支援 |", "target-side SDK host")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "| Trimmed app | 風險較高 / 部分支援 | 優先使用 SDK-host reuse |", "raw injection 或 inspector 啟動不穩定")]
    public void CompatibilityMatrix_ShouldLockScenarioRows(string relativePath, string expectedRow, string notePhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedRow,
            $"{relativePath} should preserve the documented raw-injection versus overall-support posture for this scenario row");
        content.Should().Contain(notePhrase,
            $"{relativePath} should preserve the scenario-specific mitigation or limitation note for this row");
    }

    [Theory]
    [InlineData("docfx/production/compatibility-matrix.md", "Raw injection path", "Overall support posture", "Supported through SDK-host reuse", "InspectorSdk.Initialize()")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "Raw injection 路徑", "整體支援姿態", "可透過 SDK-host reuse 支援", "InspectorSdk.Initialize()")]
    public void CompatibilityMatrix_ShouldDistinguishInjectionLimitsFromOverallSupport(
        string relativePath,
        string injectionColumn,
        string supportColumn,
        string reusePhrase,
        string sdkPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(injectionColumn,
            $"{relativePath} should separate raw injection limits from the overall support posture");
        content.Should().Contain(supportColumn,
            $"{relativePath} should describe overall product support separately from the raw injection path");
        content.Should().Contain(reusePhrase,
            $"{relativePath} should point constrained packaging scenarios to SDK-host reuse");
        content.Should().Contain(sdkPhrase,
            $"{relativePath} should point constrained packaging scenarios to InspectorSdk.Initialize()");
    }

    [Theory]
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md", "raw injection", "overall WPF DevTools support posture", "InspectorSdk.Initialize()", "SDK-hosted Inspector", "preferred fallback rather than a guarantee")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "raw injection is unavailable", "overall WPF DevTools workflow", "InspectorSdk.Initialize()", "connect() can reuse the existing pipe-backed InspectorHost", "do not assume it restores full compatibility")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "single-file and Native AOT targets cannot use raw injection", "overall WPF DevTools workflow available", "InspectorSdk.Initialize()", "preferred fallback, not a guarantee", "publish trimming may remove required types")]
    public void Guidance_ShouldDescribePackagingLimitsAsInjectionSpecific(
        string relativePath,
        string limitationPhrase,
        string supportPhrase,
        string sdkPhrase,
        string reusePhrase,
        string trimmedCaveatPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("single-file",
            $"{relativePath} should mention single-file packaging when describing injection compatibility limits");
        content.Should().Contain("Native AOT",
            $"{relativePath} should mention Native AOT when describing injection compatibility limits");
        content.Should().Contain("trimmed",
            $"{relativePath} should mention trimmed packaging when describing injection compatibility limits");
        content.Should().Contain(limitationPhrase,
            $"{relativePath} should frame these scenarios as raw-injection limits rather than blanket product incompatibility");
        content.Should().Contain(supportPhrase,
            $"{relativePath} should explain that the overall support posture remains available through SDK-host reuse");
        content.Should().Contain(sdkPhrase,
            $"{relativePath} should direct constrained targets toward InspectorSdk.Initialize()");
        content.Should().Contain(reusePhrase,
            $"{relativePath} should explain that SDK-host reuse remains available for constrained targets");
        content.Should().Contain(trimmedCaveatPhrase,
            $"{relativePath} should keep the trimmed-target caveat aligned with the compatibility matrix instead of overstating SDK-host fallback");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}