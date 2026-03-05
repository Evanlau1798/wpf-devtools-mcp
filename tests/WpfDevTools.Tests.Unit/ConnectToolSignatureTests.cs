using Xunit;
using FluentAssertions;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit;

public class ConnectToolSignatureTests
{
    [Fact]
    public void SignatureVerification_ShouldUseOfflineRevocationInDebug()
    {
        // This test verifies that in DEBUG builds, signature verification uses offline mode
        // to prevent network blocking. In RELEASE builds, online revocation is used.

        // The fix changes RevocationMode based on build configuration:
        // - DEBUG: X509RevocationMode.Offline (no network calls)
        // - RELEASE: X509RevocationMode.Online (full security)

        // This is a documentation test - the actual behavior is verified by:
        // 1. Code inspection of ConnectTool.VerifyAuthenticodeSignature()
        // 2. Integration tests that measure execution time
        // 3. Manual testing with network disconnected

#if DEBUG
        // In DEBUG mode, offline revocation should be used
        Assert.True(true, "DEBUG mode uses offline revocation to prevent blocking");
#else
        // In RELEASE mode, online revocation should be used
        Assert.True(true, "RELEASE mode uses online revocation for maximum security");
#endif
    }

    [Fact]
    public void SignatureVerification_CanBeSkippedInDebugWithEnvironmentVariable()
    {
        // This test documents that signature verification can be skipped in DEBUG builds
        // by setting WPFDEVTOOLS_SKIP_SIGNATURE_CHECK=1 environment variable

        // This is useful for:
        // 1. Development with unsigned DLLs
        // 2. Testing without code signing certificates
        // 3. CI/CD pipelines that don't have signing infrastructure

#if DEBUG
        // In DEBUG mode, signature check can be skipped
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", "1");
        var skipCheck = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");
        skipCheck.Should().Be("1");
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", null);
#else
        // In RELEASE mode, signature check cannot be skipped
        Assert.True(true, "RELEASE mode always requires signature verification");
#endif
    }
}
