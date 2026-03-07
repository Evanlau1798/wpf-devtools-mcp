#pragma once

// Bootstrapper exit codes must match BootstrapResultInterpreter.cs.
namespace ExitCodes {
    constexpr DWORD Success                 = 0x00;
    constexpr DWORD NoClrFound              = 0x10;
    constexpr DWORD ClrHostingFailed        = 0x11;
    constexpr DWORD ManagedEntrypointFailed = 0x12;
    constexpr DWORD HostfxrLoadFailed       = 0x13;
    constexpr DWORD InspectorPathInvalid    = 0x14;
}
