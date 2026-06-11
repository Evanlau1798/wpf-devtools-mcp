# ADR-003：採用 Length-Prefix Framing

## 狀態

Accepted。

## 背景

byte mode 的 named pipes 不會自動保留訊息邊界。

## 決策

每個 JSON payload 前都加上一個 4-byte length 欄位。

## 選擇原因

- 額外負擔小
- 訊息邊界清楚
- 容易處理 partial read
- 對可變大小的診斷 payload 很合適

## 影響

- 讀取端必須驗證訊息大小
- 讀取端必須迴圈讀到完整 payload

更多內容請參考[IPC 與通訊協定](../ipc.md)。
