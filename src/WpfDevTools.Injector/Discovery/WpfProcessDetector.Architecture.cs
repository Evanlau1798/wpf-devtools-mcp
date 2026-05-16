using System.Diagnostics;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Discovery;

public partial class WpfProcessDetector
{
    private const ushort ImageFileMachineUnknown = 0x0000;
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

    private ProcessArchitecture DetectArchitecture(Process process)
    {
        try
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                return ProcessArchitecture.X86;
            }

            if (TryDetectArchitectureWithIsWow64Process2(process.Handle, out var architecture))
            {
                return architecture;
            }

            if (IsWow64Process(process.Handle, out bool isWow64))
            {
                return isWow64 ? ProcessArchitecture.X86 : ProcessArchitecture.X64;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to detect architecture: {ex.Message}");
        }

        return ProcessArchitecture.Unknown;
    }

    /// <summary>
    /// Classify a process architecture from <c>IsWow64Process2</c> machine values.
    /// Extracted as pure logic so ARM64/x64/x86 combinations can be unit tested.
    /// </summary>
    public static ProcessArchitecture DetectArchitectureFromMachineTypes(
        ushort processMachine,
        ushort nativeMachine,
        bool is64BitOperatingSystem)
    {
        if (!is64BitOperatingSystem)
        {
            return ProcessArchitecture.X86;
        }

        return processMachine switch
        {
            ImageFileMachineI386 => ProcessArchitecture.X86,
            ImageFileMachineAmd64 => ProcessArchitecture.X64,
            ImageFileMachineArm64 => ProcessArchitecture.ARM64,
            ImageFileMachineUnknown => nativeMachine switch
            {
                ImageFileMachineAmd64 => ProcessArchitecture.X64,
                ImageFileMachineArm64 => ProcessArchitecture.ARM64,
                ImageFileMachineI386 => ProcessArchitecture.X86,
                _ => ProcessArchitecture.Unknown
            },
            _ => nativeMachine switch
            {
                ImageFileMachineArm64 => ProcessArchitecture.ARM64,
                ImageFileMachineAmd64 => ProcessArchitecture.X64,
                _ => ProcessArchitecture.Unknown
            }
        };
    }

    private static bool TryDetectArchitectureWithIsWow64Process2(
        IntPtr processHandle,
        out ProcessArchitecture architecture)
    {
        architecture = ProcessArchitecture.Unknown;

        try
        {
            if (!IsWow64Process2(processHandle, out ushort processMachine, out ushort nativeMachine))
            {
                return false;
            }

            architecture = DetectArchitectureFromMachineTypes(
                processMachine,
                nativeMachine,
                Environment.Is64BitOperatingSystem);
            return architecture != ProcessArchitecture.Unknown;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool isWow64);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "IsWow64Process2")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(
        IntPtr hProcess,
        out ushort processMachine,
        out ushort nativeMachine);
}
