#pragma once

#include <string>

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

bool TryParseBootstrapConfigJson(const std::string& content, BootstrapConfig& config);