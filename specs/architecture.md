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
│   │   ├── Endpoints/          # 工數查詢 / 月結報表 / 打卡表 / 考勤卡掃描
│   │   ├── Services/           # AttendanceServices / AttendanceTableService / AttendanceCardService
│   │   ├── Entities/           # AttendanceRecord / MonthlyWorkSummary / Worker / AttendanceCardScan / AttendanceCardPunch（地盤和職位都用系統字典 AccountingSiteCategory / AttendancePosition，沒有獨立實體）
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
- **不建外鍵（死規定）**：資料庫一律不建 `FOREIGN KEY` 約束。EF 的導航屬性、`Include()` 聯表 SQL 由模型生成，不依賴資料庫約束；清表、刪資料不受約束順序限制。新增關聯時只加欄位 + `HasOne/HasMany`，若 migration 仍產生 FK，就在同一支 migration 用 `ALTER TABLE ... DROP CONSTRAINT IF EXISTS` 補掉；資料完整性由程式保證（寫入前先查存在）。

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
- **登錄限流** — `POST /api/identity/login` 是全系統最貴的計算（Argon2id 每次約 300ms CPU），必須限流：按客戶端 IP 分桶，20 次/分鐘、固定窗口，超額回 `429` + ProblemDetails + `Retry-After`（策略定義在 `Common/Security/LoginRateLimit`）。客戶端 IP 由 `Common/Http/ClientIpForwarding` 解析 `X-Forwarded-For`：生產環境是「宿主 nginx → web nginx → backend」兩跳，兩層都用 `$proxy_add_x_forwarded_for` 追加，故取最後 2 項（`ForwardLimit = 2`）；客戶端自己塞的值只會落在最左邊，取不到，偽造無效。沒有這層轉換時 `RemoteIpAddress` 永遠是 127.0.0.1，全站會共用同一個桶。

---

## 外部集成

### 考勤打卡數據

