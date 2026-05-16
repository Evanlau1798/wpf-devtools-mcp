using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SignaturePolicyDecisionTests
{
    // Policy contract: trusted-root-only model.
    // Release builds always verify; trusted non-Release workspace builds require explicit opt-in before skipping.
    [Fact]
    public void Evaluate_ReleaseBuild_ShouldAlwaysVerify()
    {
        var result = SignaturePolicy.Evaluate(isDebugBuild: false);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "RELEASE builds must always verify signatures");
    }

    [Fact]
    public void Evaluate_DebugBuild_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(isDebugBuild: true);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "DEBUG builds skip verification (path already validated as trusted root)");
    }

    [Fact]
    public void Evaluate_ShouldPreserveTwoParameterOverloadForBinaryCompatibility()
    {
        var overload = typeof(SignaturePolicy).GetMethod(
            nameof(SignaturePolicy.Evaluate),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(bool), typeof(bool)],
            modifiers: null);

        overload.Should().NotBeNull(
            "existing compiled callers still bind to the original two-parameter SignaturePolicy.Evaluate overload");
    }

    [Fact]
    public void Evaluate_TrustedLocalDevelopmentBuildWithoutExplicitOptIn_ShouldVerify()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true,
            isTrustedLocalDevelopmentSkipOptIn: false);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "trusted non-Release workspace builds must not auto-skip signature verification without an explicit opt-in");
    }

    [Fact]
    public void Evaluate_TrustedLocalDevelopmentBuildWithExplicitOptIn_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true,
            isTrustedLocalDevelopmentSkipOptIn: true);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "trusted non-Release workspace builds may skip verification only after an explicit local opt-in");
    }

    [Fact]
    public void GetRevocationMode_DebugBuild_ShouldReturnOffline()
    {
        var mode = SignaturePolicy.GetRevocationMode(isDebugBuild: true);

        mode.Should().Be(X509RevocationMode.Offline,
            "DEBUG builds must use Offline revocation to prevent network blocking during development");
    }

    [Fact]
    public void GetRevocationMode_ReleaseBuild_ShouldReturnOnline()
    {
        var mode = SignaturePolicy.GetRevocationMode(isDebugBuild: false);

        mode.Should().Be(X509RevocationMode.Online,
            "RELEASE builds must use Online revocation for maximum security");
    }

    [Fact]
    public void GetRevocationMode_ShouldPreserveTwoParameterOverloadForBinaryCompatibility()
    {
        var overload = typeof(SignaturePolicy).GetMethod(
            nameof(SignaturePolicy.GetRevocationMode),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(bool), typeof(bool)],
            modifiers: null);

        overload.Should().NotBeNull(
            "existing compiled callers still bind to the original two-parameter SignaturePolicy.GetRevocationMode overload");
    }

    [Fact]
    public void GetRevocationMode_TrustedLocalDevelopmentBuildWithoutExplicitOptIn_ShouldReturnOnline()
    {
        var mode = SignaturePolicy.GetRevocationMode(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true,
            isTrustedLocalDevelopmentSkipOptIn: false);

        mode.Should().Be(X509RevocationMode.Online,
            "trusted local workspace builds must keep production-style revocation checks until an explicit opt-in is present");
    }

    [Fact]
    public void GetRevocationMode_TrustedLocalDevelopmentBuildWithExplicitOptIn_ShouldReturnOffline()
    {
        var mode = SignaturePolicy.GetRevocationMode(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true,
            isTrustedLocalDevelopmentSkipOptIn: true);

        mode.Should().Be(X509RevocationMode.Offline,
            "trusted local workspace builds may avoid production-style revocation checks only after an explicit opt-in");
    }
}
