using System.Text;
using FluentAssertions;
using WpfDevTools.Injector.Injection;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public sealed class DllInjectorSafetyContractTests
{
    [Fact]
    public void Invoke_WhenRemoteThreadTimesOut_ShouldScheduleDeferredCleanup()
    {
        var api = new FakeRemoteInjectionApi
        {
            WaitResult = RemoteThreadInvocationResult.WaitTimeout,
            RemoteBufferAddress = new IntPtr(0x1010),
            RemoteThreadHandle = new IntPtr(0x2020)
        };
        var cleanup = new RecordingCleanupScheduler();
        var invoker = new RemoteThreadBufferInvoker(api, cleanup);

        var result = invoker.Invoke(
            new IntPtr(0x3030),
            new IntPtr(0x4040),
            Encoding.Unicode.GetBytes("payload\0"),
            TimeSpan.FromSeconds(1),
            requireExitCode: false);

        result.Status.Should().Be(RemoteThreadInvocationStatus.TimedOut);
        result.DeferredCleanupScheduled.Should().BeTrue();
        cleanup.Requests.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CleanupRequest(new IntPtr(0x3030), new IntPtr(0x2020), new IntPtr(0x1010)));
        api.VirtualFreeCalls.Should().BeEmpty();
        api.ClosedHandles.Should().ContainSingle().Which.Should().Be(new IntPtr(0x2020));
    }

    [Fact]
    public void Invoke_WhenRemoteThreadCompletes_ShouldFreeRemoteBufferImmediately()
    {
        var api = new FakeRemoteInjectionApi
        {
            WaitResult = RemoteThreadInvocationResult.WaitObject0,
            RemoteBufferAddress = new IntPtr(0x1111),
            RemoteThreadHandle = new IntPtr(0x2222)
        };
        var cleanup = new RecordingCleanupScheduler();
        var invoker = new RemoteThreadBufferInvoker(api, cleanup);

        var result = invoker.Invoke(
            new IntPtr(0x3333),
            new IntPtr(0x4444),
            Encoding.Unicode.GetBytes("payload\0"),
            TimeSpan.FromSeconds(1),
            requireExitCode: false);

        result.Status.Should().Be(RemoteThreadInvocationStatus.Completed);
        result.DeferredCleanupScheduled.Should().BeFalse();
        cleanup.Requests.Should().BeEmpty();
        api.VirtualFreeCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VirtualFreeCall(new IntPtr(0x3333), new IntPtr(0x1111)));
        api.ClosedHandles.Should().ContainSingle().Which.Should().Be(new IntPtr(0x2222));
    }

    [Fact]
    public void Invoke_WhenExitCodeIsRequiredAndUnavailable_ShouldFailAfterCleaningUp()
    {
        var api = new FakeRemoteInjectionApi
        {
            WaitResult = RemoteThreadInvocationResult.WaitObject0,
            ExitCodeAvailable = false,
            RemoteBufferAddress = new IntPtr(0x1212),
            RemoteThreadHandle = new IntPtr(0x3434)
        };
        var cleanup = new RecordingCleanupScheduler();
        var invoker = new RemoteThreadBufferInvoker(api, cleanup);

        var result = invoker.Invoke(
            new IntPtr(0x5656),
            new IntPtr(0x7878),
            Encoding.Unicode.GetBytes("payload\0"),
            TimeSpan.FromSeconds(1),
            requireExitCode: true);

        result.Status.Should().Be(RemoteThreadInvocationStatus.ExitCodeUnavailable);
        cleanup.Requests.Should().BeEmpty();
        api.VirtualFreeCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VirtualFreeCall(new IntPtr(0x5656), new IntPtr(0x1212)));
    }

    [Fact]
    public void Invoke_WhenWriteFails_ShouldRollbackAllocationImmediately()
    {
        var api = new FakeRemoteInjectionApi
        {
            WriteSucceeds = false,
            RemoteBufferAddress = new IntPtr(0x9999)
        };
        var cleanup = new RecordingCleanupScheduler();
        var invoker = new RemoteThreadBufferInvoker(api, cleanup);

        var result = invoker.Invoke(
            new IntPtr(0xABAB),
            new IntPtr(0xCDCD),
            Encoding.Unicode.GetBytes("payload\0"),
            TimeSpan.FromSeconds(1),
            requireExitCode: false);

        result.Status.Should().Be(RemoteThreadInvocationStatus.WriteFailed);
        result.DeferredCleanupScheduled.Should().BeFalse();
        cleanup.Requests.Should().BeEmpty();
        api.VirtualFreeCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VirtualFreeCall(new IntPtr(0xABAB), new IntPtr(0x9999)));
    }

    [Fact]
    public void Invoke_WhenWriteFails_ShouldPreserveOriginalWin32ErrorAcrossRollback()
    {
        var api = new FakeRemoteInjectionApi
        {
            WriteSucceeds = false,
            LastError = 1234,
            LastErrorAfterCleanup = 5678,
            RemoteBufferAddress = new IntPtr(0xAAAA)
        };
        var cleanup = new RecordingCleanupScheduler();
        var invoker = new RemoteThreadBufferInvoker(api, cleanup);

        var result = invoker.Invoke(
            new IntPtr(0xBBBB),
            new IntPtr(0xCCCC),
            Encoding.Unicode.GetBytes("payload\0"),
            TimeSpan.FromSeconds(1),
            requireExitCode: false);

        result.Status.Should().Be(RemoteThreadInvocationStatus.WriteFailed);
        result.LastError.Should().Be(1234);
    }

    private sealed class FakeRemoteInjectionApi : IRemoteInjectionApi
    {
        public uint WaitResult { get; init; } = RemoteThreadInvocationResult.WaitObject0;
        public bool WriteSucceeds { get; init; } = true;
        public bool ExitCodeAvailable { get; init; } = true;
        public uint ExitCode { get; init; } = 7;
        public int LastError { get; set; } = 5;
        public int LastErrorAfterCleanup { get; init; } = 5;
        public IntPtr RemoteBufferAddress { get; init; } = new(0x1234);
        public IntPtr RemoteThreadHandle { get; init; } = new(0x5678);
        public List<VirtualFreeCall> VirtualFreeCalls { get; } = [];
        public List<IntPtr> ClosedHandles { get; } = [];

        public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId) => IntPtr.Zero;

        public IntPtr GetModuleHandle(string moduleName) => IntPtr.Zero;

        public IntPtr GetProcAddress(IntPtr moduleHandle, string procName) => IntPtr.Zero;

        public IntPtr VirtualAllocEx(
            IntPtr processHandle,
            IntPtr address,
            uint size,
            uint allocationType,
            uint protect) => RemoteBufferAddress;

        public bool WriteProcessMemory(
            IntPtr processHandle,
            IntPtr baseAddress,
            byte[] buffer,
            uint size,
            out int bytesWritten)
        {
            bytesWritten = buffer.Length;
            return WriteSucceeds;
        }

        public IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId) => RemoteThreadHandle;

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) => WaitResult;

        public bool CloseHandle(IntPtr handle)
        {
            ClosedHandles.Add(handle);
            return true;
        }

        public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType)
        {
            VirtualFreeCalls.Add(new VirtualFreeCall(processHandle, address));
            LastError = LastErrorAfterCleanup;
            return true;
        }

        public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
        {
            exitCode = ExitCode;
            return ExitCodeAvailable;
        }

        public int GetLastError() => LastError;
    }

    private sealed class RecordingCleanupScheduler : IRemoteAllocationCleanupScheduler
    {
        public List<CleanupRequest> Requests { get; } = [];

        public bool TryScheduleRelease(
            IntPtr processHandle,
            IntPtr threadHandle,
            IntPtr remoteAddress)
        {
            Requests.Add(new CleanupRequest(processHandle, threadHandle, remoteAddress));
            return true;
        }
    }

    private sealed record CleanupRequest(
        IntPtr ProcessHandle,
        IntPtr ThreadHandle,
        IntPtr RemoteAddress);

    private sealed record VirtualFreeCall(
        IntPtr ProcessHandle,
        IntPtr Address);
}
