#include <Windows.h>
#include <string>
#include <fstream>
#include <sstream>
#include "exit_codes.h"
#include "clr_hosting.h"
#include "coreclr_hosting.h"

struct BootstrapConfig
{
    std::wstring InspectorPath;
    std::wstring PipeName;
    std::wstring AuthSecretBase64;
    std::wstring AuthSecretFile;
    std::wstring CertDirectory;
    bool AuthEnabled = false;
    bool EncryptionEnabled = false;
};

static void AppendParam(std::wstring& target, const wchar_t* key, const std::wstring& value)
{
    if (value.empty())
        return;

    if (!target.empty())
        target += L';';

    target += key;
    target += L'=';
    target += value;
}

static std::wstring BuildManagedParams(const BootstrapConfig& config)
{
    std::wstring result;
    AppendParam(result, L"inspectorDllPath", config.InspectorPath);
    AppendParam(result, L"pipeName", config.PipeName);

    if (config.AuthEnabled)
    {
        AppendParam(result, L"auth", L"enabled");
        AppendParam(result, L"authSecretBase64", config.AuthSecretBase64);
    }

    if (config.EncryptionEnabled)
    {
        AppendParam(result, L"encryption", L"enabled");
        AppendParam(result, L"certDirectory", config.CertDirectory);
    }

    return result;
}

static bool ParseLegacyParams(const std::wstring& input, BootstrapConfig& config)
{
    auto sepPos = input.find(L';');
    if (sepPos == std::wstring::npos)
        return false;

    config.InspectorPath = input.substr(0, sepPos);
    config.PipeName = input.substr(sepPos + 1);
    return !config.InspectorPath.empty() && !config.PipeName.empty();
}

static bool LooksLikeKeyValueParams(const std::wstring& input)
{
    std::wistringstream stream(input);
    std::wstring pair;
    while (std::getline(stream, pair, L';'))
    {
        if (pair.rfind(L"inspectorDllPath=", 0) == 0 ||
            pair.rfind(L"pipeName=", 0) == 0 ||
            pair.rfind(L"auth=", 0) == 0 ||
            pair.rfind(L"authSecretFile=", 0) == 0 ||
            pair.rfind(L"encryption=", 0) == 0 ||
            pair.rfind(L"certDirectory=", 0) == 0)
        {
            return true;
        }
    }

    return false;
}

static bool ParseKeyValueParams(const std::wstring& input, BootstrapConfig& config)
{
    std::wistringstream stream(input);
    std::wstring pair;
    while (std::getline(stream, pair, L';'))
    {
        if (pair.empty())
            continue;

        auto sepPos = pair.find(L'=');
        if (sepPos == std::wstring::npos || sepPos == 0)
            continue;

        auto key = pair.substr(0, sepPos);
        auto value = pair.substr(sepPos + 1);

        if (key == L"inspectorDllPath")
            config.InspectorPath = value;
        else if (key == L"pipeName")
            config.PipeName = value;
        else if (key == L"auth")
            config.AuthEnabled = (value == L"enabled" || value == L"Enabled");
        else if (key == L"authSecretFile")
        {
            config.AuthSecretFile = value;
            config.AuthEnabled = true;
        }
        else if (key == L"encryption")
            config.EncryptionEnabled = (value == L"enabled" || value == L"Enabled");
        else if (key == L"certDirectory")
        {
            config.CertDirectory = value;
            config.EncryptionEnabled = true;
        }
    }

    return !config.InspectorPath.empty() && !config.PipeName.empty();
}

// Parse bootstrap parameters from the injected argument string.
static bool ParseParams(const wchar_t* params, BootstrapConfig& config)
{
    if (params == nullptr || params[0] == L'\0')
        return false;

    std::wstring input(params);
    if (!LooksLikeKeyValueParams(input) && input.find(L';') != std::wstring::npos)
        return ParseLegacyParams(input, config);

    return ParseKeyValueParams(input, config);
}

static std::wstring TrimWhitespace(const std::wstring& value)
{
    const wchar_t* whitespace = L" \t\r\n";
    auto start = value.find_first_not_of(whitespace);
    if (start == std::wstring::npos)
        return L"";

    auto end = value.find_last_not_of(whitespace);
    return value.substr(start, end - start + 1);
}

static std::wstring Utf8ToWide(const std::string& value)
{
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
    if (wideLen <= 1)
        return L"";

    std::wstring wval(wideLen - 1, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, wval.data(), wideLen);
    return wval;
}

