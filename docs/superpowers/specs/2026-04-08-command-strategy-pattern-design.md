# Telegram 指令系統 - 策略模式重構設計文件

> **日期:** 2026-04-08  
> **狀態:** 已批准，待實現

## 目標

將 `TelegramBotService` 中的多個 if/else if 分支（目前 3 個，新增後為 9 個）重構為策略模式架構，提升代碼可維護性、可測試性和擴展性。

## 問題分析

現狀：
- `HandleUpdateAsync` 包含大量 if/else if 分支，每增加一個指令就需修改此方法
- 指令處理邏輯混在 TelegramBotService 中，不利於隔離測試
- 參數驗證、異常處理、訊息回覆邏輯散落各處

目標指令數：9 個
- 庫存監控：/watch、/unwatch、/list（3 個）
- 訂位管理：/reservation_add、/reservation_remove、/reservation_list、/reservation_activate、/reservation_deactivate、/triggerbooking（6 個）

## 設計方案：泛型 + 命令 DTO + 策略模式

### 核心原則

1. **指令標準化** - 每個指令定義一個 Command DTO（ICommand 實現），明確化參數
2. **Handler 隔離** - 每個指令對應一個獨立的 Handler 類（ICommandHandler<TCommand>）
3. **統一路由** - TelegramBotService 簡化為：解析 → 構建 → 執行 → 回覆
4. **集中驗證** - CommandBuilder 統一驗證參數，拋出 CommandException
5. **集中異常** - TelegramBotService 統一 catch 異常，發送統一格式的錯誤訊息

### 架構圖

```
User Message
    ↓
TelegramBotService.HandleUpdateAsync
    ├─ CommandParser.Parse(text)           → ParsedCommand { Name, Args[] }
    ├─ CommandBuilder.Build(...)           → ICommand（具體 Command 對象）
    ├─ CommandExecutor.ExecuteAsync(...)   → CommandResult { Success, Message }
    └─ SendMessageAsync(result.Message)    → 回覆用戶
    
例外處理：CommandException → 發送錯誤訊息給用戶
```

## 核心組件

### 1. 標記介面與 Command DTO

**ICommand.cs** - 所有 command 必須實現
```csharp
public interface ICommand
{
    long ChatId { get; }
}
```

**Command DTO 清單：**
- `WatchCommand { ChatId, Url }`
- `UnwatchCommand { ChatId, Url }`
- `ListCommand { ChatId }`
- `ReservationAddCommand { ChatId, Name, Phone, PartySize, Url, LookAheadDays }`
- `ReservationRemoveCommand { ChatId, Url }`
- `ReservationListCommand { ChatId }`
- `ReservationActivateCommand { ChatId, Url }`
- `ReservationDeactivateCommand { ChatId, Url }`
- `TriggerBookingCommand { ChatId }`

### 2. 泛型 Handler 介面

**ICommandHandler<TCommand>.cs**
```csharp
public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken ct);
}

public class CommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

### 3. 指令解析

**CommandParser.cs** - 將文本分解為指令名和參數
```
"/watch https://example.com" → ParsedCommand("/watch", ["https://example.com"])
"/reservation_add John 09123456 2 https://inline.app/booking/xxx" 
  → ParsedCommand("/reservation_add", ["John", "09123456", "2", "https://inline.app/booking/xxx"])
