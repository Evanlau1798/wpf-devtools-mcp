using System.Security.Principal;
using FluentAssertions;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class CertificateStorageSecurityTests
{
    [Fact]
    public void IsNetworkPath_ShouldRejectUncPrefixes()
    {
        CertificateStorageSecurity.IsNetworkPath(@"\\server\share\certs").Should().BeTrue();
        CertificateStorageSecurity.IsNetworkPath(@"\\?\UNC\server\share\certs").Should().BeTrue();
        CertificateStorageSecurity.IsNetworkPath(@"\\?\C:\certs").Should().BeFalse();
        CertificateStorageSecurity.IsNetworkPath(@"C:\certs").Should().BeFalse();
    }

    [Fact]
    public void ResolveAndValidateLocalPath_ShouldRejectMappedNetworkDrives()
    {
        var act = () => CertificateStorageSecurity.ResolveAndValidateLocalPath(
            @"Z:\certs",
            "certDirectory",
            static _ => DriveType.Network);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*local path*");
    }

    [Fact]
    public void ResolveAndValidateLocalPath_ShouldRejectReparsePointChains()
    {
        static string NormalizeLookupPath(string path)
        {
            return path.Length > 3
                ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : path;
        }

        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\",
            @"C:\safe",
            @"C:\safe\link"
        };
        var attributes = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\"] = FileAttributes.Directory,
            [@"C:\safe"] = FileAttributes.Directory,
            [@"C:\safe\link"] = FileAttributes.Directory | FileAttributes.ReparsePoint
        };

        CertificateStorageSecurity.ContainsReparsePointInPathChain(
            @"C:\safe\link\certs",
            path => existingPaths.Contains(NormalizeLookupPath(path)),
            path => attributes[NormalizeLookupPath(path)]).Should().BeTrue();

        var act = () => CertificateStorageSecurity.ResolveAndValidateLocalPath(
            @"C:\safe\link\certs",
            "certDirectory",
            static _ => DriveType.Fixed,
            path => existingPaths.Contains(NormalizeLookupPath(path)),
            path => attributes[NormalizeLookupPath(path)]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*symbolic links*reparse points*");
    }

    [Fact]
    public void IsTrustedOwner_ShouldAllowCurrentUserSystemAndAdministratorsOnly()
    {
        var currentUserSid = WindowsIdentity.GetCurrent().User;
        currentUserSid.Should().NotBeNull();

        CertificateStorageSecurity.IsTrustedOwner(currentUserSid, currentUserSid!).Should().BeTrue();
        CertificateStorageSecurity.IsTrustedOwner(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            currentUserSid!).Should().BeTrue();
        CertificateStorageSecurity.IsTrustedOwner(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            currentUserSid!).Should().BeTrue();
        CertificateStorageSecurity.IsTrustedOwner(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            currentUserSid!).Should().BeFalse();
    }

    [Theory]
    [InlineData(WellKnownSidType.WorldSid)]
    [InlineData(WellKnownSidType.AuthenticatedUserSid)]
    [InlineData(WellKnownSidType.BuiltinUsersSid)]
    [InlineData(WellKnownSidType.BuiltinGuestsSid)]
    [InlineData(WellKnownSidType.AnonymousSid)]
    [InlineData(WellKnownSidType.NetworkSid)]
    public void IsBroadWritePrincipal_ShouldRejectBroadWindowsPrincipals(WellKnownSidType sidType)
    {
        CertificateStorageSecurity.IsBroadWritePrincipal(new SecurityIdentifier(sidType, null)).Should().BeTrue();
    }
}