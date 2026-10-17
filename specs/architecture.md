# architecture.md — 架構規範

> 決定"寫在哪"。分層、模組邊界、依賴流向、物理結構。
>
> 原則：**簡單分層，夠用就好。**

---

## 命名規範

| 層 | 規則 | 說明 |
|---|------|------|
| 基礎設施（Common/ + Identity） | 英文命名 | 通用組件，方便複用，遵循 .NET 慣例 |
| 業務模組（Attendance / Accounting / Storage） | 允許中文方法名、中文變量名 | 業務邏輯用中文更清晰，降低溝通成本 |

> Identity 視為基礎設施，是通用體系，用英文。業務代碼用中文方便為主，不求統一風格。

---

## 總體架構

簡單分層架構（Layered Architecture），單項目多模組。

不用 DDD 聚合根、不用 CQRS、不用 Contracts 層。模組間可直接調用 Service 接口。

---

## 分層結構

```
┌─────────────────────────────┐
│      API 層（Endpoints）     │  ← HTTP 入口，薄：參數綁定 + 調 Service
├─────────────────────────────┤
│    Application 層（Service）  │  ← 業務邏輯編排，調 Repository / 外部 API
├─────────────────────────────┤
│   Infrastructure 層（實現）   │  ← EF Core Repository / HttpClient / Hangfire
└─────────────────────────────┘
```

### 依賴規則

1. **API 層依賴 Application 層** — 只做 HTTP 適配，調 Service
2. **Application 層依賴 Domain 層（實體）** — 業務邏輯在這裡
3. **Infrastructure 層實現 Application 層定義的接口** — 依賴注入反轉
4. **CancellationToken 在 Infrastructure 層處理** — 業務代碼不需要關注

---

## 模組清單

| 模組 | 職責 | 核心實體 |
|------|------|---------|
| **Identity** | 用戶、角色、權限、JWT 認證、密碼哈希 | User / Role / Permission / UserRole |
| **Attendance** | 外部打卡數據拉取、工數統計、薪酬計算（MPF/OT）、月結報表 | AttendanceRecord / MonthlyWorkSummary / Worker |
| **Accounting** | 收據拍照上傳、OCR 識別、支出按地盤分類、年度提取 | Receipt / ExpenseCategory / Site |
| **Storage** | 雲盤文件管理、地盤照片存儲、按地盤/時間/事件分類、賬戶權限。文件存儲抽象為 `IFileStorage` 接口，MVP 階段用本地存儲實現，跑通後換對象存儲只需換實現 | FileItem / FilePermission / SitePhoto |

### 模組間通信

1. **直接調用 Service 接口** — 簡單系統，不走消息總線
2. **跨模組只調 Application 層接口** — 不直接操作對方的 Repository
3. **共享通用組件** — 所有模組用 Common/ 下的基礎設施

---

## 物理目錄結構

```
backend/
├── MC.Erp.csproj
├── Program.cs                  # 入口：DI 註冊 + Middleware + Endpoints
├── appsettings.json            # 配置
├── Common/                     # 公共基礎設施
│   ├── Domain/                 # 基類：Entity / AuditableEntity
│   ├── Infrastructure/         # EF Core 基類 / Repository 基類 / Middleware
│   ├── Security/               # JWT / 認證擴展 / 當前用戶上下文
│   └── Exceptions/             # 統一異常處理
├── Features/                   # 業務模組
│   ├── Identity/
│   │   ├── Endpoints/         # 登錄 / 用戶 CRUD / 角色管理
│   │   ├── Services/           # AuthService / UserService / RoleService
│   │   ├── Entities/           # User / Role / Permission
│   │   └── Repositories/       # IUserRepository
│   ├── Attendance/
│   │   ├── Endpoints/          # 工數查詢 / 月結報表
│   │   ├── Services/           # AttendanceSyncService / PayrollService
│   │   ├── Entities/           # AttendanceRecord / MonthlyWorkSummary / Worker
│   │   ├── Repositories/       # IAttendanceRepository
│   │   └── External/           # 外部打卡 API 客戶端
│   ├── Accounting/
│   │   ├── Endpoints/          # 收據上傳 / 支出查詢
│   │   ├── Services/           # ReceiptService / OcrService
│   │   ├── Entities/           # Receipt / ExpenseCategory
│   │   └── Repositories/
│   └── Storage/
│       ├── Endpoints/          # 文件上傳下載 / 權限管理
│       ├── Services/           # IFileStorage / LocalStorageProvider / FileService / SitePhotoService
│       ├── Entities/           # FileItem / FilePermission / SitePhoto
│       └── Repositories/
└── Data/
    └── ErpDbContext.cs         # 單一 DbContext，所有實體在這裡註冊

frontend/
├── src/
│   ├── api/                    # API 調用（Axios 封裝）
│   ├── components/             # 通用組件
│   ├── views/                  # 頁面
│   ├── stores/                 # Pinia stores
│   ├── router/                 # 路由
│   ├── utils/                  # 工具函數
│   ├── composables/            # 組合式函數
│   ├── assets/                 # 靜態資源
│   ├── App.vue
│   └── main.ts
├── package.json
├── vite.config.ts
└── tsconfig.json
```

---

## 數據庫