static bool ReadUtf8File(const std::wstring& path, std::wstring& value)
{
    HANDLE file = CreateFileW(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return false;

    LARGE_INTEGER fileSize;
    if (!GetFileSizeEx(file, &fileSize) || fileSize.QuadPart <= 0 || fileSize.QuadPart > 4096)
    {
        CloseHandle(file);
        return false;
    }

    std::string content(static_cast<size_t>(fileSize.QuadPart), '\0');
    DWORD bytesRead = 0;
    BOOL read = ReadFile(
        file,
        content.data(),
        static_cast<DWORD>(content.size()),
        &bytesRead,
        nullptr);
    CloseHandle(file);

    if (!read || bytesRead == 0)
        return false;

    content.resize(bytesRead);
    value = TrimWhitespace(Utf8ToWide(content));
    return !value.empty();
}

static void LogAuthSecretDeleteFailure(DWORD errorCode)
{
    std::wstring message = L"WpfDevTools Bootstrapper warning: failed to delete authentication secret file. error="
        + std::to_wstring(errorCode) + L"\n";
    OutputDebugStringW(message.c_str());
}

static void DeleteAuthSecretFile(const std::wstring& path)
{
    if (DeleteFileW(path.c_str()))
        return;

    DWORD errorCode = GetLastError();
    if (errorCode == ERROR_FILE_NOT_FOUND || errorCode == ERROR_PATH_NOT_FOUND)
        return;

    LogAuthSecretDeleteFailure(errorCode);
}

static bool LoadAuthSecretFromFile(BootstrapConfig& config)
{
    if (config.AuthSecretFile.empty())
        return true;

    std::wstring secret;
    bool loaded = ReadUtf8File(config.AuthSecretFile, secret);
    DeleteAuthSecretFile(config.AuthSecretFile);

    if (!loaded)
        return false;

    config.AuthSecretBase64 = secret;
    config.AuthEnabled = true;
    return true;
}

// Fallback: read params from temp config file.
static bool ReadConfigFile(DWORD pid, BootstrapConfig& config)
{
    wchar_t tempPath[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tempPath) == 0)
        return false;

    std::wstring configPath = std::wstring(tempPath)
        + L"WpfDevTools_Bootstrap_" + std::to_wstring(pid) + L".json";

    std::ifstream file(configPath);
    if (!file.is_open())
        return false;

    std::string content((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    auto findValue = [&](const std::string& key) -> std::wstring {
        auto pos = content.find("\"" + key + "\"");
        if (pos == std::string::npos) return L"";
        auto colonPos = content.find(':', pos);
        if (colonPos == std::string::npos) return L"";
        auto quoteStart = content.find('"', colonPos + 1);
        if (quoteStart == std::string::npos) return L"";

        std::string rawValue;
        bool escaped = false;
        for (size_t index = quoteStart + 1; index < content.size(); ++index)
        {
            auto ch = content[index];
            if (escaped)
            {
                switch (ch)
                {
                case '\\': rawValue.push_back('\\'); break;
                case '"': rawValue.push_back('"'); break;
                case 'n': rawValue.push_back('\n'); break;
                case 'r': rawValue.push_back('\r'); break;
                case 't': rawValue.push_back('\t'); break;
                default: rawValue.push_back(ch); break;
                }
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                break;
            }

            rawValue.push_back(ch);
        }

        int wideLen = MultiByteToWideChar(CP_UTF8, 0, rawValue.c_str(), -1, nullptr, 0);
        if (wideLen <= 1)
            return L"";

        std::wstring wval(wideLen - 1, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, rawValue.c_str(), -1, wval.data(), wideLen);
        return wval;
    };

    config.InspectorPath = findValue("inspectorDllPath");
    config.PipeName = findValue("pipeName");
    config.AuthSecretFile = findValue("authSecretFile");
    config.CertDirectory = findValue("certDirectory");

    auto auth = findValue("auth");
    if (!auth.empty())
    {
        config.AuthEnabled = (auth == L"enabled" || auth == L"Enabled");
    }

    auto encryption = findValue("encryption");
    if (!encryption.empty())
    {
        config.EncryptionEnabled = (encryption == L"enabled" || encryption == L"Enabled");
    }

    if (!config.AuthSecretFile.empty())
        config.AuthEnabled = true;

    if (!config.CertDirectory.empty())
        config.EncryptionEnabled = true;

    file.close();
    DeleteFileW(configPath.c_str());

    return !config.InspectorPath.empty() && !config.PipeName.empty();
}

// Exported function called via CreateRemoteThread (step 2).
extern "C" __declspec(dllexport) DWORD WINAPI BootstrapInspector(LPVOID lpParameter)
{
    BootstrapConfig config;

    auto params = static_cast<const wchar_t*>(lpParameter);
    if (!ParseParams(params, config))
    {
        DWORD pid = GetCurrentProcessId();
        if (!ReadConfigFile(pid, config))
            return ExitCodes::InspectorPathInvalid;
    }

    if (!LoadAuthSecretFromFile(config))
        return ExitCodes::AuthSecretLoadFailed;

    std::wstring managedParams = BuildManagedParams(config);

    HMODULE hClr = GetModuleHandleW(L"clr.dll");
    HMODULE hCoreclr = GetModuleHandleW(L"coreclr.dll");

    if (hClr != nullptr)
    {
        return HostNetFramework(config.InspectorPath.c_str(), managedParams.c_str());
    }

    if (hCoreclr != nullptr)
    {
        return HostNetCore(config.InspectorPath.c_str(), managedParams.c_str());
    }

    return ExitCodes::NoClrFound;
}
