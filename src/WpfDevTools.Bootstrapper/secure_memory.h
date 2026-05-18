#pragma once

#include <Windows.h>
#include <cstddef>
#include <string>

inline void SecureWipeBuffer(void* buffer, size_t byteCount) noexcept
{
    if (buffer == nullptr || byteCount == 0)
        return;

    SecureZeroMemory(buffer, byteCount);
}

template <typename CharT, typename Traits, typename Allocator>
inline void SecureWipeString(std::basic_string<CharT, Traits, Allocator>& value) noexcept
{
    if (!value.empty())
    {
        SecureWipeBuffer(value.data(), value.size() * sizeof(CharT));
    }

    value.clear();
}
