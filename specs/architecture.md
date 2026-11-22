# architecture.md — 架構規範

> 決定"寫在哪"。分層、模組邊界、依賴流向、物理結構。
>
> 原則：**簡單分層，夠用就好。**
>
> 本文件描述目標設計；哪些已實現、哪些未接線見 [status.md](./status.md)。

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

| 模組 | 職責 | 核心實體 |
|------|------|---------|
| **Identity** | 用戶、角色、權限、JWT 認證、密碼哈希 | User / Role / Permission / UserRole |
| **Attendance** | 外部打卡數據拉取、工數統計、薪酬計算（MPF/OT）、月結報表 | AttendanceRecord / MonthlyWorkSummary / Worker |
| **Accounting** | 收據拍照上傳、OCR 識別、支出按地盤分類、年度提取 | Receipt（分類走 System 的 SysDict） |
| **Storage** | 雲盤文件管理、地盤照片存儲、按地盤/時間/事件分類、賬戶權限。文件存儲抽象為 `IFileStorage` 接口，MVP 階段用本地存儲實現，跑通後換對象存儲只需換實現 | FileItem（FilePermission / SitePhoto 規劃中） |
| **System** | 系統字典：分組 + 字典項（支出分類、票據類型、文件類別等可配置項） | SysDictGroup / SysDict |

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
│   │   ├── Endpoints/          # 工數查詢 / 月結報表 / 打卡表 / 考勤卡掃描 / mock
│   │   ├── Services/           # AttendanceServices / AttendanceTableService / AttendanceCardService / 外部 API
│   │   ├── Entities/           # AttendanceRecord / MonthlyWorkSummary / Worker / Site / AttendanceCardScan / AttendanceCardPunch
│   │   └── Repositories/       # IAttendanceRepository + 實現
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
│       ├── Entities/           # SysDictGroup / SysDict
│       └── Repositories/       # ISysDictGroupRepository / ISysDictRepository + 實現
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
- **不建外鍵（死規定）**：資料庫一律不建 `FOREIGN KEY` 約束。EF 的導航屬性、`Include()` 聯表 SQL 由模型生成，不依賴資料庫約束；清表、刪資料不受約束順序限制。新增關聯時只加欄位 + `HasOne/HasMany`，若 migration 仍產生 FK，就在同一支 migration 用 `ALTER TABLE ... DROP CONSTRAINT IF EXISTS` 補掉；資料完整性由程式保證（寫入前先查存在）。既有約束由 `20261122091500_DropAllForeignKeys` 一次清掉（含殘留的 Hangfire 表）。

---

## API 規範

- RESTful 風格
- 路由前綴：`/api/{module}`，如 `/api/attendance`、`/api/accounting`
- 統一返回：`TypedResults` + ProblemDetails（RFC 9457）；業務異常直接 `throw`，由 `Common/Errors/ExceptionProblemFilter` 轉換
- 分頁：`page` + `pageSize` 參數，統一返回 `Common/PagedResult<T>`（`{ items, total, page, pageSize }`）
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

- 現階段按月提供員工薪酬表和打卡明細；管理員可編輯並保存，注意事項最多 255 字元。金額使用 `decimal(18,2)`，工數和小時使用 `decimal(8,2)`。姓名篩選是員工下拉，查詢按 `workerId` 過濾（重名不會混在一起）；不填就是全部。
- 員工（工人）資料存在 `attendance_workers`：姓名、工號（外部系統）、日薪、在職。入口在考勤薪酬頁的「員工管理」（僅 Admin），可新增、編輯（姓名 / 工號 / 日薪 / 在職）、停用、刪除。停用（`IsActive=false`）只是不出現在同步的員工下拉，薪酬查詢照舊看得到；刪除是軟刪除（`IsDeleted`），薪酬表和打卡明細都不再顯示但記錄仍在庫裡——兩者是分開的動作，不要用停用代替刪除。日薪是計薪基礎，考勤卡同步時日薪為 0 會被拒絕（否則實付人工會是負數）。不把員工放進 `sys_dict`：字典只有字串欄位，裝不下日薪，也撐不住打卡明細和薪酬表的外鍵。
- 真實打卡機未對接：管理員通過 `/api/attendance/mock?year=...&month=...` 手動初始化模擬數據（所有環境可用），重複初始化不覆蓋已有月結和打卡修正。
- 打卡時間入庫使用 UTC，查詢月份及前端展示按香港時區（UTC+8）。薪酬不再只是樣例值：由考勤卡同步時按打卡明細重算（見下一節），但仍沒有定時同步，一律手工觸發。
- 通過 `IHttpClientFactory` 調用外部考勤接口
- CancellationToken 在 Infrastructure 層統一處理
- 拉取後存入 `attendance_records` 表，按月匯總計算薪酬

### 考勤卡掃描（Attendance）

