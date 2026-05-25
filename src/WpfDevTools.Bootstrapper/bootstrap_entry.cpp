#include <Windows.h>
#include <wincrypt.h>
#include <algorithm>
#include <string>
#include <sstream>
#include "exit_codes.h"
#include "clr_hosting.h"
#include "coreclr_hosting.h"
#include "secure_memory.h"

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

static size_t GetParamLength(const wchar_t* key, const std::wstring& value)
{
    if (value.empty())
        return 0;

    return wcslen(key) + 1 + value.size();
}

static void ReserveManagedParamsCapacity(std::wstring& target, const BootstrapConfig& config)
{
    size_t capacity = GetParamLength(L"inspectorDllPath", config.InspectorPath)
        + GetParamLength(L"pipeName", config.PipeName);
    int paramCount = 0;

    if (!config.InspectorPath.empty())
        ++paramCount;
    if (!config.PipeName.empty())
        ++paramCount;

    if (config.AuthEnabled)
    {
        capacity += GetParamLength(L"auth", L"enabled")
            + GetParamLength(L"authSecretBase64", config.AuthSecretBase64);
        ++paramCount;
        if (!config.AuthSecretBase64.empty())
            ++paramCount;
    }

    if (config.EncryptionEnabled)
    {
        capacity += GetParamLength(L"encryption", L"enabled")
            + GetParamLength(L"certDirectory", config.CertDirectory);
        ++paramCount;
        if (!config.CertDirectory.empty())
            ++paramCount;
    }

    if (paramCount > 1)
        capacity += static_cast<size_t>(paramCount - 1);

    target.reserve(capacity);
}

static std::wstring BuildManagedParams(const BootstrapConfig& config)
{
    std::wstring result;
    ReserveManagedParamsCapacity(result, config);

    AppendParam(result, L"inspectorDllPath", config.InspectorPath);
    AppendParam(result, L"pipeName", config.PipeName);

    if (config.AuthEnabled)
    {
        AppendParam(result, L"auth", L"enabled");
    }

    if (config.EncryptionEnabled)
    {
        AppendParam(result, L"encryption", L"enabled");
        AppendParam(result, L"certDirectory", config.CertDirectory);
    }

    if (config.AuthEnabled)
    {
        AppendParam(result, L"authSecretBase64", config.AuthSecretBase64);
    }

    return result;
}

static bool IsLocalMachineDpapiFallbackAllowed()
{
    wchar_t value[16] = {};
    DWORD length = GetEnvironmentVariableW(
        L"WPFDEVTOOLS_ALLOW_LOCALMACHINE_DPAPI_FALLBACK",
        value,
        static_cast<DWORD>(sizeof(value) / sizeof(value[0])));
    if (length == 0 || length >= (sizeof(value) / sizeof(value[0])))
        return false;

    return _wcsicmp(value, L"1") == 0
        || _wcsicmp(value, L"true") == 0
        || _wcsicmp(value, L"yes") == 0;
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

static bool TryUtf8ToWide(const std::string& value, std::wstring& output)
{
    output.clear();
    if (value.empty())
        return true;

    int byteCount = static_cast<int>(value.size());
    int wideLen = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), byteCount, nullptr, 0);
    if (wideLen <= 0)
        return false;

    output.assign(wideLen, L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), byteCount, output.data(), wideLen) == wideLen)
        return true;

    SecureWipeString(output);
    return false;
}

static bool ReadProtectedAuthSecretFile(const std::wstring& path, std::wstring& value)
{
    SecureWipeString(value);

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
    {
        SecureWipeString(content);
        return false;
    }

    content.resize(bytesRead);
    const std::string currentUserHeader = "WPFDEVTOOLS-DPAPI:CurrentUser\n";
    const std::string localMachineHeader = "WPFDEVTOOLS-DPAPI:LocalMachine\n";
    size_t payloadOffset = std::string::npos;
    if (content.rfind(currentUserHeader, 0) == 0)
        payloadOffset = currentUserHeader.size();
    else if (content.rfind(localMachineHeader, 0) == 0)
    {
        if (!IsLocalMachineDpapiFallbackAllowed())
        {
            SecureWipeString(content);
            return false;
        }

        payloadOffset = localMachineHeader.size();
    }

    if (payloadOffset == std::string::npos)
    {
        SecureWipeString(content);
        return false;
    }

    if (payloadOffset >= content.size())
    {
        SecureWipeString(content);
        return false;
    }

    DATA_BLOB input = {
        static_cast<DWORD>(content.size() - payloadOffset),
        reinterpret_cast<BYTE*>(content.data() + payloadOffset)
    };
    DATA_BLOB output = {};
    if (!CryptUnprotectData(&input, nullptr, nullptr, nullptr, nullptr, 0, &output))
    {
        SecureWipeString(content);
        return false;
    }

    std::string plaintext(reinterpret_cast<char*>(output.pbData), output.cbData);
    SecureWipeBuffer(output.pbData, output.cbData);
    LocalFree(output.pbData);
    bool success = TryUtf8ToWide(plaintext, value) && !value.empty();
    if (!success)
        SecureWipeString(value);

    SecureWipeString(plaintext);
    SecureWipeString(content);
    return success;
}

