# Reservation Auto-Booker 設計文件

**日期：** 2026-04-08  
**功能：** 自動訂位排程（初始支援 inline.app）

---

## 背景與目標

在既有 ShopWatcher 架構中新增自動訂位功能。程式使用 Playwright 操控瀏覽器，每日 00:00 自動檢查指定餐廳在未來 N 天內的週五、六、日是否有確認位子（非候補），若有則自動填入訂位資料完成訂位，並透過 Telegram Bot 通知使用者。

---

## 架構

### 新增元件

```
src/ShopWatcher/
├── Bookers/
│   ├── IReservationBooker.cs       # 訂位介面
│   ├── InlineReservationBooker.cs  # inline.app Playwright 實作
│   └── BookingResult.cs            # 結果 DTO
├── Data/Models/
│   └── ReservationConfig.cs        # DB 模型
└── Services/
    └── ReservationService.cs       # IHostedService，每日 00:00 執行
```

### 現有檔案異動

- `TelegramBotService.cs`：新增 `/setbooking`、`/setrange`、`/bookinginfo`、`/startbooking`、`/stopbooking`、`/triggerbooking` 指令
- `AppDbContext.cs`：新增 `ReservationConfigs` DbSet
- `Program.cs`：註冊 `ReservationService`、`InlineReservationBooker`、Playwright

---

## 介面定義

```csharp
public interface IReservationBooker
{
    bool CanHandle(string url);
    Task<BookingResult> BookAsync(string url, ReservationInfo info, CancellationToken ct);
}

public record ReservationInfo(
    string Name,
    string Phone,
    int PartySize,
    DateOnly Date);

public record BookingResult(
    bool Success,
    bool WasWaitlist,
    DateOnly? BookedDate,
    TimeOnly? BookedTime,
    string? ErrorMessage);
```

---

## 資料模型

```csharp
public class ReservationConfig
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public string RestaurantUrl { get; set; } = string.Empty;
    public int LookAheadDays { get; set; } = 14;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

每個 `ChatId` 對應一筆設定（upsert 語意）。

---

## Telegram 指令

| 指令 | 格式 | 說明 |
|------|------|------|
| `/setbooking` | `/setbooking <url> <name> <phone> <size>` | 新增或更新訂位設定，啟用排程 |
| `/setrange` | `/setrange <days>` | 設定查詢未來天數（正整數） |
| `/bookinginfo` | `/bookinginfo` | 顯示目前設定與排程狀態 |
| `/startbooking` | `/startbooking` | 啟用排程（`IsActive = true`） |
| `/stopbooking` | `/stopbooking` | 停用排程（`IsActive = false`） |
| `/triggerbooking` | `/triggerbooking` | 立即手動執行一次（忽略 `IsActive` 狀態） |

---

## 排程執行流程

```
每日 00:00（UTC+8）
  → 讀取所有 IsActive = true 的 ReservationConfig
  → 若無任何設定 → 靜默結束

  對每筆 ReservationConfig：
    → 計算未來 LookAheadDays 天內的週五六日清單（依日期升冪排序）
    → 若無符合日期 → 靜默略過

    依序嘗試每個日期：
      → 呼叫 IReservationBooker.BookAsync(url, info, date)
      → WasWaitlist = true  → 跳過，試下一天
      → Success = true      → 發 Telegram 通知、IsActive = false、停止
      → Success = false     → 跳過，試下一天
      → 拋出例外           → LogWarning，試下一天

    所有日期皆無成功 → 靜默結束
```

### 午夜排程實作

`ReservationService` 使用 `PeriodicTimer` + 計算到下一個午夜（Asia/Taipei，UTC+8）的等待時間，不依賴 Quartz 或 Hangfire 等外部排程套件。

---

## InlineReservationBooker 行為

1. 使用 Playwright 開啟訂位頁面
2. 選擇對應日期
3. 掃描所有可用時段：
   - 若全為候補 → 回傳 `WasWaitlist=true, Success=false`
   - 若無任何時段 → 回傳 `Success=false, WasWaitlist=false`
   - 若有確認位子 → 選最早的時段
4. 填入姓名、電話、人數
5. 送出訂位
6. 確認頁面出現成功訊息 → 回傳 `Success=true, BookedDate, BookedTime`

---

## Telegram 通知格式

### 訂位成功
```
✅ 訂位成功！
餐廳：<url>
日期：2026-04-10（週五）
時段：18:00
人數：2 位
姓名：王小明

