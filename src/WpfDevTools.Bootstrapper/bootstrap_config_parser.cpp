#include <Windows.h>
#include <cstdint>
#include "bootstrap_config_parser.h"

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
    return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), byteCount, output.data(), wideLen) == wideLen;
}

static bool IsJsonWhitespace(char ch)
{
    return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
}

static void SkipJsonWhitespace(const std::string& content, size_t& index)
{
    while (index < content.size() && IsJsonWhitespace(content[index]))
        ++index;
}

static bool TryReadHexQuad(const std::string& content, size_t& index, uint32_t& value)
{
    if (index + 4 > content.size())
        return false;

    value = 0;
    for (int digitIndex = 0; digitIndex < 4; ++digitIndex)
    {
        char ch = content[index++];
        uint32_t digit;
        if (ch >= '0' && ch <= '9')
            digit = static_cast<uint32_t>(ch - '0');
        else if (ch >= 'a' && ch <= 'f')
            digit = static_cast<uint32_t>(ch - 'a' + 10);
        else if (ch >= 'A' && ch <= 'F')
            digit = static_cast<uint32_t>(ch - 'A' + 10);
        else
            return false;

        value = (value << 4) | digit;
    }

    return true;
}

static bool AppendUtf8CodePoint(uint32_t codePoint, std::string& output)
{
    if (codePoint == 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
        return false;

    if (codePoint <= 0x7F)
    {
        output.push_back(static_cast<char>(codePoint));
        return true;
    }

    if (codePoint <= 0x7FF)
    {
        output.push_back(static_cast<char>(0xC0 | (codePoint >> 6)));
        output.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
        return true;
    }

    if (codePoint <= 0xFFFF)
    {
        output.push_back(static_cast<char>(0xE0 | (codePoint >> 12)));
        output.push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F)));
        output.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
        return true;
    }

    output.push_back(static_cast<char>(0xF0 | (codePoint >> 18)));
    output.push_back(static_cast<char>(0x80 | ((codePoint >> 12) & 0x3F)));
    output.push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F)));
    output.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    return true;
}

static bool AppendUnicodeEscape(const std::string& content, size_t& index, std::string& output)
{
    uint32_t codeUnit;
    if (!TryReadHexQuad(content, index, codeUnit))
        return false;

    if (codeUnit >= 0xD800 && codeUnit <= 0xDBFF)
    {
        if (index + 2 > content.size() || content[index] != '\\' || content[index + 1] != 'u')
            return false;

        index += 2;
        uint32_t lowSurrogate;
        if (!TryReadHexQuad(content, index, lowSurrogate) || lowSurrogate < 0xDC00 || lowSurrogate > 0xDFFF)
            return false;

        uint32_t codePoint = 0x10000 + (((codeUnit - 0xD800) << 10) | (lowSurrogate - 0xDC00));
        return AppendUtf8CodePoint(codePoint, output);
    }

    return AppendUtf8CodePoint(codeUnit, output);
}

static bool ParseJsonString(const std::string& content, size_t& index, std::wstring& value)
{
    if (index >= content.size() || content[index] != '"')
        return false;

    ++index;
    std::string decoded;
    while (index < content.size())
    {
        char ch = content[index++];
        if (ch == '"')
            return TryUtf8ToWide(decoded, value);

        if (ch == '\\')
        {
            if (index >= content.size())
                return false;

            char escape = content[index++];
            switch (escape)
            {
            case '"': decoded.push_back('"'); break;
            case '\\': decoded.push_back('\\'); break;
            case '/': decoded.push_back('/'); break;
            case 'b': decoded.push_back('\b'); break;
            case 'f': decoded.push_back('\f'); break;
            case 'n': decoded.push_back('\n'); break;
            case 'r': decoded.push_back('\r'); break;
            case 't': decoded.push_back('\t'); break;
            case 'u':
                if (!AppendUnicodeEscape(content, index, decoded))
                    return false;
                break;
            default:
                return false;
            }

            continue;
        }

        if (static_cast<unsigned char>(ch) < 0x20)
            return false;

        decoded.push_back(ch);
    }

    return false;
}

static void ApplyJsonConfigValue(BootstrapConfig& config, const std::wstring& key, const std::wstring& value)
{
    if (key == L"inspectorDllPath")
        config.InspectorPath = value;
    else if (key == L"pipeName")
        config.PipeName = value;
    else if (key == L"auth")
        config.AuthEnabled = (value == L"enabled" || value == L"Enabled");
    else if (key == L"authSecretFile")
        config.AuthSecretFile = value;
    else if (key == L"encryption")
        config.EncryptionEnabled = (value == L"enabled" || value == L"Enabled");
    else if (key == L"certDirectory")
        config.CertDirectory = value;
}

bool TryParseBootstrapConfigJson(const std::string& content, BootstrapConfig& config)
{
    size_t index = 0;
    if (content.size() >= 3 &&
        static_cast<unsigned char>(content[0]) == 0xEF &&
        static_cast<unsigned char>(content[1]) == 0xBB &&
        static_cast<unsigned char>(content[2]) == 0xBF)
    {
        index = 3;
    }

    SkipJsonWhitespace(content, index);
    if (index >= content.size() || content[index] != '{')
        return false;

    ++index;
    bool requireMember = false;
    while (true)
    {
        SkipJsonWhitespace(content, index);
        if (index >= content.size())
            return false;

        if (content[index] == '}')
        {
            if (requireMember)
                return false;

            ++index;
            break;
        }

        std::wstring key;
        std::wstring value;
        if (!ParseJsonString(content, index, key))
            return false;

        SkipJsonWhitespace(content, index);
        if (index >= content.size() || content[index++] != ':')
            return false;

        SkipJsonWhitespace(content, index);
        if (!ParseJsonString(content, index, value))
            return false;

        ApplyJsonConfigValue(config, key, value);
        SkipJsonWhitespace(content, index);
        if (index >= content.size())
            return false;

        if (content[index] == ',')
        {
            ++index;
            requireMember = true;
            continue;
        }

        if (content[index] == '}')
        {
            ++index;
            break;
        }

        return false;
    }

    SkipJsonWhitespace(content, index);
    if (index != content.size())
        return false;

    if (!config.AuthSecretFile.empty())
        config.AuthEnabled = true;

    if (!config.CertDirectory.empty())
        config.EncryptionEnabled = true;

    return !config.InspectorPath.empty() && !config.PipeName.empty();
}