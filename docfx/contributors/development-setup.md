# Development Setup

## Prerequisites

- Windows
- .NET SDK 8+
- Visual Studio 2022 or Build Tools with WPF and C++ support

## Build managed projects

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
```

## Build native bootstrapper

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```

Switch `x64` to `Win32` or `ARM64` when the target process requires it.

## Run the test app

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

## Run the MCP server

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

## Build the documentation locally

```powershell
dotnet tool restore
dotnet tool run docfx docfx/docfx.json
```

Open `docfx/_site/index.html` in a browser after a successful build.