已暫停自動訂位排程。如需再次啟用請輸入 /startbooking
```

---

## 測試案例

### ReservationService

| # | 情境 | 預期行為 |
|---|------|----------|
| R1 | 沒有任何 ReservationConfig | 靜默略過，不呼叫 Booker |
| R2 | 未來 N 天內沒有週五六日 | 靜默略過 |
| R3 | 找到第一個有確認位子的日期 | 訂位成功，發 Telegram 通知，IsActive 設為 false |
| R4 | 前幾天是候補，後面某天有確認位 | 跳過候補，訂第一個確認位 |
| R5 | 所有日期都是候補 | 不訂位、不通知 |
| R6 | 所有日期都無位（非候補） | 不訂位、不通知 |
| R7 | Booker 拋出例外 | LogWarning，繼續試下一天，不崩潰 |

### IReservationBooker / InlineReservationBooker

> B1-B2 為單元測試；B3-B5 為 E2E 測試（需真實 Playwright + inline.app 真實頁面），`ReservationService` 單元測試改用 mock `IReservationBooker`。

#### E2E 測試設計

**有頭模式（Headed mode）：** E2E 測試一律以有頭模式（非 headless）執行，讓測試人員能看到瀏覽器的實際操作畫面確認流程正確。

**送出開關（DryRun）：** `InlineReservationBooker` 接受一個 `DryRun` 旗標：
- `DryRun = true`（預設）：流程執行到按下確認按鈕前停止，不送出訂位，回傳 `Success=false, DryRun=true`
- `DryRun = false`：真正按下確認按鈕送出訂位

此旗標透過 `appsettings` 或環境變數控制（`Reservation:DryRun`），預設為 `true`。測試時保持預設值即可安全執行而不產生真實訂位。

#### BookingResult 補充欄位

```csharp
public record BookingResult(
    bool Success,
    bool WasWaitlist,
    bool DryRun,              // 是否為 DryRun 模式（未真正送出）
    DateOnly? BookedDate,
    TimeOnly? BookedTime,
    string? ErrorMessage);
```

| # | 情境 | 預期行為 |
|---|------|----------|
| B1 | `CanHandle` 傳入 inline.app URL | 回傳 `true` |
| B2 | `CanHandle` 傳入非 inline.app URL | 回傳 `false` |
| B3 | 頁面有確認位子，DryRun=true（E2E） | 填入資料但不送出，回傳 `Success=false, DryRun=true`，瀏覽器畫面停在確認頁 |
| B4 | 頁面有確認位子，DryRun=false（E2E） | 填入資料、按下送出，回傳 `Success=true, DryRun=false` |
| B5 | 頁面只有候補（E2E） | 不填不送，回傳 `WasWaitlist=true, Success=false` |
| B6 | 頁面完全無位（E2E） | 回傳 `Success=false, WasWaitlist=false` |

### TelegramBotService 新增指令

| # | 指令 | 情境 | 預期行為 |
|---|------|------|----------|
| T1 | `/setbooking` | 首次設定 | 儲存到 DB，回覆確認 |
| T2 | `/setbooking` | 覆蓋既有設定 | 更新 DB，回覆新設定內容 |
| T3 | `/setbooking` | 格式錯誤 | 回覆使用說明 |
| T4 | `/setrange 21` | 已有設定，更新天數 | 儲存到 DB，回覆確認 |
| T4b | `/setrange 21` | 尚未設定（無 ReservationConfig） | 回覆「請先執行 /setbooking」 |
| T5 | `/bookinginfo` | 已有設定 | 顯示設定值與排程狀態 |
| T6 | `/bookinginfo` | 尚未設定 | 回覆「尚未設定」提示 |
| T7 | `/triggerbooking` | 已有設定 | 立即執行一次訂位流程 |
| T8 | `/triggerbooking` | 尚未設定 | 回覆「請先執行 /setbooking」 |
| T9 | `/stopbooking` | 排程啟用中 | IsActive = false，回覆確認 |
| T10 | `/startbooking` | 排程停用中 | IsActive = true，回覆確認 |
| T11 | `/triggerbooking` | 排程已停用 | 仍可執行，不受 IsActive 限制 |

---

## 設定（appsettings）

```json
{
  "Reservation": {
    "TimeZone": "Asia/Taipei",
    "DryRun": true
  }
}
```

| 設定鍵 | 預設值 | 說明 |
|--------|--------|------|
| `Reservation:TimeZone` | `Asia/Taipei` | 排程午夜時區 |
| `Reservation:DryRun` | `true` | `true` 時流程執行至送出前停止，不產生真實訂位 |

個人訂位資訊（姓名、電話、人數、URL、LookAheadDays）全部儲存在 DB，可透過 Telegram 指令設定。

---

## 依賴套件

- `Microsoft.Playwright`（Playwright for .NET）
- 不需要額外排程套件（使用原生 `PeriodicTimer`）