- **單一 DbContext** — `ErpDbContext`，所有實體統一註冊
- 所有表在同一個 PostgreSQL 數據庫中
- 表命名：`{module_}table_name`（下劃線小寫），如 `identity_users`、`attendance_records`
- 主鍵：`Guid` 類型，字段名 `id`
- 審計字段：`created_at` / `updated_at`
- 軟刪除：`is_deleted` + `deleted_at`（僅需要軟刪除的實體）

---

## API 規範

- RESTful 風格
- 路由前綴：`/api/{module}`，如 `/api/attendance`、`/api/accounting`
- 統一返回：`TypedResults` + ProblemDetails（RFC 9457）
- 分頁：`page` + `pageSize` 參數，返回 `{ items, total, page, pageSize }`
- 認證：Bearer JWT Token
- 授權：基於角色的 Policy，`[Authorize(Policy = "...")]`

---

## 認證 & 授權體系

完整的 Role-User 體系：

```
User ─┬─ UserRole ── Role ── RolePermission ── Permission
      │
      └─ JWT Token（包含 userId, roles, permissions）
```

- **User** — 用戶賬號，Argon2id 密碼哈希
- **Role** — 角色（如：管理員、會計、地盤主管、普通員工）
- **Permission** — 權限項（如：attendance.view, accounting.edit）
- **UserRole** — 用戶-角色多對多
- **RolePermission** — 角色-權限多對多
- **JWT** — 登錄後簽發，包含角色和權限信息
- **身份展示** — `sub` 是用戶 ID，不用作姓名。前端 `username` 讀取名字聲明（兼容 `ClaimTypes.Name` 的完整 URI）；`displayName` 優先讀取 JWT 的 `display_name`，未設顯示名或使用舊令牌時回退到用戶名。JWT payload 按 UTF-8 解碼，僅供展示，不代替後端驗證。顯示名變更後需重新登錄取得新令牌。
- **CurrentUser 上下文** — 通過 ASP.NET Core 的 ClaimsPrincipal 獲取，業務代碼直接用

---

## 外部集成

### 考勤打卡數據

- 現階段按月提供員工薪酬表和打卡明細；管理員可編輯並保存，注意事項最多 255 字元。金額使用 `decimal(18,2)`，工數和小時使用 `decimal(8,2)`。
- 真實打卡機未對接：開發環境管理員通過 `/api/attendance/mock?year=...&month=...` 手動初始化模擬數據，重複初始化不覆蓋已有月結和打卡修正；正式環境不開放初始化。
- 打卡時間入庫使用 UTC，查詢月份及前端展示按香港時區（UTC+8）。薪酬樣例金額與打卡明細獨立，計薪規則確認前不自動計算，也不啟用定時同步。
- 通過 `IHttpClientFactory` 調用外部考勤接口
- Hangfire 定時任務拉取打卡數據
- CancellationToken 在 Infrastructure 層統一處理
- 拉取後存入 `attendance_records` 表，按月匯總計算薪酬

### OCR 識別（會計收據）

- 原圖和票據草稿先保存，使用 Azure Document Intelligence `prebuilt-receipt` / `prebuilt-invoice` 識別，結果須人工確認。未識別的日期、金額及幣種保持空值。
- `IOcrService` 位於 Common/Infrastructure/Ocr，使用官方 C# SDK，憑據只在後端配置；收據中文識別須實測，不能保證準確率。
- `Receipt.SiteId` 引用考勤地盤，表示實際支出歸屬；`SiteCategoryDictId` 引用 `AccountingSiteCategory` 字典，僅表示圖片提取分類，兩者獨立。
- `DocumentTypeDictId` 引用 `AccountingDocumentType`（receipt / invoice）；圖片 `FileItem.Type` 引用 `Accounting` 支出分類，分類不在票據表重複保存。
- 金額 decimal(18,2)，票據日期 DateOnly；發票金額取 InvoiceTotal，不取可能含歷史欠款的 AmountDue，也不自動視為已付款。
- 僅 Admin 操作；會計圖片不能經通用 Storage 接口繞過權限或刪除。識別失敗保留原圖並可重試，識別不得覆寫已人工修改資料。
- 按票據日期（含起止日）、票據類型、支出分類、導出地盤分類及可選實際地盤提取全部匹配原圖為 ZIP，不限當前頁，不輸出 CSV。MD5 僅作核對，不是文件地址或唯一鍵。
- ZIP 使用臨時文件，預設上限 5000 張 / 250 MB 原圖，完成或失敗後清理；第一版單圖上限 4 MB，與 Azure F0 相容。

### 文件存儲

- `IFileStorage` 接口抽象文件讀寫，定義在 `Storage/Services/`
- MVP 階段：`LocalStorageProvider` 實現，文件存本地磁盤
- 未來：換 `S3StorageProvider` / `OssStorageProvider` 只需改 DI 註冊，業務代碼不動
- 文件元數據（名稱、路徑、分類、權限）存數據庫，實際文件內容走 `IFileStorage`
- 上傳時流式計算 MD5，資料庫 `storage_files.Md5` 保存 32 位小寫十六進制摘要，不保存文件內容。歷史文件的 MD5 可為空；磁盤存儲名仍用 Guid，不按 MD5 去重。
