# ADR-001：以 Named Pipes 作為 IPC

## 狀態

Accepted。

## 背景

server 與 injected inspector 需要在同一台 Windows 機器上做低延遲、雙向且可控的通訊。

## 決策

在 server 與 inspector 之間採用 Windows named pipes。

## 選擇原因

- 本機延遲低
- 可套用 ACL
- 很適合 Windows-only 的本機開發與測試情境
- 不需要額外開放 TCP port

## 影響

- 這個設計把系統綁定在 Windows
- 仍需自行處理 message framing 與就緒判定

更多內容請參考[IPC 與通訊協定](../ipc.md)。
