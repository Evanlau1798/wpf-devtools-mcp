#include <Windows.h>
#include <string>
#include <fstream>
#include "exit_codes.h"
#include "clr_hosting.h"
#include "coreclr_hosting.h"

// Parse "inspectorDllPath;pipeName" from params string.
// Returns false if parsing fails.
static bool ParseParams(const wchar_t* params,
    std::wstring& outInspectorPath, std::wstring& outPipeName)
{
    if (params == nullptr || params[0] == L'\0')
        return false;

    std::wstring input(params);
    auto sepPos = input.find(L';');
    if (sepPos == std::wstring::npos)
        return false;

    outInspectorPath = input.substr(0, sepPos);
    outPipeName = input.substr(sepPos + 1);
    return !outInspectorPath.empty() && !outPipeName.empty();
}

// Fallback: read params from temp config file
static bool ReadConfigFile(DWORD pid,
    std::wstring& outInspectorPath, std::wstring& outPipeName)
{
    wchar_t tempPath[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tempPath) == 0)
        return false;

    std::wstring configPath = std::wstring(tempPath)
        + L"WpfDevTools_Bootstrap_" + std::to_wstring(pid) + L".json";

    // Simple JSON parse — look for inspectorDllPath and pipeName values
    std::ifstream file(configPath);
    if (!file.is_open())
        return false;

    std::string line;
    while (std::getline(file, line))
    {
        // Minimal parsing for "key": "value" format
        auto findValue = [&](const std::string& key) -> std::wstring {
            auto pos = line.find("\"" + key + "\"");
            if (pos == std::string::npos) return L"";
            auto colonPos = line.find(':', pos);
            if (colonPos == std::string::npos) return L"";
            auto quoteStart = line.find('"', colonPos + 1);
            if (quoteStart == std::string::npos) return L"";
            auto quoteEnd = line.find('"', quoteStart + 1);
            if (quoteEnd == std::string::npos) return L"";
            std::string val = line.substr(quoteStart + 1, quoteEnd - quoteStart - 1);
            int wideLen = MultiByteToWideChar(CP_UTF8, 0, val.c_str(), -1, nullptr, 0);
            std::wstring wval(wideLen - 1, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, val.c_str(), -1, wval.data(), wideLen);
            return wval;
        };

        auto path = findValue("inspectorDllPath");
        if (!path.empty()) outInspectorPath = path;

        auto pipe = findValue("pipeName");
        if (!pipe.empty()) outPipeName = pipe;
    }

    file.close();
    // Cleanup temp file
    DeleteFileW(configPath.c_str());

    return !outInspectorPath.empty() && !outPipeName.empty();
}

// Exported function — called via CreateRemoteThread (Step 2)
extern "C" __declspec(dllexport) DWORD WINAPI BootstrapInspector(LPVOID lpParameter)
{
    std::wstring inspectorDllPath;
    std::wstring pipeName;

    // Try inline params first, then config file
    auto params = static_cast<const wchar_t*>(lpParameter);
    if (!ParseParams(params, inspectorDllPath, pipeName))
    {
        DWORD pid = GetCurrentProcessId();
        if (!ReadConfigFile(pid, inspectorDllPath, pipeName))
            return ExitCodes::InspectorPathInvalid;
    }

    // Build parameters string for managed bridge: "inspectorDllPath;pipeName"
    std::wstring managedParams = inspectorDllPath + L";" + pipeName;

    // Detect which CLR is loaded
    HMODULE hClr = GetModuleHandleW(L"clr.dll");
    HMODULE hCoreclr = GetModuleHandleW(L"coreclr.dll");

    if (hClr != nullptr)
    {
        // .NET Framework path
        return HostNetFramework(inspectorDllPath.c_str(), managedParams.c_str());
    }
    else if (hCoreclr != nullptr)
    {
        // .NET Core/5+ path
        return HostNetCore(inspectorDllPath.c_str(), managedParams.c_str());
    }
    else
    {
        return ExitCodes::NoClrFound;
    }
}
