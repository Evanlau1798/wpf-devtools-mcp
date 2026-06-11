using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapInjectionDocumentationTests
{
    [Fact]
    public void BootstrapAndInjectionDoc_ShouldScopeItselfToRawInjectionPath()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/production/bootstrap-and-injection.md"));
        var scopeSection = GetSection(content, "## Scope: raw injection path only", "## Why a bootstrapper exists");
        var whyBootstrapperIndex = content.IndexOf("## Why a bootstrapper exists", StringComparison.Ordinal);
        var scopeIndex = content.IndexOf("## Scope: raw injection path only", StringComparison.Ordinal);
        var sdkReuseIndex = scopeSection.IndexOf("compatible SDK-hosted Inspector", StringComparison.Ordinal);

        scopeSection.Should().Contain("raw injection path only",
            "the page should declare that it documents the bootstrapper-based injection path rather than the full support posture");
        scopeSection.Should().Contain("Compatibility Matrix",
            "the page should point packaging-constrained targets to the compatibility guidance");
        scopeSection.Should().Contain("InspectorSdk.Initialize()",
            "the page should direct SDK-hosted targets toward the SDK entrypoint instead of implying everything goes through injection");
        scopeIndex.Should().BeGreaterThan(-1,
            "the page should expose the raw-injection scope as a dedicated heading near the top");
        whyBootstrapperIndex.Should().BeGreaterThan(scopeIndex,
            "the scope clarification should appear before the bootstrapper rationale");
        sdkReuseIndex.Should().BeGreaterThan(scopeIndex,
            "the SDK-host reuse guidance should appear inside the initial scoping guidance, not later as an afterthought");
    }

    [Fact]
    public void BootstrapAndInjectionDoc_ShouldDescribeSdkReuseBeforeInjectionFallback()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/production/bootstrap-and-injection.md"));
        var flowSection = GetSection(content, "## High-level raw-injection flow", "## Success contract");
        var sdkReuseStepIndex = flowSection.IndexOf("2. The server first tries to reuse a compatible SDK-hosted Inspector", StringComparison.Ordinal);
        var validationStepIndex = flowSection.IndexOf("3. If SDK-host reuse is unavailable, the server validates the process and candidate DLL paths.", StringComparison.Ordinal);

        content.Should().Contain("High-level raw-injection flow",
            "the page should label the flow as the injection fallback path");
        flowSection.Should().Contain("reuse a compatible SDK-hosted Inspector",
            "the page should describe SDK-host reuse before the bootstrapper injection fallback");
        GetSection(content, "## Success contract", "## Architecture rule").Should().Contain("injection fallback path",
            "the page should explicitly scope the remaining success contract to the fallback injection path");
        content.Should().NotContain("## High-level connect flow",
            "the page should not drift back to a generic connect flow heading that implies injection-first behavior");
        sdkReuseStepIndex.Should().BeGreaterThan(-1,
            "the flow should explicitly mention SDK-host reuse");
        validationStepIndex.Should().BeGreaterThan(sdkReuseStepIndex,
            "the flow should attempt SDK-host reuse before the injection validation and bootstrapper steps");
    }

    private static string GetSection(string content, string startHeading, string endHeading)
    {
        var startIndex = content.IndexOf(startHeading, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThan(-1, $"expected heading {startHeading} to exist");

        var endIndex = content.IndexOf(endHeading, startIndex + startHeading.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"expected heading {endHeading} to appear after {startHeading}");

        return content[startIndex..endIndex];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}