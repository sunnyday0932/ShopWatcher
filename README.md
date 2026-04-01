# ShopWatcher

監控 PChome 24h 商品庫存，補貨時透過 Telegram Bot 即時通知。

## 功能

- 透過 Telegram Bot 管理監控清單，無需任何 UI
- 每 30 秒自動檢查商品庫存狀態
- 支援多位使用者，各自管理獨立的監控清單
- 多人監控同一商品時，HTTP 請求自動去重，降低對 PChome 的請求壓力

## Bot 指令

| 指令 | 說明 |
|---|---|
| `/watch <url>` | 新增商品到監控清單 |
| `/unwatch <url>` | 停止監控指定商品 |
| `/list` | 查看目前監控中的所有商品 |

**範例：**
```
/watch https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL
```

## 快速開始

### 前置需求

- Docker & Docker Compose
- Telegram Bot Token（透過 [@BotFather](https://t.me/BotFather) 建立）

### 設定

建立 `.env` 檔案：

```env
TELEGRAM__BOTTOKEN=your_bot_token_here
```

### 啟動

```bash
docker compose up -d
```

## 開發

### 需求

- .NET 10 SDK
- JetBrains Rider 或 Visual Studio

### 執行測試

```bash
dotnet test
```

### 執行單一測試

```bash
dotnet test --filter "FullyQualifiedName~WatchCommand"
```

## 設計文件

詳細架構與測試案例說明見 [`docs/superpowers/specs/2026-04-01-shopwatcher-design.md`](docs/superpowers/specs/2026-04-01-shopwatcher-design.md)。
