#include "coreclr_hosting.h"
#include "exit_codes.h"

#include <nethost.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <string>
#include <memory>

// hostfxr function pointers (loaded dynamically)
using hostfxr_initialize_fn = int(HOSTFXR_CALLTYPE*)(
    const char_t* runtime_config_path,
    const hostfxr_initialize_parameters* parameters,
    hostfxr_handle* host_context_handle);

using hostfxr_get_delegate_fn = int(HOSTFXR_CALLTYPE*)(
    const hostfxr_handle host_context_handle,
    enum hostfxr_delegate_type type,
    void** delegate);

using hostfxr_close_fn = int(HOSTFXR_CALLTYPE*)(
    const hostfxr_handle host_context_handle);

// Bridge function pointer: [UnmanagedCallersOnly] static int RunNative(IntPtr, int)
using bridge_fn = int(CORECLR_DELEGATE_CALLTYPE*)(const uint8_t* args, int32_t sizeBytes);

constexpr int HostfxrSuccess = 0;
constexpr int HostfxrSuccessHostAlreadyInitialized = 1;
constexpr int HostfxrSuccessDifferentRuntimeProperties = 2;

static bool IsHostfxrInitializationSuccess(int rc)
{
    return rc == HostfxrSuccess ||
        rc == HostfxrSuccessHostAlreadyInitialized ||
        rc == HostfxrSuccessDifferentRuntimeProperties;
}

static std::wstring GetRuntimeConfigPath(const wchar_t* inspectorDllPath)
{
    std::wstring path(inspectorDllPath);
    // Replace .dll with .runtimeconfig.json
    auto dotPos = path.rfind(L'.');
    if (dotPos != std::wstring::npos)
        path = path.substr(0, dotPos);
    path += L".runtimeconfig.json";
    return path;
}

DWORD HostNetCore(const wchar_t* inspectorDllPath, const wchar_t* parameters)
{
    if (inspectorDllPath == nullptr || inspectorDllPath[0] == L'\0')
        return ExitCodes::InspectorPathInvalid;

    DWORD attrs = GetFileAttributesW(inspectorDllPath);
    if (attrs == INVALID_FILE_ATTRIBUTES)
        return ExitCodes::InspectorPathInvalid;

    // Step 1: Find hostfxr path via nethost
    char_t hostfxrPath[MAX_PATH];
    size_t bufferSize = MAX_PATH;
    int rc = get_hostfxr_path(hostfxrPath, &bufferSize, nullptr);
    if (rc != 0)
        return ExitCodes::HostfxrLoadFailed;

    // Step 2: Load hostfxr
    HMODULE hHostfxr = LoadLibraryW(hostfxrPath);
    if (!hHostfxr)
        return ExitCodes::HostfxrLoadFailed;

    auto initFn = reinterpret_cast<hostfxr_initialize_fn>(
        GetProcAddress(hHostfxr, "hostfxr_initialize_for_runtime_config"));
    auto getDelegateFn = reinterpret_cast<hostfxr_get_delegate_fn>(
        GetProcAddress(hHostfxr, "hostfxr_get_runtime_delegate"));
    auto closeFn = reinterpret_cast<hostfxr_close_fn>(
        GetProcAddress(hHostfxr, "hostfxr_close"));

    if (!initFn || !getDelegateFn || !closeFn)
    {
        FreeLibrary(hHostfxr);
        return ExitCodes::HostfxrLoadFailed;
    }

    // Step 3: Initialize for runtime config
    auto runtimeConfig = GetRuntimeConfigPath(inspectorDllPath);
    hostfxr_handle cxt = nullptr;
    rc = initFn(runtimeConfig.c_str(), nullptr, &cxt);
    if (!IsHostfxrInitializationSuccess(rc) || cxt == nullptr)
    {
        if (cxt) closeFn(cxt);
        FreeLibrary(hHostfxr);
        return ExitCodes::ClrHostingFailed;
    }

    // Step 4: Get load_assembly_and_get_function_pointer delegate
    load_assembly_and_get_function_pointer_fn loadAssemblyFn = nullptr;
    rc = getDelegateFn(cxt, hdt_load_assembly_and_get_function_pointer,
        reinterpret_cast<void**>(&loadAssemblyFn));
    if (rc != 0 || !loadAssemblyFn)
    {
        closeFn(cxt);
        FreeLibrary(hHostfxr);
        return ExitCodes::ClrHostingFailed;
    }

    // Step 5: Load Inspector assembly and get RunNative function pointer
    bridge_fn bridgeFn = nullptr;
    rc = loadAssemblyFn(
        inspectorDllPath,
        L"WpfDevTools.Inspector.BootstrapBridge, WpfDevTools.Inspector",
        L"RunNative",
        UNMANAGEDCALLERSONLY_METHOD,
        nullptr,
        reinterpret_cast<void**>(&bridgeFn));

    if (rc != 0 || !bridgeFn)
    {
        closeFn(cxt);
        FreeLibrary(hHostfxr);
        return ExitCodes::ManagedEntrypointFailed;
    }

    // Step 6: Convert parameters to UTF-8 and call bridge
    DWORD result = ExitCodes::ManagedEntrypointFailed;
    if (parameters && parameters[0] != L'\0')
    {
        int utf8Len = WideCharToMultiByte(CP_UTF8, 0, parameters, -1, nullptr, 0, nullptr, nullptr);
        if (utf8Len > 0)
        {
            auto utf8Buf = std::make_unique<uint8_t[]>(utf8Len);
            WideCharToMultiByte(CP_UTF8, 0, parameters, -1,
                reinterpret_cast<char*>(utf8Buf.get()), utf8Len, nullptr, nullptr);

            // sizeBytes excludes null terminator
            int sizeBytes = utf8Len - 1;
            int managedResult = bridgeFn(utf8Buf.get(), sizeBytes);
            result = (managedResult == 0) ? ExitCodes::Success : ExitCodes::ManagedEntrypointFailed;
        }
    }
    else
    {
        // Empty parameters
        int managedResult = bridgeFn(nullptr, 0);
        result = (managedResult == 0) ? ExitCodes::Success : ExitCodes::ManagedEntrypointFailed;
    }

    closeFn(cxt);
    FreeLibrary(hHostfxr);
    return result;
}