static void LogAuthSecretFileCleanupFailure(const wchar_t* operation, DWORD errorCode)
{
    std::wstring message = L"WpfDevTools Bootstrapper warning: failed to ";
    message += operation;
    message += L" authentication secret file. error="
        + std::to_wstring(errorCode) + L"\n";
    OutputDebugStringW(message.c_str());
}

static bool WipeFileContents(const std::wstring& path)
{
    HANDLE file = CreateFileW(
        path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return false;

    LARGE_INTEGER fileSize;
    if (!GetFileSizeEx(file, &fileSize))
    {
        DWORD errorCode = GetLastError();
        CloseHandle(file);
        SetLastError(errorCode == ERROR_SUCCESS ? ERROR_READ_FAULT : errorCode);
        return false;
    }

    bool success = true;
    DWORD errorCode = ERROR_SUCCESS;
    auto recordLastErrorFailure = [&errorCode, &success](DWORD fallbackError = ERROR_WRITE_FAULT)
    {
        success = false;
        if (errorCode == ERROR_SUCCESS)
        {
            DWORD lastError = GetLastError();
            errorCode = lastError == ERROR_SUCCESS ? fallbackError : lastError;
        }
    };
    auto recordDeterministicFailure = [&errorCode, &success](DWORD failureCode)
    {
        success = false;
        if (errorCode == ERROR_SUCCESS)
        {
            errorCode = failureCode;
        }
    };

    if (fileSize.QuadPart > 0)
    {
        BYTE zeros[256] = {};
        LONGLONG remaining = fileSize.QuadPart;
        while (remaining > 0)
        {
            DWORD bytesToWrite = static_cast<DWORD>(std::min<LONGLONG>(remaining, sizeof(zeros)));
            DWORD bytesWritten = 0;
            BOOL wrote = WriteFile(file, zeros, bytesToWrite, &bytesWritten, nullptr);
            if (!wrote)
            {
                recordLastErrorFailure();
                break;
            }

            if (bytesWritten != bytesToWrite)
            {
                recordDeterministicFailure(ERROR_WRITE_FAULT);
                break;
            }

            remaining -= bytesWritten;
        }

        if (!FlushFileBuffers(file))
            recordLastErrorFailure(ERROR_WRITE_FAULT);

        SetFilePointer(file, 0, nullptr, FILE_BEGIN);
    }

    if (!SetEndOfFile(file))
        recordLastErrorFailure(ERROR_WRITE_FAULT);

    if (!FlushFileBuffers(file))
        recordLastErrorFailure(ERROR_WRITE_FAULT);

    CloseHandle(file);
    if (!success)
        SetLastError(errorCode);

    return success;
}

static void SecureDeleteAuthSecretFile(const std::wstring& path)
{
    if (!WipeFileContents(path))
    {
        DWORD wipeErrorCode = GetLastError();
        if (wipeErrorCode != ERROR_FILE_NOT_FOUND && wipeErrorCode != ERROR_PATH_NOT_FOUND)
            LogAuthSecretFileCleanupFailure(L"wipe", wipeErrorCode);
    }

    if (DeleteFileW(path.c_str()))
        return;

    DWORD errorCode = GetLastError();
    if (errorCode == ERROR_FILE_NOT_FOUND || errorCode == ERROR_PATH_NOT_FOUND)
        return;

    LogAuthSecretFileCleanupFailure(L"delete", errorCode);
}

static bool LoadAuthSecretFromFile(BootstrapConfig& config)
{
    if (config.AuthSecretFile.empty())
        return true;

    std::wstring secret;
    bool loaded = ReadProtectedAuthSecretFile(config.AuthSecretFile, secret);
    SecureDeleteAuthSecretFile(config.AuthSecretFile);

    if (!loaded)
    {
        SecureWipeString(secret);
        return false;
    }

    SecureWipeString(config.AuthSecretBase64);
    config.AuthSecretBase64.swap(secret);
    SecureWipeString(secret);
    config.AuthEnabled = true;
    return true;
}

// Exported function called via CreateRemoteThread (step 2).
extern "C" __declspec(dllexport) DWORD WINAPI BootstrapInspector(LPVOID lpParameter)
{
    BootstrapConfig config;
    DWORD result = ExitCodes::NoClrFound;

    auto params = static_cast<const wchar_t*>(lpParameter);
    if (!ParseParams(params, config))
    {
        return ExitCodes::InspectorPathInvalid;
    }

    if (!LoadAuthSecretFromFile(config))
    {
        SecureWipeString(config.AuthSecretBase64);
        return ExitCodes::AuthSecretLoadFailed;
    }

    std::wstring managedParams = BuildManagedParams(config);

    HMODULE hClr = GetModuleHandleW(L"clr.dll");
    HMODULE hCoreclr = GetModuleHandleW(L"coreclr.dll");

    if (hClr != nullptr)
    {
        result = HostNetFramework(config.InspectorPath.c_str(), managedParams.c_str());
    }
    else if (hCoreclr != nullptr)
    {
        result = HostNetCore(config.InspectorPath.c_str(), managedParams.c_str());
    }

    SecureWipeString(config.AuthSecretBase64);
    SecureWipeString(managedParams);
    return result;
}
