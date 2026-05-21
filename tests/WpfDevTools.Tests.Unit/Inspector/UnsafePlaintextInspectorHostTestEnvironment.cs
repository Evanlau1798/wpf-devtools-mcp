using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Tests.Unit.Inspector;

internal static class UnsafePlaintextInspectorHostTestEnvironment
{
    internal static IDisposable BeginScope() =>
        InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => true);
}
