# ShopWatcher

監控 PChome 24h 商品庫存，補貨時透過 Telegram Bot 即時通知。支援 inline.app 餐廳自動訂位。

## 功能

- 透過 Telegram Bot 管理監控清單，無需任何 UI
- 每 30 秒自動檢查商品庫存狀態
- 支援多位使用者，各自管理獨立的監控清單
- 多人監控同一商品時，HTTP 請求自動去重，降低對 PChome 的請求壓力
- 每日 00:00 自動以 Playwright 操控瀏覽器嘗試 inline.app 餐廳訂位

## Bot 指令

### 商品庫存監控

| 指令 | 說明 |
|---|---|
| `/watch <url>` | 新增商品到監控清單 |
| `/unwatch <url>` | 停止監控指定商品 |
| `/list` | 查看目前監控中的所有商品 |

**範例：**
```
/watch https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL
```

### 自動訂位（inline.app）

| 指令 | 說明 |
|---|---|
| `/reservation_add <name> <phone> <partysize> <url> [lookahead]` | 新增訂位設定（lookahead 預設 14 天）|
| `/reservation_remove <url>` | 移除訂位設定 |
| `/reservation_list` | 查看目前所有啟用的訂位設定 |
| `/reservation_activate <url>` | 啟用指定餐廳的自動訂位 |
| `/reservation_deactivate <url>` | 暫停指定餐廳的自動訂位 |
| `/triggerbooking` | 立即手動執行一次訂位檢查 |

**範例：**
```
/reservation_add 王小明 0912345678 2 https://inline.app/booking/... 14
```

每日 00:00（UTC）自動掃描所有啟用的訂位設定，往後 `lookahead` 天逐日嘗試訂位，成功後停止。

## 快速開始

### 前置需求

- Docker & Docker Compose
- Telegram Bot Token（透過 [@BotFather](https://t.me/BotFather) 建立）

### 設定

建立 `.env` 檔案：

```env
TELEGRAM__BOTTOKEN=your_bot_token_here
```

選填（訂位功能）：

```env
RESERVATION__DRYRUN=true   # true = 不真正送出訂位（預設），false = 實際送出
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

### 執行 E2E 測試（需要真實瀏覽器）

```bash
RUN_E2E=true E2E_RESTAURANT_URL=https://inline.app/booking/... dotnet test --filter "Category=E2E"
```

## 設計文件

- 商品監控架構：[`docs/superpowers/specs/2026-04-01-shopwatcher-design.md`](docs/superpowers/specs/2026-04-01-shopwatcher-design.md)
- 自動訂位設計：[`docs/superpowers/specs/2026-04-08-reservation-design.md`](docs/superpowers/specs/2026-04-08-reservation-design.md)
