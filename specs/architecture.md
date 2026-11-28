# architecture.md — 架構規範

> 決定「寫在哪」。分層、模組邊界、依賴流向、物理結構。
>
> 原則：**簡單分層，夠用就好。**
>
> 本文件只放「跨模組的結構約定」。**單一模組的業務規則在各模組文件**，改哪一塊就只讀那一份：
> [考勤](domain/attendance.md)、[會計](domain/accounting.md)、[文件](domain/storage.md)、[系統字典](domain/system-dict.md)。
>
> 哪些已實現、哪些未接線見 [status.md](./status.md)。

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
│   Infrastructure 層（實現）   │  ← EF Core Repository / HttpClient / 文件存儲
└─────────────────────────────┘
```

### 依賴規則

1. **API 層依賴 Application 層** — 只做 HTTP 適配，調 Service
2. **Application 層依賴 Domain 層（實體）** — 業務邏輯在這裡
3. **Infrastructure 層實現 Application 層定義的接口** — 依賴注入反轉
4. **CancellationToken 在 Infrastructure 層處理** — 業務代碼不需要關注

---

## 模組清單

| 模組 | 職責 | 核心實體 | 規則 |
|------|------|---------|------|
| **Identity** | 用戶、角色、權限、JWT 認證、密碼哈希 | User / Role / Permission / UserRole | 見下方「認證 & 授權體系」 |
| **Attendance** | 打卡數據、工數統計、薪酬計算（MPF/OT）、考勤卡掃描 | AttendanceRecord / MonthlyWorkSummary / Worker | [domain/attendance.md](domain/attendance.md) |
| **Accounting** | 收據拍照上傳、OCR 識別、支出按地盤分類、年度提取 | Receipt（分類走 System 的 SysDict） | [domain/accounting.md](domain/accounting.md) |
| **Storage** | 雲盤文件管理、地盤照片存儲、按地盤/時間/事件分類 | FileItem（FilePermission 規劃中） | [domain/storage.md](domain/storage.md) |
| **System** | 系統字典：分組 + 字典項（支出分類、票據類型、文件類別等可配置項） | SysDictGroup / SysDict | [domain/system-dict.md](domain/system-dict.md) |

### 模組間通信

1. **直接調用 Service 接口** — 簡單系統，不走消息總線
2. **跨模組只調 Application 層接口** — 不直接操作對方的 Repository
3. **共享通用組件** — 所有模組用 Common/ 下的基礎設施

---

## 物理目錄結構

```
backend/
├── MC.Erp.slnx                # 唯一 .NET 解決方案入口
├── Dockerfile                  # 打包 CI 發布產物
├── MC.Erp.csproj
├── Program.cs                  # 入口：DI 註冊 + Middleware + Endpoints
├── appsettings.json            # 配置
├── Common/                     # 公共基礎設施
│   ├── Domain/                 # 基類：Entity / AuditableEntity
│   ├── Infrastructure/Ocr/     # IOcrService / 共用 Document Intelligence client / 考勤卡解析
│   ├── Security/               # JWT / 密碼哈希 / 當前用戶上下文
│   └── SeedData.cs             # 啟動時遷移 + 初始管理員
├── Features/                   # 業務模組
│   ├── Identity/
│   │   ├── Endpoints/          # 登錄 / 用戶 CRUD / 角色管理
│   │   ├── Services/           # AuthService / UserService / RoleService
│   │   ├── Entities/           # User / Role / Permission / UserRole / RolePermission
│   │   └── Repositories/       # IUserRepository / IRoleRepository + 實現
│   ├── Attendance/
│   │   ├── Endpoints/          # 工數查詢 / 月結報表 / 打卡表 / 考勤卡掃描
│   │   ├── Services/           # AttendanceServices / AttendanceTableService / AttendanceCardService
│   │   ├── Entities/           # AttendanceRecord / MonthlyWorkSummary / Worker / AttendanceCardScan / AttendanceCardPunch（地盤和職位都用系統字典，沒有獨立實體）
│   │   └── Repositories/       # IAttendanceRepository（只負責員工 CRUD；打卡明細與薪酬表的查詢在 AttendanceTableService 直接用 DbContext）
│   ├── Accounting/
│   │   ├── Endpoints/          # 收據上傳 / 識別 / 保存 / 匯出
│   │   ├── Services/           # ReceiptService / AccountingFileProtectionService
│   │   ├── Entities/           # Receipt
│   │   └── Repositories/       # ReceiptRepository
│   ├── Storage/
│   │   ├── Endpoints/          # 文件上傳下載
│   │   ├── Services/           # IFileStorage / LocalFileStorage / FileService
│   │   └── Entities/           # FileItem
│   └── System/
│       ├── Endpoints/          # 字典分組 + 字典項 CRUD
│       ├── Services/           # SysDictGroupService / SysDictService
│       ├── Entities/           # SysDictGroup / SysDict（SysDict.ParentId 做分組內的層級，只建導航不建 FK）
│       └── Repositories/       # ISysDictGroupRepository / ISysDictRepository + 實現
└── Data/
    └── ErpDbContext.cs         # 單一 DbContext，所有實體在這裡註冊

frontend/
├── src/
│   ├── api/                    # API 調用（Axios 封裝）
│   ├── components/             # 通用組件（DictTreeSelect / ImageCropDialog…）
│   ├── views/                  # 頁面
│   ├── stores/                 # Pinia stores
│   ├── router/                 # 路由
│   ├── utils/                  # 工具函數（dicts.ts / cropImage.ts）
│   ├── composables/            # 組合式函數
│   ├── assets/                 # 靜態資源
│   ├── App.vue
│   └── main.ts
├── package.json
├── Dockerfile                  # 打包 CI 構建產物
├── nginx.conf                  # 生產靜態文件與 API 代理
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
- **不建外鍵（死規定）**：資料庫一律不建 `FOREIGN KEY` 約束。EF 的導航屬性、`Include()` 聯表 SQL 由模型生成，不依賴資料庫約束；清表、刪資料不受約束順序限制。新增關聯時只加欄位 + `HasOne/HasMany`，若 migration 仍產生 FK，就在同一支 migration 用 `ALTER TABLE ... DROP CONSTRAINT IF EXISTS` 補掉；資料完整性由程式保證（寫入前先查存在）。

