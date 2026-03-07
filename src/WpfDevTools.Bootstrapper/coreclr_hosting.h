#pragma once
#include <Windows.h>

// Load .NET Core/5+ runtime via hostfxr and call managed bridge.
// Returns bootstrapper exit code.
DWORD HostNetCore(const wchar_t* inspectorDllPath, const wchar_t* parameters);
