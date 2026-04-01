# ShopWatcher — 設計文件

**日期：** 2026-04-01
**狀態：** 已確認，待實作

---

## 專案目標

監控 PChome 24h 商品頁面的庫存狀態，當商品補貨時透過 Telegram Bot 通知使用者。

---

## 使用情境

### Bot 指令

| 指令 | 說明 |
|---|---|
| `/watch <url>` | 新增一個商品網址到監控清單 |
| `/unwatch <url>` | 停止監控指定商品 |
| `/list` | 列出目前監控中的所有商品 |

### 使用流程

1. 使用者在 Telegram 對話框輸入 `/watch https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL`
2. 系統每 30 秒檢查一次商品庫存
3. 一旦有貨，Bot 立即發送通知給使用者
4. 只要有貨且使用者未 `/unwatch`，每一輪都會繼續通知
5. 使用者輸入 `/unwatch <url>` 可手動停止監控

### 多使用者行為

- 不同使用者可各自監控同一個商品 URL，互不影響
- A 使用者停止監控某商品，不影響 B 使用者對同一商品的監控
- 系統層面對重複 URL 只爬取一次（HTTP 去重），但通知各自獨立發送

---

## 架構

### 方案

單一 .NET 10 Worker Service + SQLite，打包為單一 Docker container。

### 專案結構

```
ShopWatcher/
├── src/
│   └── ShopWatcher/
│       ├── Program.cs
│       ├── Services/
│       │   ├── TelegramBotService.cs    # IHostedService：處理 Bot 指令（long polling）
│       │   └── StockCheckerService.cs   # IHostedService：每 30 秒定時檢查庫存
│       ├── Scrapers/
│       │   ├── IScraper.cs              # 爬蟲介面
│       │   └── PchomeScraper.cs         # PChome 24h 實作
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   └── Models/
│       │       └── WatchItem.cs
│       └── appsettings.json
├── tests/
│   └── ShopWatcher.Tests/
│       ├── UseCases/
│       │   ├── WatchCommandTests.cs
│       │   ├── UnwatchCommandTests.cs
│       │   ├── ListCommandTests.cs
│       │   └── StockNotificationTests.cs
│       └── Scrapers/
│           └── PchomeScraperTests.cs
├── Dockerfile
├── docker-compose.yml
└── ShopWatcher.sln
```

### 元件說明

**TelegramBotService**
- 以 long polling 方式接收 Telegram 訊息
- 解析 `/watch`、`/unwatch`、`/list` 指令
- 寫入或更新 SQLite 中的 `WatchItem`

**StockCheckerService**
- 每 30 秒執行一次
- 讀取所有 `IsActive = true` 的 WatchItem
- 將 URL 去重複，對每個唯一 URL 呼叫對應的 `IScraper`
- 根據結果對每個 WatchItem 獨立判斷是否發通知

**IScraper / PchomeScraper**

```csharp
public interface IScraper
{
    bool CanHandle(string url);
    Task<bool> IsInStockAsync(string url, CancellationToken ct);
}
```

- `CanHandle`：判斷此 scraper 是否支援該網址（PchomeScraper 比對 `pchome.com.tw`）
- `IsInStockAsync`：透過 PChome API 或 HTML 解析判斷庫存，回傳 `true` 表示有貨
- 錯誤以 exception 往上拋，由 `StockCheckerService` 捕捉並 log，不中斷整個 loop
- 未來新增其他購物網站只需新增對應的 `IScraper` 實作

---

## 資料模型

### WatchItem

| 欄位 | 型別 | 說明 |
|---|---|---|
| `Id` | int (PK) | 自動遞增 |
| `ChatId` | long | Telegram 使用者的 Chat ID |
| `Url` | string | 商品網址 |
| `IsActive` | bool | false 表示已停止監控 |
| `CreatedAt` | DateTime | 新增時間 |

- `ChatId + Url` 加上 unique constraint，同一使用者不重複加相同 URL

---

## 測試案例

### Use Case 測試（UseCases/）

| 測試 | 情境 | 預期結果 |
|---|---|---|
| WatchCommand_AddsWatchItem | 使用者輸入 `/watch <url>` | DB 新增一筆 WatchItem，IsActive = true |
| WatchCommand_Idempotent | 同一使用者重複 `/watch` 相同 URL | 不重複新增，回覆友善提示 |
| UnwatchCommand_DeactivatesItem | 使用者輸入 `/unwatch <url>` | 對應 WatchItem 的 IsActive 設為 false |
| UnwatchCommand_UrlNotFound | `/unwatch` 一個未監控的 URL | 回覆友善錯誤訊息 |
| UnwatchCommand_DoesNotAffectOtherUsers | A 停止監控某 URL | B 對同一 URL 的 WatchItem 不受影響 |
| ListCommand_ReturnsActiveItems | 使用者輸入 `/list` | 只列出該使用者 IsActive = true 的項目 |
| ListCommand_EmptyList | 使用者沒有任何監控項目 | 回覆「目前沒有監控中的商品」 |
| StockNotification_SendsWhenInStock | 商品從無貨變有貨 | 對應使用者收到 Telegram 通知 |
| StockNotification_NoNotificationWhenOutOfStock | 商品持續無貨 | 不發通知 |
| StockNotification_NotifiesAllUsersWatchingSameUrl | A 和 B 都監控同一 URL，商品有貨 | A 和 B 各自收到通知 |
| StockNotification_DeduplicatesHttpRequests | A 和 B 監控同一 URL | 同一輪只對該 URL 發出一次 HTTP 請求 |

### Scraper 測試（Scrapers/）

| 測試 | 情境 | 預期結果 |
|---|---|---|
| PchomeScraper_CanHandle_PchomeUrl | 傳入 pchome.com.tw 網址 | 回傳 true |
| PchomeScraper_CannotHandle_OtherUrl | 傳入非 PChome 網址 | 回傳 false |
| PchomeScraper_ReturnsTrue_WhenInStock | Mock 回應顯示有貨 | 回傳 true |
| PchomeScraper_ReturnsFalse_WhenOutOfStock | Mock 回應顯示缺貨 | 回傳 false |

---

## 部署

### 環境變數（appsettings.json / 環境變數覆寫）

| 變數 | 說明 |
|---|---|
| `Telegram:BotToken` | Telegram Bot API Token |
| `ConnectionStrings:Default` | SQLite 檔案路徑 |
| `Checker:IntervalSeconds` | 檢查間隔（預設 30） |

### Docker Compose 啟動

```bash
docker compose up -d
```

---

## 未來擴充方向（不在初版範圍）

- 支援其他購物網站（新增 `IScraper` 實作即可）
- 每人監控數量上限
- 冷卻時間設定（避免有貨時每 30 秒都通知）
- Web Dashboard
