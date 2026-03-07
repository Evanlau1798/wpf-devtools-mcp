using FluentAssertions;
using WpfDevTools.Inspector;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class BootstrapBridgeTests
{
    [Fact]
    public void Run_ShouldHaveCorrectSignatureForExecuteInDefaultAppDomain()
    {
        // BootstrapBridge.Run must have signature: public static int Run(string)
        var method = typeof(BootstrapBridge).GetMethod("Run",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("BootstrapBridge.Run must exist for ExecuteInDefaultAppDomain");
        method!.ReturnType.Should().Be(typeof(int),
            "ExecuteInDefaultAppDomain requires int return type");

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void RunNative_ShouldExistWithUnmanagedCallersOnly()
    {
        var method = typeof(BootstrapBridge).GetMethod("RunNative",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("BootstrapBridge.RunNative must exist for hostfxr");
        method!.ReturnType.Should().Be(typeof(int));

#if NET8_0_OR_GREATER
        var attr = method.GetCustomAttributes(
            typeof(System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute), false);
        attr.Should().HaveCount(1,
            "RunNative must have [UnmanagedCallersOnly] for hostfxr integration");
#endif
    }

    [Fact]
    public void Run_EmptyParams_ShouldNotThrow()
    {
        // Run with empty params should handle gracefully.
        // Without a WPF Dispatcher, this returns -1 (exception caught) or 0.
        var result = BootstrapBridge.Run("");

        result.Should().BeOneOf(0, -1);
    }
}
