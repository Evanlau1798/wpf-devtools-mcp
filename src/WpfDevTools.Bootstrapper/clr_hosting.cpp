#include "clr_hosting.h"
#include "exit_codes.h"

#include <metahost.h>
#include <mscoree.h>
#pragma comment(lib, "mscoree.lib")

DWORD HostNetFramework(const wchar_t* inspectorDllPath, const wchar_t* parameters)
{
    if (inspectorDllPath == nullptr || inspectorDllPath[0] == L'\0')
        return ExitCodes::InspectorPathInvalid;

    // Verify Inspector DLL exists
    DWORD attrs = GetFileAttributesW(inspectorDllPath);
    if (attrs == INVALID_FILE_ATTRIBUTES)
        return ExitCodes::InspectorPathInvalid;

    ICLRMetaHost* pMetaHost = nullptr;
    ICLRRuntimeInfo* pRuntimeInfo = nullptr;
    ICLRRuntimeHost* pRuntimeHost = nullptr;
    DWORD result = ExitCodes::ClrHostingFailed;

    HRESULT hr = CLRCreateInstance(
        CLSID_CLRMetaHost, IID_ICLRMetaHost,
        reinterpret_cast<LPVOID*>(&pMetaHost));
    if (FAILED(hr)) goto cleanup;

    // Get the runtime loaded in this process
    hr = pMetaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo,
        reinterpret_cast<LPVOID*>(&pRuntimeInfo));
    if (FAILED(hr)) goto cleanup;

    hr = pRuntimeInfo->GetInterface(
        CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost,
        reinterpret_cast<LPVOID*>(&pRuntimeHost));
    if (FAILED(hr)) goto cleanup;

    // Start the CLR if not already running (safe to call multiple times)
    hr = pRuntimeHost->Start();
    if (FAILED(hr) && hr != S_FALSE) goto cleanup;

    // ExecuteInDefaultAppDomain requires: public static int Method(string)
    // BootstrapBridge.Run matches this signature.
    {
        DWORD managedReturnValue = 0;
        hr = pRuntimeHost->ExecuteInDefaultAppDomain(
            inspectorDllPath,
            L"WpfDevTools.Inspector.BootstrapBridge",
            L"Run",
            parameters ? parameters : L"",
            &managedReturnValue);

        if (FAILED(hr))
        {
            result = ExitCodes::ManagedEntrypointFailed;
            goto cleanup;
        }

        // managedReturnValue: 0 = success, -1 = managed exception
        result = (managedReturnValue == 0)
            ? ExitCodes::Success
            : ExitCodes::ManagedEntrypointFailed;
    }

cleanup:
    if (pRuntimeHost) pRuntimeHost->Release();
    if (pRuntimeInfo) pRuntimeInfo->Release();
    if (pMetaHost) pMetaHost->Release();
    return result;
}
