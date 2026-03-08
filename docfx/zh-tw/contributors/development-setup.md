# 開發環境設定

## 先決條件

- Windows
- .NET SDK 8+
- Visual Studio 2022 或 Build Tools，且具備 WPF 與 C++ 支援

## 建置 managed 專案

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
```

## 建置 native bootstrapper

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```

若目標行程要求不同架構，請把 `x64` 改成 `Win32` 或 `ARM64`。

## 啟動測試應用程式

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

## 啟動 MCP server

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

## 在本機建置文件站

```powershell
dotnet tool restore
dotnet tool run docfx docfx/docfx.json
```

建置成功後，請用瀏覽器開啟 `docfx/_site/index.html`。