```

### 4. 指令構建與驗證

**CommandBuilder.cs** - 將解析後的參數構建成 Command DTO
- 驗證參數個數
- 驗證參數類型（如 partySize 必須是 int）
- 拋出 CommandException 若驗證失敗
- 返回具體的 Command 對象

**支援的指令映射：**
```
"/watch" → WatchCommand
"/unwatch" → UnwatchCommand
"/list" → ListCommand
"/reservation_add" → ReservationAddCommand（驗證：name, phone, partysize(int), url, [lookahead]）
"/reservation_remove" → ReservationRemoveCommand
"/reservation_list" → ReservationListCommand
"/reservation_activate" → ReservationActivateCommand
"/reservation_deactivate" → ReservationDeactivateCommand
"/triggerbooking" → TriggerBookingCommand
```

### 5. 指令執行器

**CommandExecutor.cs** - 反射解析泛型 handler，執行業務邏輯
- 從 DI 容器解析 `ICommandHandler<TCommand>`
- 調用 `ExecuteAsync(command, ct)`
- 捕捉異常並轉換為 CommandException

### 6. Handler 實現

每個 handler 是獨立的類，示例結構：

```csharp
public class WatchCommandHandler(AppDbContext db, ILogger<WatchCommandHandler> logger) 
    : ICommandHandler<WatchCommand>
{
    public async Task<CommandResult> ExecuteAsync(WatchCommand command, CancellationToken ct)
    {
        // 業務邏輯：
        // 1. 查詢是否已存在
        // 2. 新增或激活
        // 3. 返回 CommandResult { Success = true, Message = "✅ 已開始監控..." }
        
        // 異常處理：
        // logger.LogError(...) + throw new CommandException("❌ 執行指令失敗...")
    }
}
```

### 7. 重構後的 TelegramBotService

```csharp
public class TelegramBotService(
    ITelegramBotClient botClient,
    CommandExecutor commandExecutor,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;
        var chatId = update.Message.Chat.Id;

        try
        {
            var parsedCommand = CommandParser.Parse(text);
            var command = CommandBuilder.Build(parsedCommand.Name, parsedCommand.Args, chatId);
            var result = await commandExecutor.ExecuteAsync(command, ct);
            await SendMessageAsync(chatId, result.Message, ct);
        }
        catch (CommandException ex)
        {
            await SendMessageAsync(chatId, ex.Message, ct);
            logger.LogWarning("指令執行失敗: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            await SendMessageAsync(chatId, "❌ 發生未預期的錯誤，請稍後重試", ct);
            logger.LogError(ex, "處理 Telegram 更新時發生錯誤");
        }
    }
}
```

## 文件結構

```
src/ShopWatcher/Commands/
├── ICommand.cs
├── CommandParser.cs
├── CommandBuilder.cs
├── CommandExecutor.cs
├── CommandException.cs
├── CommandResult.cs
├── DTOs/
│   ├── WatchCommand.cs
│   ├── UnwatchCommand.cs
│   ├── ListCommand.cs
│   ├── ReservationAddCommand.cs
│   ├── ReservationRemoveCommand.cs
│   ├── ReservationListCommand.cs
│   ├── ReservationActivateCommand.cs
│   ├── ReservationDeactivateCommand.cs
│   └── TriggerBookingCommand.cs
├── Handlers/
│   ├── ICommandHandler.cs
│   ├── WatchCommandHandler.cs
│   ├── UnwatchCommandHandler.cs
│   ├── ListCommandHandler.cs
│   ├── ReservationAddCommandHandler.cs
│   ├── ReservationRemoveCommandHandler.cs
│   ├── ReservationListCommandHandler.cs
│   ├── ReservationActivateCommandHandler.cs
│   ├── ReservationDeactivateCommandHandler.cs
│   └── TriggerBookingCommandHandler.cs
```

## DI 整合

Program.cs：
```csharp
builder.Services.AddScoped<CommandExecutor>();

// 註冊所有 Handlers
builder.Services.AddScoped<ICommandHandler<WatchCommand>, WatchCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UnwatchCommand>, UnwatchCommandHandler>();
// ... 其他 handlers
```

## 優勢

| 方面 | 優勢 |
|------|------|
| **可維護性** | 每個指令邏輯獨立封裝，改動不影響其他指令 |
| **可擴展性** | 新增指令只需：新增 Command DTO + Handler，無需修改路由 |
| **可測試性** | Handler 可單獨測試，無需 mock Telegram Bot |
| **型別安全** | 指令參數明確化為 DTO，編譯時檢查 |
| **異常處理** | 統一在 TelegramBotService 中處理，訊息一致 |
| **代碼清晰** | TelegramBotService 的 HandleUpdateAsync 只有 10 行核心邏輯 |

## 後續實現步驟

1. **第一階段** - 建立基礎架構
   - ICommand、ICommandHandler、CommandException、CommandResult
   - CommandParser、CommandBuilder、CommandExecutor
   
2. **第二階段** - 建立 9 個 Command DTO
   
3. **第三階段** - 實現 9 個 Handler
   - 複用現有的業務邏輯，只是換個位置
   
4. **第四階段** - 重構 TelegramBotService
   - 移除舊的 if/else if 邏輯
   - 保留 IServiceScopeFactory（Scope 改在 Handlers 中處理）
   
5. **第五階段** - 更新 Program.cs 和測試
   - 註冊所有 handlers
   - 為每個 handler 新增單元測試

## 備註

- IServiceScopeFactory 將從 TelegramBotService 移除，改在各 Handler 中注入 AppDbContext（已是 scoped）
- TriggerBookingCommandHandler 會注入 IReservationRunner，由它呼叫排程服務
- 所有 Command DTO 和 Handler 都放在同一層級，方便尋找和維護