- 現階段按月提供員工薪酬表和打卡明細；管理員可編輯並保存，注意事項最多 255 字元。金額使用 `decimal(18,2)`，工數和小時使用 `decimal(8,2)`。姓名篩選是員工下拉，查詢按 `workerId` 過濾（重名不會混在一起）；不填就是全部。薪酬表另可按職位分類篩選，條件是當月薪酬記錄的職位快照（`PositionDictId`）。
- **薪酬表是「員工 × 職位 × 地盤」一行一個組合**：同一個員工同一個月可能在多個地盤、多個職位下工作，各出一行（`attendance_monthly_summaries.PositionDictId` + `SiteDictId`，並各存標籤快照）。合計按整份篩選結果相加，日薪不參與合計。工數、MPF、實付的公式對工數是線性的，所以分行之後各行的合計和未分組時一致。
- **打卡明細帶職位和地盤**（`attendance_records.PositionDictId` / `SiteDictId`），所以同一個人可以逐日換職位、換地盤；考勤卡同步時寫入員工當下的職位和當次選的地盤，也可以在「編輯打卡」逐筆改。薪酬表的分組完全由明細推出來。
- **人工改打卡明細會重算該月薪酬**：只有參與計薪的欄位（時間、O.T.、勞工假、職位、地盤）真的變動才重算，只改備註不觸發；改到別的月份時兩個月都重算。職位和地盤不改變金額總數，但決定分組行，所以也在觸發清單裡。回傳值帶 `payrollRows`（0 表示這次改動不涉及計薪）。
- 人工直接改薪酬表的欄位仍然保留（只影響那一行、不觸發計算），但下次同步或改明細時會被重算蓋回去；Remark 和勞工假在重算時保留。
- 員工（工人）資料存在 `attendance_workers`：姓名、工號（外部系統）、職位分類（可選）、日薪、在職。職位分類引用 `AttendancePosition` 字典，選項在「系統字典 → 職位分類」維護；SeedData 只預置「樓面員工」，不主動回填現有員工。入口在考勤薪酬頁的「員工管理」（僅 Admin），可新增、編輯（姓名 / 工號 / 職位分類 / 日薪 / 在職）、停用、刪除。停用（`IsActive=false`）只是不出現在同步的員工下拉，薪酬查詢照舊看得到；刪除是軟刪除（`IsDeleted`），薪酬表和打卡明細都不再顯示但記錄仍在庫裡——兩者是分開的動作，不要用停用代替刪除。日薪是計薪基礎，考勤卡同步時日薪為 0 會被拒絕（否則實付人工會是負數）。不把員工放進 `sys_dict`：字典只有字串欄位，裝不下日薪，也撐不住打卡明細和薪酬表的外鍵。
- 真實打卡機未對接，也沒有模擬數據初始化入口：打卡明細只能由考勤卡同步（見下一節）或人工維護產生。
- 地盤是系統字典 `AccountingSiteCategory`（「系統字典 → 地盤分類」維護），**沒有獨立的地盤表**，也沒有 `/attendance/sites` 端點；`AttendanceRecord.SiteDictId` 引用字典項。地盤可以有上級（例：牛房 → 一樓、二樓），考勤選的是最末級的那一項。考勤和會計票據共用這一份名單：同一批地盤既用於打卡明細，也用於票據的地盤歸屬和 ZIP 導出目錄，不再各維護一份。停用（`Enabled=false`）只是不出現在同步的地盤下拉，已導入的打卡明細和歷史票據照舊顯示；刪除會因明細或票據引用而被拒絕。SeedData 不預置任何地盤，選項由用戶自己維護。
- 打卡時間入庫使用 UTC，查詢月份及前端展示按香港時區（UTC+8）。薪酬不再只是樣例值：由考勤卡同步或人工改打卡明細時按明細重算（見本節後續與上一節），但仍沒有定時同步，一律手工觸發。
- 通過 `IHttpClientFactory` 調用外部考勤接口
- CancellationToken 在 Infrastructure 層統一處理
- 拉取後存入 `attendance_records` 表，按月匯總計算薪酬

### 考勤卡掃描（Attendance）

