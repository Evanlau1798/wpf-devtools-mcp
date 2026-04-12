using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public sealed class DllInjectorSafetyContractTests
{
    [Fact]
    public void DllInjector_ShouldGuardRemoteMemoryCleanupBehindCompletedRemoteExecution()
    {
        var content = File.ReadAllText(
            WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Injector/Injection/DllInjector.cs"));

        content.Should().Contain("shouldFreeAllocatedMemory");
        content.Should().Contain("shouldFreeBootstrapParameters");
        content.Should().Contain("if (shouldFreeAllocatedMemory && allocatedMemory != IntPtr.Zero && hProcess != IntPtr.Zero)");
        content.Should().Contain("if (shouldFreeBootstrapParameters && remoteParamAddr != IntPtr.Zero)");
    }
}
