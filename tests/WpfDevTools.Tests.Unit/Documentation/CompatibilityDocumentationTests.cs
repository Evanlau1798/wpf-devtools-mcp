using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class CompatibilityDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/compatibility-matrix.md", "### Self-contained single-file WPF apps", "Raw injection path: Not supported.", "Overall support posture: Supported through SDK-host reuse.", "InspectorSdk.Initialize()")]
    [InlineData("docfx/production/compatibility-matrix.md", "### Native AOT", "Raw injection path: Not supported.", "Overall support posture: Not supported.", "SDK-hosted reuse is not a Native AOT workaround")]
    [InlineData("docfx/production/compatibility-matrix.md", "### Trimmed apps", "Raw injection path: Risky / partial.", "Overall support posture: Prefer SDK-host reuse.", "raw injection or inspector startup unreliable")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "### Self-contained single-file WPF app", "Raw injection 路徑：不支援。", "整體支援姿態：可透過 SDK-host reuse 支援。", "InspectorSdk.Initialize()")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "### Native AOT", "Raw injection 路徑：不支援。", "整體支援姿態：不支援。", "SDK-hosted reuse 不是 Native AOT workaround")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "### Trimmed app", "Raw injection 路徑：風險較高 / 部分支援。", "整體支援姿態：優先使用 SDK-host reuse。", "raw injection 或 inspector 啟動不穩定")]
    public void CompatibilityMatrix_ShouldLockScenarioSections(
        string relativePath,
        string scenarioHeading,
        string rawInjectionPhrase,
        string supportPhrase,
        string notePhrase)
    {
        var section = ReadMarkdownSection(relativePath, scenarioHeading, "### ");

        section.Should().Contain(rawInjectionPhrase,
            $"{relativePath} should preserve the documented raw-injection posture for {scenarioHeading}");
        section.Should().Contain(supportPhrase,
            $"{relativePath} should preserve the documented overall-support posture for {scenarioHeading}");
        section.Should().Contain(notePhrase,
            $"{relativePath} should preserve the scenario-specific mitigation or limitation note for {scenarioHeading}");
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
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md", "raw injection", "single-file WPF app support posture", "InspectorSdk.Initialize()", "SDK-hosted Inspector", "preferred fallback rather than a guarantee")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "raw injection is unavailable", "single-file WPF workflow", "InspectorSdk.Initialize()", "connect() can reuse the existing pipe-backed InspectorHost", "do not assume it restores full compatibility")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "single-file targets cannot use raw injection", "single-file WPF workflow available", "InspectorSdk.Initialize()", "preferred fallback, not a guarantee", "publish trimming may remove required types")]
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
            $"{relativePath} should mention Native AOT as a separate unsupported packaging/runtime boundary");
        content.Should().Contain("trimmed",
            $"{relativePath} should mention trimmed packaging when describing injection compatibility limits");
        content.Should().Contain(limitationPhrase,
            $"{relativePath} should frame these scenarios as raw-injection limits rather than blanket product incompatibility");
        content.Should().Contain(supportPhrase,
            $"{relativePath} should explain that SDK-host reuse is available for supported single-file targets without extending that claim to Native AOT");
        content.Should().Contain(sdkPhrase,
            $"{relativePath} should direct constrained targets toward InspectorSdk.Initialize()");
        content.Should().Contain(reusePhrase,
            $"{relativePath} should explain that SDK-host reuse remains available for constrained targets");
        content.Should().Contain(trimmedCaveatPhrase,
            $"{relativePath} should keep the trimmed-target caveat aligned with the compatibility matrix instead of overstating SDK-host fallback");
    }

    [Theory]
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md")]
    [InlineData("docfx/quickstart/sdk-hosted-inspector.md")]
    [InlineData("docfx/zh-tw/quickstart/sdk-hosted-inspector.md")]
    [InlineData("docfx/guides/troubleshooting.md")]
    [InlineData("docfx/zh-tw/guides/troubleshooting.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs")]
    public void SdkGuidance_ShouldNotClaimNativeAotSupport(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        var nativeAotLines = content.Split('\n')
            .Where(line => line.Contains("Native AOT", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToArray();

        nativeAotLines.Should().NotBeEmpty($"{relativePath} should document Native AOT as an explicit boundary");
        nativeAotLines.Should().OnlyContain(line =>
            !ContainsNativeAotSupportClaim(line),
            $"{relativePath} should not describe Native AOT as available through SDK-hosted reuse");
        nativeAotLines.Should().Contain(line =>
            line.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("不支援", StringComparison.Ordinal) ||
            line.Contains("not a Native AOT workaround", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("不是 Native AOT workaround", StringComparison.Ordinal),
            $"{relativePath} should explicitly keep Native AOT unsupported");
    }

    [Theory]
    [InlineData("README.md", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("docfx/production/compatibility-matrix.md", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse 透過 named pipes 通訊")]
    [InlineData("docfx/quickstart/index.md", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "Raw injection/bootstrapper fallback 必須符合架構", "SDK-hosted reuse 透過 named pipes 通訊")]
    [InlineData("docfx/guides/troubleshooting.md", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("docfx/zh-tw/guides/troubleshooting.md", "Raw injection/bootstrapper fallback 必須符合架構", "SDK-hosted reuse 透過 named pipes 通訊")]
    [InlineData("docfx/reference/error-model.md", "`ArchitectureMismatch` is an injection/bootstrapper error", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("docfx/zh-tw/reference/error-model.md", "`ArchitectureMismatch` 是 injection/bootstrapper error", "SDK-hosted reuse 透過 named pipes 通訊")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "Architecture matching is mandatory for raw injection/bootstrapper fallback", "SDK-hosted reuse communicates over named pipes")]
    public void ArchitectureGuidance_ShouldScopeBitnessRequirementToInjectionPath(
        string relativePath,
        string rawInjectionPhrase,
        string sdkPipePhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(rawInjectionPhrase);
        content.Should().Contain(sdkPipePhrase);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string ReadMarkdownSection(string relativePath, string heading, string nextHeadingPrefix)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var startIndex = content.IndexOf(heading, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should contain {heading}");

        var endIndex = content.IndexOf(
            Environment.NewLine + nextHeadingPrefix,
            startIndex + heading.Length,
            StringComparison.Ordinal);

        return endIndex > startIndex
            ? content[startIndex..endIndex]
            : content[startIndex..];
    }

    private static bool ContainsNativeAotSupportClaim(string line) =>
        line.Contains("Supported through SDK-host reuse", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("available through SDK", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("available through the target-side SDK host", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("prefer SDK-hosted", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("start the SDK host", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("InspectorSdk.Initialize()", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("可透過 SDK-host reuse 支援", StringComparison.Ordinal) ||
        line.Contains("優先使用 SDK-hosted", StringComparison.Ordinal) ||
        line.Contains("呼叫 `InspectorSdk.Initialize()`", StringComparison.Ordinal);
}