- 輸入是「微電腦打卡鐘」月卡照片，版式固定：日期 | 上午上班 / 下班 | 下午上班 / 下班 | 加班上班 / 下班 | 小計。掃描結果即時落庫（`attendance_card_scans` + `attendance_card_punches`）供前端核對；導入打卡明細由使用者按「同步」觸發，不是掃描時自動做。
- 入庫欄位與識別結果一一對應：年月姓名各帶置信度，明細每欄存 `time` 及各自的置信度。**考勤卡記錄不存工時、加班時數或合計**——工時與金額在導入打卡明細時按規則計算（見本節最後幾條）。
- 卡片層級的複核標記（year / month / name / layout）原樣存 `text[]`；明細行的複核標記按當前閾值即時算，因為它完全可由置信度推回，而 `layout`（表頭漏檢）推不回來，所以必須存。
- 掃描結果可人工修正（`PUT /api/attendance/scans/{id}`，僅 Admin，前端雙擊年月、姓名或時間即可改）：只覆寫真正改動的欄位，改過的欄位置信度記 1（人工確認），沒改的保留原本識別置信度；卡級複核標記隨即重算，`layout` 除外（只能靠重新掃描解決）。時間須為 HH:mm，留空即清除該欄；卡上漏讀的日子可補。前端識別後與保存修正後都把月份篩選移到識別到的年月，避免新記錄因篩選不符而看不到。
- 原圖存 `storage_files`，文件類別用啟動時 SeedData 建立的 `AttendanceCard` / `card` 字典；一筆掃描對應一張原圖。掃描失敗不寫任何資料，也不留孤兒原圖。
- 與票據識別共用同一個 `DocumentIntelligenceClient`（`DocumentIntelligenceClientProvider`）及同一份 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY`，模型改用 `prebuilt-layout`，不新增環境變量。單圖上限 4 MB，只收 JPEG / PNG（看檔頭不看副檔名），一次只認一張卡。
- `prebuilt-layout` 只回表格格子，格子本身沒有置信度，所以按 `RowIndex` / `ColumnIndex` 還原行列，再用格子的 `Spans` 對回逐字置信度並取最低值。
- 打卡鐘印的是「日號 + 時間」（`0509:07` = 05 日 09:07），時間一律正規化為 HH:mm；同一格出現兩個時間（`09:07 17:05`）代表它橫跨上班 / 下班兩欄，按順序拆開；小計欄及之後的欄位忽略。
- 只有 1–31 的日號才算日期，表頭、標題、頁腳合計（231 / 307）不會被當成日期；表頭「日期」漏檢時退回按閱讀順序填欄，並把整張卡標記為 `layout` 需複核。休息日（只有日號、沒有時間）保留空列，方便前端對齊日期。
- 置信度閾值 0.80：印刷紅字時間逐欄標記；手寫的年份、月份、姓名**不參與卡片層複核**（它們的置信度普遍在 0.6–0.75，用印刷體的閾值去卡會讓每張卡一直掛「需覆核」），置信度照樣回報並在前端提示「手寫欄位置信度偏低」。需要嚴格把關時可勾「一併檢查手寫欄位」（`PUT /scans/{id}` 與 `POST /scans/{id}/confirm` 都接受 `includeHandwritten`）。手寫數字不得當作已核對。
- 人工核對完成後可按「標記為已核對」（`POST /api/attendance/scans/{id}/confirm`）：清掉卡片層複核標記，時間欄的置信度記成人工確認，狀態由「需覆核」變「已核對」，並記下 `ConfirmedAt`，時間本身完全不動。這是讓 `layout` 這類推不回來的標記由人工放行的唯一途徑；時間欄本身的低置信度只影響明細顯示，不影響卡片狀態。**改動識別結果（含姓名）之後必須重新按一次「標記為已核對」**，前端不再自動補核對。
- 流程是兩步，順序由後端把關：先「標記為已核對」，再「同步」。識別結果再經 `PUT` 真的改動過（年月、姓名或任何時間）就會清空 `ConfirmedAt`，要重新核對。即使識別結果本來就沒有紅字（狀態是「待核對」而不是「已核對」），也必須按過一次「標記為已核對」才可同步。
- `prebuilt-layout` 對紅色印刷的打卡時間較穩（等寬、印刷體）；對手寫（月份、底部合計）識別率低，不能保證準確。若手寫長期不準，可評估改用 GPT-4o 視覺模型直出 JSON 作備選方案，但須先更新 `specs/tech-stack.md` 並確認費用，不得擅自引入。
- 同步（`POST /api/attendance/scans/{id}/sync`，僅 Admin，body 指定員工和地盤（`workerId` + `siteDictId`，地盤是系統字典 `AccountingSiteCategory` 的字典項）——卡上只有手寫姓名，配對由使用者決定）：**前提是卡片已標記為已核對（`ConfirmedAt` 有值）且沒有需覆核的欄位，否則回 400**；前端只在第 1 步完成後才顯示同步區。有打卡時間的日子各生成一筆打卡明細（上班＝上午上班，缺則用下午上班；下班＝下午下班，缺則用上午下班；卡上是香港時間，存庫用 UTC，凌晨下班自動跨日；加班時數存 `OvertimeHours`，備註記「考勤卡導入」；**職位寫入員工當下的職位分類、地盤寫入這次選的地盤**），再按該員工該月**全部**打卡明細重算薪酬表（按職位 × 地盤分組，回傳 `rows` 可能是多行）。同一員工同一月再同步＝重算：先刪掉上次由考勤卡導入的明細再寫入，改完時間重按不會重複計工數，模擬數據和人工維護的明細不受影響。沒有年月、日號超出該月天數、員工日薪為 0（會算出負數實付人工）一律拒絕同步。薪酬表的工數、O.T.、MPF、實付、總數每次同步都會被覆蓋，Remark 保留；人工單獨改過這些欄位後，下次同步會蓋回去。薪酬表每一行記錄該組合當下的職位分類和地盤標籤快照（`PositionDictId` + `Position`、`SiteDictId` + `Site`），之後改員工職位或改字典名稱都不影響已生成的月份；重算時快照會跟着更新。
- 計薪公式集中在 `AttendanceRules`（純函數，`Attendance.Checks` 直接驗證，不用資料庫）：工數 = 非勞工假的打卡明細數，目前一天一筆＝1 工，`WorkDaysOf` 已回傳 `decimal`，要改成半天 0.5 工或按工時折算只改這一個方法（`ApplyPayroll` 的 `workDays` 參數也是 `decimal`）；總工數 = 工數 + 勞工假（勞工假不在考勤卡上，沿用該薪酬行已填的值，不會被清零）；O.T. 時薪 = 日薪 ÷ 8 × 1.5，OT 金額 = O.T. 小時 × 時薪；MPF（僱主、僱員）= 總工數 × 50，兩邊同額；實付人工 = 日薪 × 總工數 − MPF（僱員）+ OT 金額；總數 = MPF（僱主）+ MPF（僱員）+ 實付人工。日薪取 `Worker.DailyWage`，所有金額四捨五入到 2 位。
- 分組重算的規則（`AttendanceTableService.RecomputePayrollAsync`）：按明細的（職位 × 地盤）分組，每組一行；同（職位 × 地盤）的舊行沿用其勞工假和 Remark，找不到明細的舊行整行刪除；該月完全沒有明細時不動薪酬表（避免把人工維護的行清掉）。
- 加班時數只在同一列內計算（`OtIn` 與 `OtOut` 同在一日）：跨列的加班（今日只填上班、次日才填下班）不支援，會算成 0 小時；工數不受影響，只是少算 OT 金額。
- 日薪為 0 只擋在**同步入口**（`SyncAsync` 檢查 `Worker.DailyWage <= 0` 就回 400）。同步之後，人工改薪酬表的 `DailyWage` 或把員工日薪改成 0 都不會被這條規則擋住；下次重算時以員工當下的日薪覆蓋。

### OCR 識別（會計收據）

- 票據可直接手動錄入（不必先有原圖），原圖可稍後補上或更換。前端只有一個「保存」動作，保存後即完成，沒有草稿狀態；必填為票據類型、支出分類（不能是 pending）及地盤分類，日期、金額、幣種可留空。OCR 只在使用者按「OCR 識別」時執行，回傳建議欄位，由使用者決定是否填入（只填空白欄位），不直接改寫票據資料。
- **原圖一律先裁剪再上傳**：拍照 / 選圖後打開 `components/ImageCropDialog.vue`，拉框裁掉桌面、背景和手指，確認後才上傳；已保存的原圖可再按「裁剪原圖」下載回來重裁（等於更換原圖，會清掉舊的識別結果）。裁剪用原生 Canvas（`utils/cropImage.ts`），**不引入裁剪庫**，`specs/tech-stack.md` 沒有列的不裝。
  - 解碼時按 EXIF 轉正（手機直拍不會躺倒），另備「旋轉 90°」按鈕；支援自由 / 1:1 / 4:3 / 3:4 / 16:9 比例，鎖比例時以拖動位移推另一邊。
  - 預覽是縮到 1280px 的縮圖（拖框流暢），落盤才按原圖像素裁切，所以裁剪不會降低解析度。
  - 輸出統一 JPEG（先填白底，PNG 透明不會變黑），逐級降質、必要時縮尺寸，保證 ≤ 4 MB（後端 `ReceiptService.CheckImageAsync` 的上限）。選圖端放寬到 10 MB，因為大圖裁剪後會縮下來。
- `IOcrService` 位於 Common/Infrastructure/Ocr，使用官方 C# SDK `Azure.AI.DocumentIntelligence`；客戶端由 `DocumentIntelligenceClientProvider` 共用（考勤卡掃描用同一個 client）；憑據只從環境變量 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY` 讀取，代碼和 appsettings 不出現密鑰；收據中文識別須實測，不能保證準確率。
- `Receipt.SiteCategoryDictId` 引用 `AccountingSiteCategory` 字典（顯示名「地盤分類」），是票據唯一的地盤歸屬，同時決定導出 ZIP 的頂層目錄；考勤的打卡明細也用同一份名單（`AttendanceRecord.SiteDictId`），**會計與考勤共用一個地盤分組**，不再各自維護。
- `DocumentTypeDictId` 引用 `AccountingDocumentType`（receipt / invoice）。支出分類由 `Receipt.Type` 持有（`Accounting` 字典），沒有原圖也可保存；有原圖時同步寫入 `FileItem.Type`，供文件模組按分類展示。
- 金額 decimal(18,2)，票據日期 DateOnly；發票金額取 InvoiceTotal，不取可能含歷史欠款的 AmountDue，也不自動視為已付款。
- 有原圖後才可識別。使用 Azure Document Intelligence `prebuilt-receipt`（收據 / 現沽單）及 `prebuilt-invoice`（發票按發票類型錄入）；只映射票據表需要的欄位（商戶 / 供應商、日期、金額、幣種、票據號碼），每個欄位帶 Azure 回報的置信度（0–1）；未識別的日期、金額及幣種保持空值。
- 置信度低於 0.80，或識別到欄位卻讀不出值（例如只認到貨幣符號 `$`、沒有金額）的欄位，標記為需人工複核，由前端提示使用者對照原圖核對。手持拍攝、褶皺反光、含手寫金額的單據識別率有限，識別結果不得當作已核對。
- 金額缺失時，從識別文字裡找「合計 / 總計 / TOTAL」等標籤後面的金額兜底：同一行優先，其次下一行的整行純數字；固定給 0.5 置信度並標記人工複核。只認明確的總額字眼，避免誤抓日期、電話或相機浮水印。
- 只保存及回傳票據欄位（商戶、日期、金額、幣種、票據號碼）與置信度；Azure 原始結果（pages / words / polygon）不入庫、不回前端，`OcrResultJson` 存的是識別建議本身。
- 僅 Admin 操作；會計圖片不能經通用 Storage 接口繞過權限或刪除。票據可經會計接口刪除（原圖一併清理），刪除不可復原。識別失敗保留原圖並可重試，識別結果不會覆寫票據欄位。
- 按票據日期（含起止日）、票據類型、支出分類及地盤分類提取全部匹配原圖為 ZIP，不限當前頁，不輸出 CSV；沒有原圖的票據不進 ZIP。ZIP 文件夾用字典完整路徑（`牛房 / 一樓 / 支出分類`），多層字典的同名下級不會擠進同一個文件夾；字典項找不到時退回票據上保存的名稱快照。MD5 僅作核對，不是文件地址或唯一鍵。
- ZIP 使用臨時文件，預設上限 5000 張 / 250 MB 原圖，完成或失敗後清理；第一版單圖上限 4 MB，與 Azure F0 相容。

