#pragma once
#include <Windows.h>

// Load .NET Framework CLR and call managed bridge via ExecuteInDefaultAppDomain.
// Returns bootstrapper exit code.
DWORD HostNetFramework(const wchar_t* inspectorDllPath, const wchar_t* parameters);
