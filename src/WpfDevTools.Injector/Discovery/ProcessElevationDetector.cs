using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Detects whether a target Windows process is running with an elevated token.
/// </summary>
public static class ProcessElevationDetector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    /// <summary>
    /// Attempt to determine whether the specified process is elevated.
    /// </summary>
    /// <param name="processId">Target process identifier.</param>
    /// <param name="isElevated">True when the target token is elevated.</param>
    /// <returns>
    /// True when the elevation state could be determined; otherwise false when the
    /// process could not be queried due to access limitations or process lifetime.
    /// </returns>
    public static bool TryIsProcessElevated(int processId, out bool isElevated)
    {
        isElevated = false;
        var processHandle = IntPtr.Zero;
        var tokenHandle = IntPtr.Zero;

        try
        {
            processHandle = OpenProcess(ProcessQueryLimitedInformation, false, unchecked((uint)processId));
            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!OpenProcessToken(processHandle, TokenQuery, out tokenHandle))
            {
                return false;
            }

            var elevation = default(TokenElevationData);
            var size = Marshal.SizeOf<TokenElevationData>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenElevation, buffer, size, out _))
                {
                    return false;
                }

                elevation = Marshal.PtrToStructure<TokenElevationData>(buffer);
                isElevated = elevation.TokenIsElevated != 0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Win32Exception)
        {
            return false;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
            {
                CloseHandle(tokenHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationData
    {
        public int TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
