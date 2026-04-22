using FluentAssertions;
using WpfDevTools.Injector.Injection;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public sealed class DllInjectorErrorSanitizationTests
{
    [Fact]
    public void Inject_WhenOpenProcessFails_ShouldReturnSanitizedMessage()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new FailingOpenProcessApi { LastError = 5 };
            var injector = new DllInjector(api, NoOpCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Error.Should().Be(WpfDevTools.Shared.Enums.InjectionError.AccessDenied);
            result.ErrorMessage.Should().Be("Failed to open target process for injection");
            result.ErrorMessage.Should().NotContain("5");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void Inject_WhenInternalExceptionOccurs_ShouldReturnSanitizedMessage()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new ThrowingModuleHandleApi(new InvalidOperationException("secret failure details"));
            var injector = new DllInjector(api, NoOpCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Error.Should().Be(WpfDevTools.Shared.Enums.InjectionError.Unknown);
            result.ErrorMessage.Should().Be("Unexpected injection failure");
            result.ErrorMessage.Should().NotContain("secret failure details");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void Inject_WhenWaitFails_ShouldReturnSanitizedMessage()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new RemoteWaitFailureApi
            {
                WaitResult = RemoteThreadInvocationResult.WaitFailed,
                LastError = 32
            };
            var injector = new DllInjector(api, NoOpCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Error.Should().Be(WpfDevTools.Shared.Enums.InjectionError.Unknown);
            result.ErrorMessage.Should().Be("The injector could not confirm remote thread completion");
            result.ErrorMessage.Should().NotContain("32");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void Inject_WhenWaitResultIsUnexpected_ShouldReturnSanitizedMessage()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new RemoteWaitFailureApi
            {
                WaitResult = 0x00000080,
                LastError = 0
            };
            var injector = new DllInjector(api, NoOpCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Error.Should().Be(WpfDevTools.Shared.Enums.InjectionError.Unknown);
            result.ErrorMessage.Should().Be("The injector could not confirm remote thread completion");
            result.ErrorMessage.Should().NotContain("0x00000080");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void Inject_WhenDeferredCleanupSchedulingFails_ShouldReturnSanitizedMessage()
    {
        var dllPath = CreateExistingTempFile();
        try
        {
            var api = new RemoteWaitFailureApi
            {
                WaitResult = RemoteThreadInvocationResult.WaitTimeout,
                LastError = 0
            };
            var injector = new DllInjector(api, DecliningCleanupScheduler.Instance);

            var result = injector.Inject(Environment.ProcessId, dllPath, TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Error.Should().Be(WpfDevTools.Shared.Enums.InjectionError.Unknown);
            result.ErrorMessage.Should().Be("The injector could not confirm remote thread completion");
            result.ErrorMessage.Should().NotContain("deferred remote buffer cleanup");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    private static string CreateExistingTempFile()
    {
        var filePath = Path.GetTempFileName();
        File.WriteAllText(filePath, "placeholder");
        return filePath;
    }

    private sealed class FailingOpenProcessApi : IRemoteInjectionApi
    {
        public int LastError { get; init; }

        public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId) => IntPtr.Zero;

        public IntPtr GetModuleHandle(string moduleName) => throw new NotSupportedException();

        public IntPtr GetProcAddress(IntPtr moduleHandle, string procName) => throw new NotSupportedException();

        public IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address, uint size, uint allocationType, uint protect)
            => throw new NotSupportedException();

        public bool WriteProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint size, out int bytesWritten)
            => throw new NotSupportedException();

        public IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId) => throw new NotSupportedException();

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) => throw new NotSupportedException();

        public bool CloseHandle(IntPtr handle) => true;

        public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType)
            => throw new NotSupportedException();

        public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
            => throw new NotSupportedException();

        public int GetLastError() => LastError;
    }

    private sealed class ThrowingModuleHandleApi : IRemoteInjectionApi
    {
        private readonly Exception _exception;

        public ThrowingModuleHandleApi(Exception exception)
        {
            _exception = exception;
        }

        public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId) => new(0x1234);

        public IntPtr GetModuleHandle(string moduleName) => throw _exception;

        public IntPtr GetProcAddress(IntPtr moduleHandle, string procName) => throw new NotSupportedException();

        public IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address, uint size, uint allocationType, uint protect)
            => throw new NotSupportedException();

        public bool WriteProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint size, out int bytesWritten)
            => throw new NotSupportedException();

        public IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId) => throw new NotSupportedException();

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) => throw new NotSupportedException();

        public bool CloseHandle(IntPtr handle) => true;

        public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType)
            => throw new NotSupportedException();

        public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
            => throw new NotSupportedException();

        public int GetLastError() => 0;
    }

    private sealed class NoOpCleanupScheduler : IRemoteAllocationCleanupScheduler
    {
        public static NoOpCleanupScheduler Instance { get; } = new();

        public bool TryScheduleRelease(IntPtr processHandle, IntPtr threadHandle, IntPtr remoteAddress) => true;
    }

    private sealed class DecliningCleanupScheduler : IRemoteAllocationCleanupScheduler
    {
        public static DecliningCleanupScheduler Instance { get; } = new();

        public bool TryScheduleRelease(IntPtr processHandle, IntPtr threadHandle, IntPtr remoteAddress) => false;
    }

    private sealed class RemoteWaitFailureApi : IRemoteInjectionApi
    {
        public uint WaitResult { get; init; }
        public int LastError { get; init; }

        public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId) => new(0x1000);

        public IntPtr GetModuleHandle(string moduleName) => new(0x2000);

        public IntPtr GetProcAddress(IntPtr moduleHandle, string procName) => new(0x3000);

        public IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address, uint size, uint allocationType, uint protect)
            => new(0x4000);

        public bool WriteProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint size, out int bytesWritten)
        {
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

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) => WaitResult;

        public bool CloseHandle(IntPtr handle) => true;

        public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType) => true;

        public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
        {
            exitCode = 0;
            return false;
        }

        public int GetLastError() => LastError;
    }
}