- 輸入是「微電腦打卡鐘」月卡照片，版式固定：日期 | 上午上班 / 下班 | 下午上班 / 下班 | 加班上班 / 下班 | 小計。掃描結果即時落庫（`attendance_card_scans` + `attendance_card_punches`）供前端核對；導入打卡明細由使用者按「同步」觸發，不是掃描時自動做。
- 入庫欄位與識別結果一一對應：年月姓名各帶置信度，明細每欄存 `time` 及各自的置信度。**考勤卡記錄不存工時、加班時數或合計**——工時與金額在導入打卡明細時按規則計算（見本節最後兩條）。
- 卡片層級的複核標記（year / month / name / layout）原樣存 `text[]`；明細行的複核標記按當前閾值即時算，因為它完全可由置信度推回，而 `layout`（表頭漏檢）推不回來，所以必須存。
- 掃描結果可人工修正（`PUT /api/attendance/scans/{id}`，僅 Admin，前端雙擊年月、姓名或時間即可改）：只覆寫真正改動的欄位，改過的欄位置信度記 1（人工確認），沒改的保留原本識別置信度；卡級複核標記隨即重算，`layout` 除外（只能靠重新掃描解決）。時間須為 HH:mm，留空即清除該欄；卡上漏讀的日子可補。前端識別後與保存修正後都把月份篩選移到識別到的年月，避免新記錄因篩選不符而看不到。
- 原圖存 `storage_files`，文件類別用啟動時 SeedData 建立的 `AttendanceCard` / `card` 字典；一筆掃描對應一張原圖。URL 掃描由 Azure 拉圖，本服務不下載、不存原圖。掃描失敗不寫任何資料，也不留孤兒原圖。
- 與票據識別共用同一個 `DocumentIntelligenceClient`（`DocumentIntelligenceClientProvider`）及同一份 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY`，模型改用 `prebuilt-layout`，不新增環境變量。單圖上限 4 MB，只收 JPEG / PNG（看檔頭不看副檔名），一次只認一張卡。
- `prebuilt-layout` 只回表格格子，格子本身沒有置信度，所以按 `RowIndex` / `ColumnIndex` 還原行列，再用格子的 `Spans` 對回逐字置信度並取最低值。
- 打卡鐘印的是「日號 + 時間」（`0509:07` = 05 日 09:07），時間一律正規化為 HH:mm；同一格出現兩個時間（`09:07 17:05`）代表它橫跨上班 / 下班兩欄，按順序拆開；小計欄及之後的欄位忽略。
- 只有 1–31 的日號才算日期，表頭、標題、頁腳合計（231 / 307）不會被當成日期；表頭「日期」漏檢時退回按閱讀順序填欄，並把整張卡標記為 `layout` 需複核。休息日（只有日號、沒有時間）保留空列，方便前端對齊日期。
- 置信度閾值 0.80：印刷紅字時間逐欄標記；手寫的年份、月份、姓名**不參與卡片層複核**（它們的置信度普遍在 0.6–0.75，用印刷體的閾值去卡會讓每張卡一直掛「需覆核」），置信度照樣回報並在前端提示「手寫欄位置信度偏低」。需要嚴格把關時可勾「一併檢查手寫欄位」（`PUT /scans/{id}` 與 `POST /scans/{id}/confirm` 都接受 `includeHandwritten`）。手寫數字不得當作已核對。
- 人工核對完成後可按「標記為已核對」（`POST /api/attendance/scans/{id}/confirm`）：清掉卡片層複核標記，狀態由「需覆核」變「已識別」，時間欄完全不動。這是讓 `layout` 這類推不回來的標記由人工放行的唯一途徑；時間欄本身的低置信度只影響明細顯示，不影響卡片狀態。
- `prebuilt-layout` 對紅色印刷的打卡時間較穩（等寬、印刷體）；對手寫（月份、底部合計）識別率低，不能保證準確。若手寫長期不準，可評估改用 GPT-4o 視覺模型直出 JSON 作備選方案，但須先更新 `specs/tech-stack.md` 並確認費用，不得擅自引入。
- 同步（`POST /api/attendance/scans/{id}/sync`，僅 Admin，body 指定員工和地盤——卡上只有手寫姓名，配對由使用者決定；前端把這一區放在識別結果對話框上方）：有打卡時間的日子各生成一筆打卡明細（上班＝上午上班，缺則用下午上班；下班＝下午下班，缺則用上午下班；卡上是香港時間，存庫用 UTC，凌晨下班自動跨日；加班時數存 `OvertimeHours`，備註記「考勤卡導入」），再按該員工該月**全部**打卡明細重算薪酬表。同一員工同一月再同步＝重算：先刪掉上次由考勤卡導入的明細再寫入，改完時間重按不會重複計工數，模擬數據和人工維護的明細不受影響。沒有年月、日號超出該月天數、員工日薪為 0（會算出負數實付人工）一律拒絕同步。薪酬表的工數、O.T.、MPF、實付、總數每次同步都會被覆蓋，Remark 保留；人工單獨改過這些欄位後，下次同步會蓋回去。
- 計薪公式集中在 `AttendanceRules`（純函數，`Attendance.Checks` 直接驗證，不用資料庫）：工數 = 非勞工假的打卡明細數，目前一天一筆＝1 工，要改成半天 0.5 工或按工時折算只改 `WorkDaysOf`；總工數 = 工數 + 勞工假（勞工假不在考勤卡上，沿用薪酬表已填的值，不會被清零）；O.T. 時薪 = 日薪 ÷ 8 × 1.5，OT 金額 = O.T. 小時 × 時薪；MPF（僱主、僱員）= 總工數 × 50，兩邊同額；實付人工 = 日薪 × 總工數 − MPF（僱員）+ OT 金額；總數 = MPF（僱主）+ MPF（僱員）+ 實付人工。日薪取 `Worker.DailyWage`，所有金額四捨五入到 2 位。

### OCR 識別（會計收據）

- 票據可直接手動錄入（不必先有原圖），原圖可稍後補上或更換。前端只有一個「保存」動作，保存後即完成，沒有草稿狀態；必填為票據類型、支出分類（不能是 pending）及地盤分類，日期、金額、幣種可留空。OCR 只在使用者按「OCR 識別」時執行，回傳建議欄位，由使用者決定是否填入（只填空白欄位），不直接改寫票據資料。
- `IOcrService` 位於 Common/Infrastructure/Ocr，使用官方 C# SDK `Azure.AI.DocumentIntelligence`；客戶端由 `DocumentIntelligenceClientProvider` 共用（考勤卡掃描用同一個 client）；憑據只從環境變量 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY` 讀取，代碼和 appsettings 不出現密鑰；收據中文識別須實測，不能保證準確率。
- `Receipt.SiteCategoryDictId` 引用 `AccountingSiteCategory` 字典，是票據唯一的地盤歸屬，同時決定導出 ZIP 的頂層目錄；不引用考勤 `Site`，會計與考勤的地盤各自獨立（考勤地盤只用於考勤模組）。
- `DocumentTypeDictId` 引用 `AccountingDocumentType`（receipt / invoice）。支出分類由 `Receipt.Type` 持有（`Accounting` 字典），沒有原圖也可保存；有原圖時同步寫入 `FileItem.Type`，供文件模組按分類展示。
- 金額 decimal(18,2)，票據日期 DateOnly；發票金額取 InvoiceTotal，不取可能含歷史欠款的 AmountDue，也不自動視為已付款。
- 有原圖後才可識別。使用 Azure Document Intelligence `prebuilt-receipt`（收據 / 現沽單）及 `prebuilt-invoice`（發票按發票類型錄入）；只映射票據表需要的欄位（商戶 / 供應商、日期、金額、幣種、票據號碼），每個欄位帶 Azure 回報的置信度（0–1）；未識別的日期、金額及幣種保持空值。
- 置信度低於 0.80，或識別到欄位卻讀不出值（例如只認到貨幣符號 `$`、沒有金額）的欄位，標記為需人工複核，由前端提示使用者對照原圖核對。手持拍攝、褶皺反光、含手寫金額的單據識別率有限，識別結果不得當作已核對。
- 金額缺失時，從識別文字裡找「合計 / 總計 / TOTAL」等標籤後面的金額兜底：同一行優先，其次下一行的整行純數字；固定給 0.5 置信度並標記人工複核。只認明確的總額字眼，避免誤抓日期、電話或相機浮水印。
- 只保存及回傳票據欄位（商戶、日期、金額、幣種、票據號碼）與置信度；Azure 原始結果（pages / words / polygon）不入庫、不回前端，`OcrResultJson` 存的是識別建議本身。
- 僅 Admin 操作；會計圖片不能經通用 Storage 接口繞過權限或刪除。票據可經會計接口刪除（原圖一併清理），刪除不可復原。識別失敗保留原圖並可重試，識別結果不會覆寫票據欄位。
- 按票據日期（含起止日）、票據類型、支出分類及地盤分類提取全部匹配原圖為 ZIP，不限當前頁，不輸出 CSV；沒有原圖的票據不進 ZIP。ZIP 按「地盤分類 / 支出分類」分文件夾。MD5 僅作核對，不是文件地址或唯一鍵。
- ZIP 使用臨時文件，預設上限 5000 張 / 250 MB 原圖，完成或失敗後清理；第一版單圖上限 4 MB，與 Azure F0 相容。

### 文件存儲

- `IFileStorage` 接口抽象文件讀寫，定義在 `Storage/Services/`
- MVP 階段：`LocalStorageProvider` 實現，文件存本地磁盤
- 未來：換 `S3StorageProvider` / `OssStorageProvider` 只需改 DI 註冊，業務代碼不動
- 文件元數據（名稱、路徑、分類、權限）存數據庫，實際文件內容走 `IFileStorage`
- 上傳時流式計算 MD5，資料庫 `storage_files.Md5` 保存 32 位小寫十六進制摘要，不保存文件內容。歷史文件的 MD5 可為空；磁盤存儲名仍用 Guid，不按 MD5 去重。
