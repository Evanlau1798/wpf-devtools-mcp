using FluentAssertions;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public sealed class DllInjectorMemoryHygieneTests
{
    [Fact]
    public void Inject_ShouldZeroDllPathBytesAfterRemoteWrite()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new CapturingSuccessApi();
            var injector = new DllInjector(api, NoOpCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeTrue();
            api.LastWrittenBuffer.Should().NotBeNull();
            api.LastWrittenBuffer!.Should().OnlyContain(value => value == 0,
                "local DLL path bytes should be cleared after the remote write attempt completes");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void DllInjector_ShouldRouteEveryRemoteBufferThroughZeroingWrapper()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Injector/Injection/DllInjector.cs"));

        source.Should().Contain("CryptographicOperations.ZeroMemory(parameterBytes)");
        CountOccurrences(source, "_bufferInvoker.Invoke(").Should().Be(1,
            "all local injection byte buffers should flow through the wrapper that zeroes them in finally");
    }

    [Fact]
    public void InjectAndCallExport_ShouldCheckExportTimeoutBeforeEncodingBootstrapParameters()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Injector/Injection/DllInjector.cs"));

        var encodeIndex = source.IndexOf(
            "Encoding.Unicode.GetBytes(parameters + \"\\0\")",
            StringComparison.Ordinal);
        var timeoutIndex = source.IndexOf(
            "return InjectionMechanismFailure.InvokeBootstrapExportBudgetExhausted",
            StringComparison.Ordinal);

        encodeIndex.Should().BeGreaterThanOrEqualTo(0);
        timeoutIndex.Should().BeGreaterThanOrEqualTo(0);
        timeoutIndex.Should().BeLessThan(encodeIndex,
            "the export phase budget must be checked before materializing sensitive bootstrap parameter bytes");
    }

    private static string CreateExistingTempFile()
    {
        var filePath = Path.GetTempFileName();
        File.WriteAllText(filePath, "placeholder");
        return filePath;
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class CapturingSuccessApi : IRemoteInjectionApi
    {
        public byte[]? LastWrittenBuffer { get; private set; }

        public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId) => new(0x1000);

        public IntPtr GetModuleHandle(string moduleName) => new(0x2000);

        public IntPtr GetProcAddress(IntPtr moduleHandle, string procName) => new(0x3000);

        public IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address, uint size, uint allocationType, uint protect)
            => new(0x4000);

        public bool WriteProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint size, out int bytesWritten)
        {
            LastWrittenBuffer = buffer;
            bytesWritten = buffer.Length;
            return true;
        }

        public IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId) => new(0x5000);

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) =>
            RemoteThreadInvocationResult.WaitObject0;

        public bool CloseHandle(IntPtr handle) => true;

        public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType) => true;

        public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
        {
            exitCode = 0;
            return true;
        }

        public int GetLastError() => 0;
    }

    private sealed class NoOpCleanupScheduler : IRemoteAllocationCleanupScheduler
    {
        public static NoOpCleanupScheduler Instance { get; } = new();

        public bool TryScheduleRelease(IntPtr processHandle, IntPtr threadHandle, IntPtr remoteAddress) => true;
    }
}