### 系統字典層級（System）

- `sys_dict` 有 `ParentId`（自引用，同一分組內多層），用來表達「牛房 → 一樓 / 二樓」這類歸屬；只建 EF 導航（`Parent` / `Children`），**不建資料庫 FK**。
- 識別碼（`Value`）由系統按層級自動生成：頂層 `1`、`2`；下級跟在所屬上級後面編 `1.1`、`1.2`，孫級 `1.1.1`。識別碼**一律不可手動修改**，換上級時自動重新編號，所以歷史打印／導出仍能從編號看出層級。
- 上級必須存在且屬於同一分組；不能歸到自己的下級之下（成環防護）；有下級的項不能再歸到其他上級之下（要先移走下級），也不能被刪除。
- 前端：字典頁用樹形表格顯示（預設全展開，可逐行收起）。所有系統字典下拉（會計票據、考勤地盤／職位、文件庫類別、字典上級選擇）一律用 `frontend/src/components/DictTreeSelect.vue` 的樹形下拉，用 `frontend/src/utils/dicts.ts` 的 `dictTree()` 出樹、`dictPath()` 出「牛房 / 一樓」路徑；選完輸入框顯示完整路徑。不要用扁平的「父 · 子」字串，也不要自己再拼字串或另寫排序。
- **接口不重複返回字典名稱**：列表 / 詳情 DTO 只回字典項 id（`type`、`siteCategoryDictId`…），前端用已載入的字典按 id 映射（`dictText()`）。例外是真正落庫的歷史快照（薪酬表 `Position` / `Site` 欄）和 ZIP 導出時內部用的文件夾名稱。
- 樹形下拉要能選有下級的上級節點（`check-strictly`），否則「牛房」這種還有下級的地盤就點不了；停用項預設隱藏（`allow-disabled` 用於查歷史），已選中的停用項維持可顯示。
- **篩選一律含下級**：所有列表篩選（文件庫文件類別、會計票據列表、考勤薪酬表的地盤／職位）選上級時要連同其整棵子樹的記錄一起返回，前端不給開關。後端擴張用 `SysDictService.SubtreeIdsAsync(rootId)`（按 `ParentId` 收集，不按識別碼前綴——識別碼會在換上級時重新編號，只是顯示用的派生資料；含停用項，歷史記錄掛在停用子分類上不能漏）。只有 ZIP 導出（會計「提取圖片」）加「包含下級」開關（默認開，`includeDescendants=false` 才只取該分類本身）。表單單選（打卡、員工職位、票據表單、上傳類別、字典上級）不涉及子樹語義，不做擴張。

