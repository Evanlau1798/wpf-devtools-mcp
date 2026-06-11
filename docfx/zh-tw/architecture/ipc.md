# IPC 與通訊協定

## Transport 選擇

- MCP client 到 server：STDIO
- Server 到 inspector：named pipes

## 為什麼使用 named pipes

對於僅限本機、僅限 Windows 的 IPC 情境，named pipes 兼具速度、ACL 能力，以及容易針對單機場景做安全保護的優點。

## Message framing

inspector protocol 在 named pipes 上使用 length-prefix framing。

```text
[4-byte length][UTF-8 JSON payload]
```

這可避免訊息邊界不清的問題，對大型 payload 也比依賴分隔符號更安全。

## Request 模型

- request/response，使用 correlation ID
- buffered event 會透過 explicit drain、polling 或 piggyback 欄位呈現
- 具備訊息大小上限與 timeout 控制

## 營運層面的含意

- pipe 名稱由 target process ID 衍生
- client 不應假設多個 server 可以同時共享同一個 target session
- readiness 與注入本身同樣重要；bootstrapper 被載入，不代表 pipe 已經 ready