---

## API 規範

- RESTful 風格
- 路由前綴：`/api/{module}`，如 `/api/attendance`、`/api/accounting`
- 統一返回：`TypedResults` + ProblemDetails（RFC 9457）；業務異常直接 `throw`，由 `Common/Errors/ExceptionProblemFilter` 轉換
- 分頁：`page` + `pageSize` 參數，統一返回 `Common/PagedResult<T>`（`{ items, total, page, pageSize }`）
- 認證：Bearer JWT Token
- 授權：基於角色的 Policy，`[Authorize(Policy = "...")]`

### 標識符：id 為準，code 作可讀別名

對外接口引用「系統字典項」時，**主鍵一律用 `id`（Guid）；`code`（字典的 `Value`）只作可讀別名，兩者並存**，不要只留一種。

- **請求端兩種都收**：查詢參數既收 id 形式（`documentTypeDictId=dd95...`，`Guid` 綁定），也收 code 形式（`documentType=invoice`）；後端先判斷是否為合法 Guid，不是就按 code 查，最後統一解析成字典項 id 再進業務層。
- **響應端兩種都回**：對外的字典引用同時返回 `{ id, code }`。前端用 `id` 發請求、用 `code` 寫日誌／埋點／測試斷言。
- **為什麼不只用 code**：分層字典（如地盤「沙田 / 一樓」）的 `Value` 是**派生值**，`SysDictService.UpdateAsync` 換上級時會重新編號（`2.1` 可能變成 `1.3`）。把 code 當主鍵或持久化引用，等於每次調層級都在改主鍵——已導出的 ZIP 路徑、審計日誌、外部對接全部失效。只有**扁平分組**（`Accounting` 的 `materials` / `transport`、`AccountingDocumentType` 的 `receipt` / `invoice`）的 code 是手動定義且永久的，可以安心當別名。
- **實體（非字典）一律只用 id**：用戶、員工、票據、文件這些由用戶建立的實體，名稱會變（改名、離職重入職），`workerId` / `userId` 這類 GUID 不能被姓名或工號取代。
- `code` **不可用作持久化引用**（不寫進業務表、不進 ZIP 文件名、不進外部系統的回調參數），只活在請求與響應的「可讀」層。
- 參考：Google [AIP-122](https://google.aip.dev/122) 要求資源 ID 是字串且可讀（`books/les-miserables`），同時保留系統生成的 `uid`；Stripe 則用不透明前綴 ID（`cus_xxx`）避免被枚舉。兩種思路的共同點是「對外識別碼要穩定、唯一、不可重用」，可讀性只是加分項。

---

## 認證 & 授權體系

```
User ─┬─ UserRole ── Role ── RolePermission ── Permission
      │
      └─ JWT Token（包含 userId, roles, permissions）
```

- **User** — 用戶賬號，Argon2id 密碼哈希；種子 `admin` 帶 `IsProtected = true`（唯一的主管理員，不可刪除、不可停用、不可移除 Admin 角色）
- **Role** — 角色（如：管理員、會計、地盤主管、普通員工）
- **Permission** — 權限項（如：attendance.view, accounting.edit）
- **UserRole** — 用戶-角色多對多
- **RolePermission** — 角色-權限多對多
- **JWT** — 登錄後簽發，包含角色和權限信息
- **身份展示** — `sub` 是用戶 ID，不用作姓名。前端 `username` 讀取名字聲明（兼容 `ClaimTypes.Name` 的完整 URI）；`displayName` 優先讀取 JWT 的 `display_name`，未設顯示名或使用舊令牌時回退到用戶名。JWT payload 按 UTF-8 解碼，僅供展示，不代替後端驗證。顯示名變更後需重新登錄取得新令牌。
- **CurrentUser 上下文** — 通過 ASP.NET Core 的 ClaimsPrincipal 獲取，業務代碼直接用
- **登錄限流** — `POST /api/identity/login` 是全系統最貴的計算（Argon2id 每次約 300ms CPU），必須限流：按客戶端 IP 分桶，20 次/分鐘、固定窗口，超額回 `429` + ProblemDetails + `Retry-After`（策略定義在 `Common/Security/LoginRateLimit`）。客戶端 IP 由 `Common/Http/ClientIpForwarding` 解析 `X-Forwarded-For`：生產環境是「宿主 nginx → web nginx → backend」兩跳，兩層都用 `$proxy_add_x_forwarded_for` 追加，故取最後 2 項（`ForwardLimit = 2`）；客戶端自己塞的值只會落在最左邊，取不到，偽造無效。沒有這層轉換時 `RemoteIpAddress` 永遠是 127.0.0.1，全站會共用同一個桶。

---

## 業務規則去哪讀

| 你在改 | 讀哪份 |
|--------|--------|
| 打卡、工數、薪酬公式、考勤卡掃描 | [domain/attendance.md](domain/attendance.md) |
| 收據、OCR、裁剪上傳、ZIP 提取 | [domain/accounting.md](domain/accounting.md) |
| 文件上傳、MD5 去重、下載 | [domain/storage.md](domain/storage.md) |
| 字典分組、層級、樹形下拉、分類篩選、停用 | [domain/system-dict.md](domain/system-dict.md) |