### 文件存儲

- `IFileStorage` 接口抽象文件讀寫，定義在 `Storage/Services/`
- MVP 階段：`LocalStorageProvider` 實現，文件存本地磁盤
- 未來：換 `S3StorageProvider` / `OssStorageProvider` 只需改 DI 註冊，業務代碼不動
- 文件元數據（名稱、路徑、分類、權限）存數據庫，實際文件內容走 `IFileStorage`
- 上傳時流式計算 MD5，資料庫 `storage_files.Md5` 保存 32 位小寫十六進制摘要，不保存文件內容。歷史文件的 MD5 可為空，磁盤存儲名用 Guid + 原擴展名。
- **按 MD5 去重**：同內容在磁盤上只存一份。上傳時若已有同 MD5 且未刪除的記錄，就丟掉剛寫的那份、共用既有的 `StoredName`；每筆記錄仍各有一行（原名、類別、上傳人、時間各記各的），所以同一張圖可以一次當票據原圖、一次當考勤卡。MD5 為空的歷史資料不參與去重。
- 刪除是軟刪除，且**只有再沒有任何未刪除記錄指向同一個 `StoredName` 時才刪磁盤文件**——去重後一個物理文件可能被多筆記錄共用，少做這一步就會出現「刪了 A，B 的圖也不見了」。
- 下載有兩條路，職責不同：
  - `GET /api/storage/files/{id}/download` —— 按記錄取，**元數據（檔名、ContentType）一定屬於這筆記錄**；
  - `GET /api/storage/files/by-md5/{md5}/download` —— 內容尋址，去重後同一摘要的物理文件唯一，回最早上傳那一筆的元數據。
  - 前端預覽**按 id 取圖、按 MD5 做快取鍵**：同內容只下載一份、只快取一份，同時檔名和 ContentType 永遠正確。
