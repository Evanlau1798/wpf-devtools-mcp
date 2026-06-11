using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Resolves the base address of a module in a remote process.
/// Uses K32EnumProcessModulesEx + GetModuleFileNameExW for full path matching.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class RemoteModuleResolver
{
    /// <summary>
    /// Find the remote base address of a DLL by full path comparison.
    /// </summary>
    public static IntPtr FindModuleBase(IntPtr hProcess, string dllFullPath)
    {
        var normalizedTarget = Path.GetFullPath(dllFullPath);

        var modules = new IntPtr[1024];
        if (!K32EnumProcessModulesEx(hProcess, modules, modules.Length * IntPtr.Size,
            out int cbNeeded, LIST_MODULES_ALL))
        {
            return IntPtr.Zero;
        }

        int moduleCount = cbNeeded / IntPtr.Size;
        var pathBuffer = new StringBuilder(260);

        for (int i = 0; i < moduleCount; i++)
        {
            pathBuffer.Clear();
            if (GetModuleFileNameExW(hProcess, modules[i], pathBuffer, pathBuffer.Capacity) > 0)
            {
                var modulePath = Path.GetFullPath(pathBuffer.ToString());
                if (modulePath.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return modules[i];
                }
            }
        }

        return IntPtr.Zero;
    }

    private const uint LIST_MODULES_ALL = 0x03;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EnumProcessModulesEx(
        IntPtr hProcess,
        [Out] IntPtr[] lphModule,
        int cb,
        out int lpcbNeeded,
        uint dwFilterFlag);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameExW(
        IntPtr hProcess,
        IntPtr hModule,
        StringBuilder lpFilename,
        int nSize);
}
