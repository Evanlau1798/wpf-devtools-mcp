using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public sealed class InspectorHostPipeSecurityTests
{
    [Fact]
    public void CreatePipeSecurityForCurrentUser_ShouldProtectDaclAndAvoidBroadPrincipals()
    {
        var security = InspectorHost.CreatePipeSecurityForCurrentUser();

        security.AreAccessRulesProtected.Should().BeTrue(
            "pipe ACLs must not inherit broader machine-level permissions");

        var currentUser = WindowsIdentity.GetCurrent().User;
        currentUser.Should().NotBeNull();

        var allowedSids = GetAllowedSids(security);
        allowedSids.Should().Contain(currentUser!);
        allowedSids.Should().Contain(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        allowedSids.Should().NotContain(new SecurityIdentifier(WellKnownSidType.WorldSid, null));
        allowedSids.Should().NotContain(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null));
        allowedSids.Should().NotContain(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
    }

    [Theory]
    [InlineData(WellKnownSidType.WorldSid)]
    [InlineData(WellKnownSidType.AuthenticatedUserSid)]
    [InlineData(WellKnownSidType.BuiltinUsersSid)]
    public void ValidatePipeSecurity_ShouldRejectBroadAllowPrincipal(WellKnownSidType sidType)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(sidType, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        var act = () => InspectorHost.ValidatePipeSecurityDoesNotGrantBroadPrincipals(security);

        act.Should().Throw<InvalidOperationException>();
    }

    private static SecurityIdentifier[] GetAllowedSids(PipeSecurity security)
    {
        return security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToArray();
    }
}
