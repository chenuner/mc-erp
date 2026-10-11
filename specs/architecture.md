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
- **CurrentUser 上下文** — 通過 ASP.NET Core 的 ClaimsPrincipal 獲取，業務代碼直接用

---

## 外部集成

### 考勤打卡數據

- 通過 `IHttpClientFactory` 調用外部考勤接口
- Hangfire 定時任務拉取打卡數據
- CancellationToken 在 Infrastructure 層統一處理
- 拉取後存入 `attendance_records` 表，按月匯總計算薪酬

### OCR 識別（會計收據）

- 上傳收據照片後，調用外部 OCR 服務識別金額、日期等
- 識別結果需要人工確認後入庫
- 按地盤（Site）分類歸檔
- OCR 服務待選型（Azure Cognitive Services / 雲廠商 API），接口抽象為 `IOcrService`

### 文件存儲

- `IFileStorage` 接口抽象文件讀寫，定義在 `Storage/Services/`
- MVP 階段：`LocalStorageProvider` 實現，文件存本地磁盤
- 未來：換 `S3StorageProvider` / `OssStorageProvider` 只需改 DI 註冊，業務代碼不動
- 文件元數據（名稱、路徑、分類、權限）存數據庫，實際文件內容走 `IFileStorage`
- 上傳時流式計算 MD5，資料庫 `storage_files.Md5` 保存 32 位小寫十六進制摘要，不保存文件內容。歷史文件的 MD5 可為空；磁盤存儲名仍用 Guid，不按 MD5 去重。
