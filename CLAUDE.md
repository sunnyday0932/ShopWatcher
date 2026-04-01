# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

ShopWatcher — 監控 PChome 24h 商品庫存的 .NET 10 Worker Service，透過 Telegram Bot 通知使用者補貨資訊。

## Commands

```bash
# 建置
dotnet build

# 執行全部測試
dotnet test

# 執行單一測試類別
dotnet test --filter "FullyQualifiedName~WatchCommandTests"

# 啟動（需設定 .env）
docker compose up -d
```

## Architecture

兩個 `IHostedService` 並行運作：
- `TelegramBotService`：long polling 接收 `/watch`、`/unwatch`、`/list` 指令，透過 `IServiceScopeFactory` 建立 scope 讀寫 SQLite
- `StockCheckerService`：每 30 秒掃描 WatchItem，對重複 URL 只爬一次（URL 去重），依各 ChatId 獨立發通知，同樣透過 `IServiceScopeFactory` 讀 DB

爬蟲透過 `IScraper` 介面抽象，`PchomeScraper` 為目前唯一實作。新增購物網站只需實作 `IScraper`（實作 `CanHandle` 與 `IsInStockAsync`）並在 `Program.cs` DI 中加入 `AddSingleton<IScraper, NewScraper>()`。

## Testing

- 測試使用 xUnit + NSubstitute + EF Core InMemory provider
- 每個測試透過 `CreateDbWithScope()` 建立獨立的 InMemory DB（dbName 需在 lambda 外先求值，避免每個 scope 得到不同 DB 名稱）
- `TelegramBotService` 與 `StockCheckerService` 的測試透過 `IServiceScopeFactory` 注入，模擬生產環境行為

## Configuration

| 環境變數 | 說明 |
|---|---|
| `Telegram__BotToken` | Telegram Bot API Token（必填） |
| `ConnectionStrings__Default` | SQLite 路徑（預設 `Data Source=shopwatcher.db`） |
| `Checker__IntervalSeconds` | 檢查間隔秒數（預設 30） |
