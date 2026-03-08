# 部署指南

## 部署模型

這個 server 最常見的部署方式，是作為本機開發者或測試自動化的 companion process，與目標 WPF 應用程式一起運行在同一台 Windows 機器上。

目前 transport 為 STDIO，因此典型部署流程如下：

- 一個支援 MCP 的本機 client 啟動 `WpfDevTools.Mcp.Server`
- server 發現並連線到本機的 WPF 行程
- server 將 bootstrapper 與 inspector 注入到目標行程
- server 與 inspector 透過 named pipes 通訊

## 生產環境部署檢查表

- 以 `Release` 建置 server。
- 針對所有要支援的架構都建置 native bootstrapper。
- 對 release 用的 inspector DLL 完成簽章。
- 設定驗證與 TLS 相關環境變數。
- 確認 server 能在相同使用者或必要權限邊界下觸及目標應用。
- 至少 smoke-test `get_processes`、`connect`、`ping` 與一個代表性的 inspection workflow。

## 開發命令與 release 命令的差異

快速開始頁面使用的是從 repository root 執行的開發期命令：

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

若要做正式部署，或是提供可重複使用的內部 bundle，請先 publish，再啟動輸出成品：

```powershell
dotnet publish src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj -c Release -o <PUBLISH_DIR>
dotnet "<PUBLISH_DIR>\WpfDevTools.Mcp.Server.dll"
```

當您要在 MCP client 中註冊正式環境 server 時，請指向 publish 後的輸出，而不是原始碼專案命令。例如：

```powershell
claude mcp add --transport stdio wpf-devtools -- dotnet "<PUBLISH_DIR>\WpfDevTools.Mcp.Server.dll"
```

## 最小可交付產物集合

一份實用的 release bundle 至少應包含：

- MCP server 二進位
- inspector 二進位
- 架構專屬的 native bootstrapper 二進位
- 必要 runtime 相依項
- 關於環境變數與架構選擇的部署說明

## 營運建議

- 營運日誌請寫到 `stderr` 或檔案，不要寫到 `stdout`。
- 若你會發佈 x86、x64 與 ARM64 版本，請附上架構專屬 release notes。
- 發佈前至少選一個真實目標應用測試每種 runtime 家族：.NET Framework 與現代 .NET。
