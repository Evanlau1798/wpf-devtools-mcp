using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

[Collection("SecurityState")]
public sealed class LocalSecretProtectorTests
{
    [Fact]
    public void Protect_WhenCurrentUserDpapiFails_ShouldFailClosedByDefault()
    {
        using var scope = LocalSecretProtector.BeginTestScope(
            (_, scope) => scope == DataProtectionScope.CurrentUser
                ? throw new CryptographicException("CurrentUser unavailable")
                : [1, 2, 3],
            localMachineFallbackAllowed: () => false);

        var act = () => LocalSecretProtector.Protect([42]);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*CurrentUser unavailable*");
    }

    [Fact]
    public void Protect_WhenLocalMachineFallbackIsExplicitlyAllowed_ShouldMarkPayloadScope()
    {
        using var scope = LocalSecretProtector.BeginTestScope(
            (_, dpapiScope) => dpapiScope == DataProtectionScope.CurrentUser
                ? throw new CryptographicException("CurrentUser unavailable")
                : [1, 2, 3],
            localMachineFallbackAllowed: () => true);

        var protectedBytes = LocalSecretProtector.Protect([42]);

        System.Text.Encoding.ASCII.GetString(protectedBytes)
            .Should().StartWith("WPFDEVTOOLS-DPAPI:LocalMachine\n");
    }

    [Fact]
    public void Unprotect_WhenLocalMachinePayloadIsNotExplicitlyAllowed_ShouldFailClosed()
    {
        using var scope = LocalSecretProtector.BeginTestScope(
            (_, _) => throw new InvalidOperationException("Protect should not be called."),
            localMachineFallbackAllowed: () => false);
        var protectedBytes = CreateLocalMachinePayload([42]);

        var act = () => LocalSecretProtector.Unprotect(protectedBytes);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*LocalMachine*disabled*");
    }

    [Fact]
    public void Unprotect_WhenLocalMachinePayloadIsExplicitlyAllowed_ShouldReadLegacyPayload()
    {
        using var scope = LocalSecretProtector.BeginTestScope(
            (_, _) => throw new InvalidOperationException("Protect should not be called."),
            localMachineFallbackAllowed: () => true);
        var protectedBytes = CreateLocalMachinePayload([42]);

        var plaintext = LocalSecretProtector.Unprotect(protectedBytes);

        plaintext.Should().Equal(42);
    }

    private static byte[] CreateLocalMachinePayload(byte[] plaintext)
    {
        var header = Encoding.ASCII.GetBytes("WPFDEVTOOLS-DPAPI:LocalMachine\n");
        var payload = ProtectedData.Protect(plaintext, null, DataProtectionScope.LocalMachine);
        var result = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(payload, 0, result, header.Length, payload.Length);
        return result;
    }
}
