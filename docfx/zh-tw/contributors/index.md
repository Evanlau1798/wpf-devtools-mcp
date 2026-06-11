# 貢獻指南

感謝你考慮為這個專案做出貢獻。

## 建議先讀

1. [開發環境設定](development-setup.md)
2. [測試與 TDD](testing-and-tdd.md)
3. [文件撰寫風格](documentation-style.md)
4. [ADR 索引](../architecture/adrs/index.md)

## 專案優先順序

本專案特別重視：

- 正確性優先於表面上的功能數量
- 生產環境等級的診斷品質與安全性
- AI-Friendly 的 MCP 契約與操作指引
- 能保護真實行為的測試，而不是只追求覆蓋率數字
- 準確、以任務為導向、容易快速掃讀的文件

## 在提出變更之前

- 先確認該行為是否已由 ADR 覆蓋
- 先理解 runtime 與 architecture 影響
- 優先提交範圍小、且有測試支撐的變更
- 遵守倉庫的單檔案大小限制
