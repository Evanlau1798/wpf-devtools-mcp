using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class DllPathValidatorTestSeamTests
{
    [Fact]
    public async Task TestingOverrides_ShouldNotLeakIntoSuppressedExecutionContext()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousValidatedSigner = DllPathValidator.ValidatedSignerOverrideForTesting;
        var previousCurrentProcessSigner = DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;

        try
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ => 0;
            DllPathValidator.ValidatedSignerOverrideForTesting = _ => CreateTestSigner();
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = CreateTestSigner();
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = true;

            Task<OverrideSnapshot> task;
            using (ExecutionContext.SuppressFlow())
            {
                task = Task.Run(CaptureOverrides);
            }

            var snapshot = await task;

            snapshot.HasWinVerifyTrustOverride.Should().BeFalse();
            snapshot.HasValidatedSignerOverride.Should().BeFalse();
            snapshot.HasCurrentProcessSignerOverride.Should().BeFalse();
            snapshot.TrustedLocalDevelopmentBuildOverride.Should().BeNull();
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.ValidatedSignerOverrideForTesting = previousValidatedSigner;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = previousCurrentProcessSigner;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
        }
    }

    private static OverrideSnapshot CaptureOverrides()
        => new(
            DllPathValidator.WinVerifyTrustOverrideForTesting is not null,
            DllPathValidator.ValidatedSignerOverrideForTesting is not null,
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting is not null,
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting);

    private static DllPathValidator.ValidatedAuthenticodeSigner CreateTestSigner()
        => new(
            "1111111111111111111111111111111111111111",
            "CN=WpfDevTools Test Signer",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

    private sealed record OverrideSnapshot(
        bool HasWinVerifyTrustOverride,
        bool HasValidatedSignerOverride,
        bool HasCurrentProcessSignerOverride,
        bool? TrustedLocalDevelopmentBuildOverride);
}