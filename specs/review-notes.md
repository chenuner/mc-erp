# review-notes.md — 本輪 review：指引、問題清單、邏輯梳理

> **處理結果（2026-11-22）**
>
> | 編號 | 處理 |
> |------|------|
> | S1 / S2 密鑰 | ⏸ 依維護者指示未動；`appsettings.Development.json` 仍留明文 key，待自行輪換 |
> | S3 上傳白名單 | ⏸ 未做。判斷後認為「地盤照片按地盤分類」是 Storage 的正當用途，denylist 只該擋 `AttendanceCard`，需要產品決定；已記入 `status.md` |
> | S4 重複查字典 | ✅ 已合併成一次 |
> | S5 URL 掃描無防護 | ✅ 端點已刪除 |
> | L1 改明細不重算 | ✅ 已修：只有計薪相關欄位變動才重算，只改備註不觸發 |
> | L2 `WorkDaysOf` 回 `int` | ✅ 已改成 `decimal`，specs 那句話現在成立 |
> | L3 加班跨列 | ✅ specs 補了規則；行為未變 |
> | L4 明細搬月薪酬不跟 | ✅ 已修：跨月時兩個月都重算 |
> | L5 日薪 0 後門 | ✅ specs 已明說「只在同步入口把關」 |
> | L6 繞過「標記為已核對」 | ✅ 已拆掉（事後發現本來就走不到，屬死代碼） |
> | L7 預設密碼 | ⏸ 依維護者指示保留，已記為金科玉律 7 的已知例外 |
> | L8 系統分組被覆寫 | 📝 已記入 `status.md`，行為未變 |
> | L9 `Users.AnyAsync` 過濾 | 📝 已記入待確認，本輪未改 |
> | D1 三個零調用端點 | ✅ 已刪 |
> | D2 `.http` 範例 | ✅ 已更新（刪除的端點移除、新欄位補上、加薪酬 PUT 範例） |
> | D3 無死代碼 | ✅ 確認 |
> | D4 `AccountingFileProtectionService` | 保留（理由見下） |
> | D5 `by-md5` 預覽 | ✅ 已移除路由與前端用法，預覽改按 id |
> | D6 `listRoles` 無引用 | 📝 保留，已記入 `status.md`「角色分配 UI 未實現」 |
> | D7 `ModulePlaceholder.vue` | ✅ 已刪 |
> | D8 `Permission` 未接線 | 📝 已記入 `status.md` |
> | P1–P3 規格文字 | ✅ 已修 |
>
> 驗證：`dotnet build` 0 警告 0 錯誤；Identity 8 / Accounting 31 / Attendance 77 checks 全過；**集成檢查 67 項在真實 PostgreSQL 上全過（含從零套用全部 migration）**；`vue-tsc -b` 0 錯誤。`vite build` 在本機沙箱因 `spawn EPERM`（禁止子進程管道）無法執行，需在正常終端補跑一次。
>
> 遷移踩過的坑（已修）：新 migration 原本叫 `20261122154923_*`，字串排序排在 `20261122161315_AddAttendancePosition` **之前**，全新庫會在建索引時報 `column "PositionDictId" does not exist`；已改名為 `20261122235959_*` 並在 `status.md` 記下「新增 migration 後要跑 `migrations list` 確認排最後」。
>
> 下方是當時的 review 報告，保留作記錄。

> 範圍：只看「specs 與代碼的偏差」。按風險排序，每條都標了檔案和行號。
> 狀態：**待維護者逐條確認**；確認前不改任何代碼。
> 對照基準：`specs/status.md`（2026-11-22）、`specs/architecture.md`、`AGENTS.md`。
> 本輪沒有跑 `scripts/check.ps1`（只做閱讀，不改代碼）。

---

## 0. 怎麼自己 review（照這個順序走，從上到下）

| 步 | 看什麼 | 為什麼這個順序 | 對應問題編號 |
|---|--------|----------------|--------------|
| 1 | 機密與倉庫衛生 | 洩露一次就回不去，且與其它問題無關，5 分鐘看完 | S1、S2 |
| 2 | 權限與越權面 | 錯一次就是資料外洩；只需看 5 個 Endpoints 檔的 `RequireAuthorization` | S3、S4 |
| 3 | 「會算錯錢」的流程 | 考勤卡 → 打卡明細 → 薪酬表，是唯一會產生金額的鏈路 | L1–L5 |
| 4 | 兩步流程（核對 → 同步）的語意 | specs 有一整節規定，最容易前後端漂移 | L1、L6 |
| 5 | 種子資料與環境 | SeedData 在每次啟動都會跑，錯了是全域影響 | L7、L8 |
| 6 | 死代碼與分層 | 改完上面的再看，免得清掉的東西其實是上面問題的修法 | D1–D9 |
| 7 | 規格文字自身 | 最後統一修 specs，避免下次 review 又對錯基準 | P1–P3 |

**review 時要反覆問自己的三個問題**（這個倉庫最容易出錯的地方）：

1. **「這條寫入路徑，有沒有繞過同樣的業務規則？」** 例如：同步薪酬會重算工數，那改打卡明細之後呢？
2. **「specs 說的『唯一途徑』是真的唯一嗎？」** 例如：specs 說「標記為已核對」是放行 `layout` 的唯一途徑，前端卻另有一條自動符合同步條件的路。
3. **「這是規格要求的，還是代碼自己加的行為？」** 例如：SeedData 在非 Production 自動建 `Admin@123`。

**建議的閱讀順序（檔案層級）**：

```
Program.cs (權限/配置)  →  Common/SeedData.cs  →  Common/Errors/ExceptionProblemFilter.cs
  →  Features/Attendance/Services/AttendanceRules.cs      ← 所有計薪公式都在這，先讀它
  →  Features/Attendance/Services/AttendanceCardService.cs ← 掃描/修正/核對/同步四件事都在這（456 行）
  →  Features/Attendance/Endpoints/*.cs
  →  Features/Attendance/Services/AttendanceTableService.cs
  →  frontend/src/views/attendance/AttendanceView.vue      ← 兩步流程的 UI，對照後端一起讀
```

---

## 1. 機密與倉庫衛生

### S1 🔴 未提交的工作區把 Azure 密鑰寫進了 `appsettings.Development.json`

`backend/appsettings.Development.json:14` 目前有**未提交**改動，內容是明文 `AZURE_DOC_KEY`。

- `specs/architecture.md:210`：「代碼和 appsettings 不出現密鑰」。
- `README.md:142`：「不得將密鑰提交到 Git」；本機應走 `dotnet user-secrets`（backend 已配置 `UserSecretsId`）。
- 現狀風險：這把 key 已經出現在工作區、可能已出現在你的編輯器歷史 / 備份 / 截圖裡。**按已洩露處理**。
- 建議：還原這個檔案（key 改走 user-secrets 或環境變量），並在 Azure Portal 輪換這把 key。
- ⚠️ 你前一輪選擇了「先別動，等我確認」——這條不動代碼，只等你決定。
- 順帶：`tests/Accounting.Checks/IntegrationChecks.cs:21-22` 用 `ConfigurationBuilder` 讀 `appsettings.Development.json`，再以 `AddEnvironmentVariables()` 覆蓋。目前 `:42` 有把 `AZURE_DOC_KEY` 清空，所以還沒出事；但這正好說明「配置文件裡放密鑰」會被任何讀配置的路徑撿到。

### S2 🟠 `.gitignore` 沒有排除任何 `appsettings*.json`

`git ls-files` 顯示 `backend/appsettings.json`、`backend/appsettings.Development.json` 都是**已跟蹤**文件；當前 `.gitignore` 對它們沒有任何規則（這也解釋了 S1 是怎麼發生的）。

- 建議（二選一，取其一即可）：
  1. 保留跟蹤，但加一條 CI/本地檢查：`backend/appsettings*.json` 裡不許出現 `AZURE_DOC|SECRET|PASSWORD|KEY`。
  2. `git rm --cached backend/appsettings.Development.json` 改為跟蹤 `appsettings.Development.example.json`。
- 與 specs 的關係：`AGENTS.md`「不按環境分叉行為」的意志下，配置文件仍是存在的（`appsettings.Development.json` 有自己的 Storage:TempPath），所以只處理「密鑰」，不要順手刪環境文件。

---

## 2. 權限

### S3 🔴 Storage 模組的**上傳類別**沒有任何白名單，可寫入任意字典分組

`backend/Features/Storage/Services/FileService.cs:22-28` 的 `上传Async` 只驗證「字典項存在且 enabled」，以及「Accounting 分組需要 Admin」。任何已登入用戶（`StorageEndpoints.cs:17-19` 只有 `RequireAuthorization()`）都可以：

- 上傳到 `AttendanceCard` 分組 → 得到一張用 `storage_files` 撐著的「假考勤卡」；
- 上傳到 `AccountingSiteCategory`、`AttendancePosition`、或任何用戶自建分組。

**偏差**：`specs/architecture.md:58` 對 Storage 的描述是「地盤照片存儲、按地盤/時間/事件分類」；`status.md:24` 明確「按地盤 / 帳戶的檔案權限 ❌ 未實現，目前只有 Admin / 非 Admin 粗粒度」。但代碼實際的粗粒度是**按字典分組名硬編碼**，而不是「Admin / 非 Admin」——specs 沒有記錄這個規則，任何新增分組都會默默變成「人人可上傳」。

- 建議：加一個允許上傳的分組白名單常數（例如只允許自建的非系統分組），並在 `status.md` 補一句實際的權限規則。權限細化（FilePermission）仍是 `status.md:24` 的未實現項，不要在這次 review 順手做。

### S4 🟡 `UpdateReceipt` 對同一個地盤字典查了兩次

`backend/Features/Accounting/Services/ReceiptService.cs:129` 與 `:146` 各呼叫一次 `DictAsync(siteCategory, "AccountingSiteCategory")`。功能上不影響結果（兩次都通過才賦值），屬於純冗餘；`specs/architecture.md:211` 沒有要求重複校驗。

- 建議：`var siteCategory = request.SiteCategoryDictId is { } id ? await DictAsync(id, "AccountingSiteCategory") : null;`，一次搞定。

### S5 🟡 考勤卡掃描的 URL 入口沒有任何防護

`AttendanceEndpoints.cs:31` → `AttendanceCardService.ScanAsync(string url)`（`:249-254`）。

- 這裡的 SSRF 風險 Azure 那邊會擋（我們不自己下載，程式碼註釋也寫了原因），但**沒有速率限制、沒有長度上限、沒有域名白名單**，`ScanUrlRequest.Url` 只有 `[Required]`（`:178`）。`specs/architecture.md:195` 只提到 URL 掃描存在，沒有記「誰能按、多久能按一次」。
- 建議：`ScanUrlRequest.Url` 加 `[Url, MaxLength(2048)]`；若要更嚴再在 specs 記明「僅 Admin、不設速率限制」。順帶：這個端點與 D1 重疊，若不保留就直接刪。

---

## 3. 會算錯錢的流程（最高業務風險）

### L1 🔴 人工改「打卡明細」之後，薪酬表不會重算，兩張表互相矛盾

- 寫入路徑：`AttendanceTableEndpoints.cs:83-100` `UpdatePunchAsync` 改 `ClockInTime/ClockOutTime/OvertimeHours/IsLaborLeave/Remark` 後**只 `SaveAsync`**，完全不碰 `attendance_monthly_summaries`。
- 重算路徑只有一個：`AttendanceCardService.SyncAsync`（同步考勤卡時）。
- 具體後果：
  - 把某天改成「勞工假」→ `WorkDaysOf` 少一天，但薪酬表的 `WorkDays` 不變；
  - 改 `OvertimeHours` → 薪酬表的 `OvertimeHours` / `OvertimeAmount` 不變；
  - 刪掉一筆明細 → 工數不變。
  - 直到**同一個員工同一個月再同步一次考勤卡**，才會被 `ApplyPayroll` 蓋回去。
- **偏差**：`specs/architecture.md:204` 只說「人工單獨改過這些欄位後，下次同步會蓋回去」——那說的是**薪酬表欄位**，並沒有說「改打卡明細不會重算薪酬」。但是 `status.md:19` 和 `architecture.md:205` 的計薪公式都是「工數 = 打卡明細數」，讀者會合理預期兩者是同步的。這是最容易被使用者踩到的矛盾。
- 選項（請選一個）：
  1. 改明細後同步重算該員工該月薪酬（在 `UpdatePunchAsync` 尾巴按 `AttendanceRules.ApplyPayroll` 算一次）——行為最一致，但改 `Remark` 也會觸發重算；
  2. 只在不影響計薪的欄位變動時不重算，影響計薪的欄位（`ClockInTime` 所屬月份、`OvertimeHours`、`IsLaborLeave`）變動時重算；
  3. 不改代碼，在 specs 明確寫「打卡明細的人工修改不影響薪酬表，直到下次同步」。

### L2 🟡「工數」在記憶體是 `int`，在資料庫是 `decimal(8,2)`

- `AttendanceRules.WorkDaysOf` 回傳 `int`（`:74`）；`ApplyPayroll(..., int workDays, ...)`（`:89`）；`MonthlyWorkSummary.WorkDays` / `TotalWorkDays` 是 `decimal`（`MonthlyWorkSummary.cs:26,30`），`ErpDbContext.cs:139-141` 是 `HasPrecision(8, 2)`。
- **偏差**：`specs/architecture.md:205` 寫「要改成半天 0.5 工或按工時折算只改 `WorkDaysOf`」——這句話是錯的。真改成半天，`WorkDaysOf` 簽名要從 `int` 變 `decimal`，`ApplyPayroll` 的 `int workDays` 參數也要改，`Attendance.Checks` 的斷言也可能要改。至少三處，不是一處。
- 建議：這輪把 `WorkDaysOf` 直接改成 `decimal`（純內部，行為不變），讓 specs 那句話變成真的；或者改 specs 的文字。

### L3 🟡 加班時數的跨午夜只在「同一列」成立

`OvertimeHoursOf`（`AttendanceRules.cs:63-69`）的跨午夜處理本身是對的：`OtIn=18:00`、`OtOut=00:30` → 6.5 小時，`Attendance.Checks:295-296` 有測。

規格沒涵蓋的邊界：如果卡上把加班寫成**跨兩列**（當日 `18:00 → 空白`，次日 `→ 01:00`），現在 `OtIn` 有值、`OtOut` 為空 → 回 0 小時（`HasTime` 判定仍是工作日，工數不受影響，只少算 OT 金額）。`architecture.md:205` 的「凌晨下班自動跨日」說的是 `ClockOutTime`，不是 OT。

- 建議：specs 補一句「加班必須印在同一列，跨列不支援」；或在 `Attendance.Checks` 加一條測試把這個行為釘死（哪種都行，別讓它變成未定義行為）。

### L4 🟡 改打卡明細的日期可以把明細搬到別的月份，薪酬表不會跟著搬

`SyncAsync:382-384` 取該員工該月的明細用 `AttendanceRules.MonthRange`（香港月 → UTC）；薪酬表的 `Year/Month` 就是那個香港月，這條目前自洽。

但 `UpdatePunchAsync` 允許把 `ClockInTime` 改成**下個月**——明細跑到下個月，薪酬表還留在上個月，兩邊就分叉了（比 L1 更難察覺，因為連「同一月份」的假象都沒了）。

- 建議併入 L1 一起決策：改月份時要不要「兩個月都重算」。

### L5 🟡 「日薪為 0 拒絕同步」有一條後門

- `specs/architecture.md:181`、`:204` 都說「日薪為 0 會被拒絕」。
- 實際擋在 `SyncAsync:369-370`，但：
  - `AttendanceServices.UpdateWorkerAsync`（`:45`）允許把日薪改成 0；
  - `UpdatePayrollAsync`（`AttendanceTableEndpoints.cs:72`）允許直接改薪酬表的 `DailyWage`，**不驗證與 `Worker.DailyWage` 一致**。
- 也就是說：同步前「日薪必須 > 0」的保證，在同步後可以被任何一次人工編輯破壞，而 specs 沒有說這條規則是「只在同步時生效」。
- 建議：specs 明說「日薪 > 0 只在同步入口把關」，或把工人在 `UpdateWorkerAsync` 的日薪下限改成 `> 0`。

---

## 4. 兩步流程（核對 → 同步）的語意漂移

### L6 🔴 前端用「改名 + 自動 confirm」繞過了「標記為已核對」

`frontend/src/views/attendance/AttendanceView.vue:697-713` `relabelAndConfirm`：

1. `updateCardScan` 把卡上姓名改成選中的員工姓名（後端 `:332` 因此清掉 `ConfirmedAt`）；
2. **立刻** `confirmCardScan(id)` 把核對時間補回來；
3. 然後才 `syncCardScan`。

**偏差**：`specs/architecture.md:202` 寫「識別結果再經 `PUT` 真的改動過（年月、姓名或任何時間）就會清空 `ConfirmedAt`，**要重新核對**」；`:201` 更直接說「這是讓 `layout` 這類推不回來的標記由人工放行的**唯一途徑**」。前端這條路讓「改動 → 重新核對」變成**一次點擊、零人工確認**，`layout` 也能被這種方式放行。

- 另外 `saveCard`（`:785`）也有同樣效果：用戶選了不同員工姓名後按「保存修改」，姓名被覆寫，核對時間被清空——這步是對的；但 `syncCard` 的 `relabelAndConfirm` 分支把「必須人工重新核對」變成程序自動。
- 建議（請選）：
  1. 前端刪掉 `relabelAndConfirm`，改為提示「姓名已改，請重新按『標記為已核對』」——最符合 specs；
  2. 後端在 `UpdateAsync` 接受一個 `relabelOnly` 旗標，改名不算「內容改動」——但那要改 specs，且 `layout` 風險仍在；
  3. specs 承認這條路徑並寫進去。

> 我的建議是 1：它同時消掉一個 specs 沒提的隱藏分支，代碼也短了。

---

## 5. 種子資料與環境（會踩到「不按環境分叉」死規定）

### L7 🔴 `SeedData` 在非 Production 自動建 `admin / Admin@123`

`backend/Common/SeedData.cs:83-86`：

```csharp
var initialAdminPassword = sp.GetRequiredService<IConfiguration>()["Bootstrap:AdminPassword"]
    ?? (sp.GetRequiredService<IHostEnvironment>().IsProduction()
        ? throw new InvalidOperationException("Bootstrap:AdminPassword is required for the first production startup")
        : "Admin@123");
```

- **直接違反** `AGENTS.md` 金科玉律 7：「不按環境分叉行為——不自行新增 `IsDevelopment()` / `IsProduction()` 分支來關閉功能；本地與生產保持一致。」也與 `status.md:5` 的專案意志衝突。
- 這是一個**預設密碼**（`Admin@123`）：任何沒設 `Bootstrap:AdminPassword` 的環境（例如 staging、或被誤設成非 Production 的生產）都會得到一個可登入的管理員帳號。
- `tests/Accounting.Checks/IntegrationChecks.cs:67` 硬編碼 `Admin@123` 登入，所以這條「測試依賴預設密碼」——改它會牽動整合檢查。
- 建議：不管什麼環境都要求 `Bootstrap:AdminPassword`（或走 `user-secrets` / 環境變量），測試改成注入 `ConnectionStrings__Default` 的同時注入 `Bootstrap__AdminPassword`。這一條需要你拍板，因為它會改掉本地「開箱即用」的體驗。

### L8 🟡 `SeedData` 每次啟動都會覆寫系統分組的名稱與排序

`SeedData.cs:38-41`：

```csharp
group.IsSystem = true;
group.Name = name;
group.Sort = sort;
```

- 註釋說「方便改名後自動追上」，但 `specs/status.md:25` 把字典分組描述成「分組管理 + 字典項 CRUD，字典值自動編號、**可調整排序**」。使用者透過 `PUT /api/system/dict-groups/{id}` 或 `reorder` 對**系統分組**的調整，下一次啟動會被無聲蓋掉。
- 對比：`SysDictGroupService.DeleteAsync:71` 允許系統分組「不可刪除」，但 `UpdateAsync` 沒有任何「系統分組名稱/排序不可改」的限制——一個允許改、一個偷偷改回去。
- 建議：系統分組只補 `IsSystem=true`（首次建立時），不要每次覆寫 `Name`/`Sort`；或明確禁止對系統分組改名稱/排序並在 API 回 400。

### L9 🟡 首次登入時 `Users.AnyAsync()` 的判斷在查詢過濾之下

`SeedData.cs:80` 用的是 `db.Users.AnyAsync()`，受 `HasQueryFilter(u => !u.IsDeleted)` 影響（`ErpDbContext.cs:64`）。如果管理員被軟刪除（`UserService.DeleteAsync` 應該是軟刪），下一次重啟時 `AnyAsync()` 回 false → **再建一份 admin / 角色**（`Role` 唯一索引會讓 `db.Roles` 插入撞鍵，啟動失敗）。

- 建議：先確認 `UserService.DeleteAsync` 是軟刪還是硬刪（我這輪沒讀那個檔的刪除實作）。若確實是軟刪，這就是「刪光管理員 → 重啟就炸」的等級：應改用 `IgnoreQueryFilters().AnyAsync()` 判斷，或改成「沒有管理員時才補建」。

---

## 6. 死代碼與分層（確認後即可機械清理）

> 判定方法：全倉 `grep` 排除 `obj/`、`bin/`、`Migrations/` 後零引用。

### D1 後端：4 個沒有任何調用方的端點

| 端點 | 位置 | 狀態 |
|------|------|------|
| `GET /api/attendance/records/{workerId}` | `AttendanceEndpoints.cs:27` → `AttendanceRepository.GetRecordsAsync` | 前後端零引用；`RecordDto`（`:163`）只被它用 |
| `GET /api/attendance/summaries` | `AttendanceEndpoints.cs:42` → `AttendanceServices.GetSummariesAsync` → `AttendanceRepository.GetSummariesByMonthAsync` | 前後端零引用；`SummaryDto`（`:167`）只被它用 |
| `GET /api/attendance/summaries/{workerId}` | `AttendanceEndpoints.cs:43` → `AttendanceServices.GetSummaryAsync` → `AttendanceRepository.GetSummaryAsync` | 同上 |
| `POST /api/attendance/scan/url` | `AttendanceEndpoints.cs:31` → `AttendanceCardService.ScanAsync(string url)` | 前端沒有 `scanAttendanceCardUrl`；`attendance.http`（未跟蹤）也沒有這個例子 |

- 與規格的關係：`architecture.md:195` 有描述 URL 掃描、`status.md:21` 的入口清單**沒有**列出 `/scan/url`。如果它是有意的備用入口，請在 `status.md` 補上；否則刪掉（連同 `ScanUrlRequest`、`ScanAsync(string url)` 多載）。
- `GET /records` 和 `GET /summaries` 的刪除會連帶讓 `AttendanceRepository` 只剩 3 個方法（worker CRUD），`IAttendanceRepository` 一起縮。**注意**：L1 若選「改明細要重算薪酬」的方案 1，`GetRecordsAsync` 可能被重新用到——所以**先決定 L1，再清 D1**。

### D2 後端：`.http` 範例只有考勤有（既有欠賬，不在本輪清）

`backend/Features/Attendance/Endpoints/attendance.http` 是**未跟蹤**的新增文件，內容與 `architecture.md` 一致，建議 `git add`；其餘 modules 沒有 `.http`，與 `AGENTS.md` 新端點檢查清單第 6 條有落差，記在 `status.md` 的欠賬裡即可。

### D3 後端：`AttendanceTableService` 沒有死代碼

確認通過：`GetPayrollByIdAsync`、`GetPunchByIdAsync`、`SaveAsync` 都由 `AttendanceTableEndpoints` 使用；`PayrollQuery` / `PunchQuery` 各被列表、筆數、合計共用，口徑一致。

### D4 後端：`AccountingFileProtectionService` 是 1 行轉發

`AccountingFileProtectionService.cs:5-7` 只包了 `repo.IsFileReferencedAsync`。按 `AGENTS.md` 金科玉律 1「三行重複代碼勝過過早抽象」，可以併回 `FileService`；但它擋住了 Storage→Accounting Repo 的直接依賴，符合 `architecture.md:64`「跨模組只調 Application 層接口」。**我建議保留。**

### D5 前端：`StorageView` 的 `getFileBlob(id, md5)` 用 MD5 下載

`api/storage.ts:43-48` 在 `md5` 非空時走 `/storage/files/by-md5/{md5}/download`。

- 後端 `FileService.按Md5下载Async:81` 用 `FirstOrDefaultAsync`，**同一個 MD5 有多筆時回哪一筆不確定**（資料庫不按 MD5 去重，`architecture.md:228` 明確「不按 MD5 去重」）。
- 影響：Storage 網格縮略圖 / 大圖預覽可能顯示**另一張同內容文件的副本**——內容相同所以通常看不出來；但 `ContentType`/`OriginalName` 可能不同，下載檔名會錯。更重要的是：這條路由讓「預先知道 MD5 的人」可以繞過 `id` 直接拿檔案，而 MD5 在 `FileDto` 裡是**回給前端的**。
- 建議：預覽一律走 `id` 下載；`by-md5` 路由若確定要做 CDN/緩存用途，再單獨討論（specs 目前完全沒提這條路由）。
- **偏差**：`specs` 完全沒有提到 `/storage/files/by-md5/...`。

### D6 前端：`api/identity.ts` 的 `listRoles` 零引用（≠死代碼，見下）

`identity.ts:54-56` 沒有被任何 view 使用；`UsersView.vue` 只做用戶 CRUD，**沒有角色分配 UI**（後端 `POST /users/{userId}/roles/{roleId}` 存在但無入口）。`architecture.md:155-172` 描述了完整的 Role/UserRole 體系，`status.md:15` 寫「用戶/角色管理 ✅ 可用」——後端可以，前端只有一半。

- 建議：**不要刪 `listRoles`**，它是給「角色分配 UI」預留的；改成在 `status.md` 補「角色分配 UI ❌ 未實現（後端端點已就緒）」，避免下一個人以為前端已經能做。

### D7 前端死組件：`components/ModulePlaceholder.vue`

全倉 `grep` 零引用，`router/index.ts` 也沒有用它的路由。可直接刪。

### D8 後端未實作但已建模：`Permission` / `RolePermission`

`ErpDbContext.cs:15,17` 有 `Permissions` / `RolePermissions` 的 DbSet 與實體，`JwtTokenGenerator.cs:29-30` 會往 token 塞 `permission` claim，`AuthService.cs:22` 會查權限。但：

- 沒有任何 Service / Endpoint 維護 `Permission` 或 `RolePermission`；
- 沒有任何 `RequirePolicy` 用到 `permission`（`Program.cs:78-81` 只有 `admin` 一個 policy，基於 Role）；
- `SeedData` 不建任何 permission。

即 `architecture.md:167`「Permission — 權限項（如：attendance.view, accounting.edit）」與 `:170`「JWT — 登錄後簽發，包含角色和權限信息」在**授權上完全沒被使用**，token 裡的 `permission` claim 是純裝飾。`status.md` 沒有標註這件事。

- 建議：在 `status.md` 加一行「Permission / RolePermission：已建模但未接線（授權只用 Role-based `admin` policy）」。

---

## 7. 規格文字自身的偏差

### P1 `architecture.md:205`「只改 `WorkDaysOf`」不成立 → 見 L2

### P2 `architecture.md:204` 的「同步＝重算」講得太絕對

- 它沒說「人工改打卡明細後不重算」（見 L1），也沒說「重算只覆蓋薪酬表的計算欄位，`DailyWage` 取自 `Worker.DailyWage` 而非薪酬表原有的值」。
- 實測 `ApplyPayroll:91` 是把 `summary.DailyWage = Money(dailyWage)` **覆寫**成員工當下的日薪——`specs` 沒有明說這點，但這是使用者最容易誤解的地方（以為改薪酬表的日薪會被保留）。

### P3 `architecture.md:93` 寫「`Repositories/` — IAttendanceRepository + 實現」，但 `AttendanceCardService` 與 `AttendanceTableService` 直接注入 `ErpDbContext`

- 前者（`IAttendanceRepository`）只服務已死/半死的 `GET /records`、`GET /summaries` 路徑；
- 後兩者（真正在用的掃描 / 同步 / 薪酬查詢）**完全繞過 Repository**，直接 `db.AttendanceRecords`、`db.MonthlyWorkSummaries`。
- 偏差：`architecture.md:42-48` 的依賴規則「API → Application → Infrastructure（EF Core Repository）」對 Attendance 模組只有一半成立。`specs` 要麼承認「多數模組直接用 DbContext，Repository 只在需要時才建」，要麼就該統一。**我建議前者**（`AGENTS.md` 金科玉律 1：不過度工程化），並順手把 `IAttendanceRepository` 併回 Service——但它要先確定 D1 的刪除範圍。

### P4 `architecture.md:80` 與代碼一致，確認通過

---

## 8. 確認通過的項目（避免重複 review）

| 項目 | 結論 |
|------|------|
| 計薪公式（OT/MPF/實付/總數） | `AttendanceRules.ApplyPayroll` 與 `architecture.md:205` 完全一致；`Attendance.Checks:300-310` 有逐項斷言 |
| 跨午夜 | `AttendanceRules.ToRecord:44`、`OvertimeHoursOf:67` 有處理，`Attendance.Checks:291-296` 有測 |
| 兩步流程後端把關 | `SyncAsync:353-356` 同時檢查 `NeedsReview` 與 `ConfirmedAt`，符合 `architecture.md:202` |
| 修正不等於勾選 | `UpdateAsync:332` 只在 `ApplyTo` 回 true 時清 `ConfirmedAt`，符合 `architecture.md:202` |
| 手寫欄位不參與卡片層複核 | `CardReviewRules.ReviewFieldsOf` + `Attendance.Checks:233-258` 一致 |
| 掃描失敗不留孤兒原圖 | `SaveAsync:451-462` 有清理，符合 `architecture.md:195` |
| 考勤卡與票據共用 Azure client | `Program.cs:95-97` 共用 `DocumentIntelligenceClientProvider`，符合 `architecture.md:196` |
| 地盤＝系統字典 `AccountingSiteCategory` | 無獨立表、無 `/attendance/sites` 端點，符合 `status.md:18` |
| 不建外鍵 | `ErpDbContext` 未宣告 `HasConstraintName` 之類；FK 由 `20261122091500_DropAllForeignKeys` 統一清掉，符合 `AGENTS.md` 金科玉律 8 |
| 前端無第二個 UI 庫 / 無 vue-query | `package.json` + import 掃描通過 |
| `/scalar` 在所有環境開放 | `Program.cs:154-159` 已註釋掉 `IsDevelopment` 分支，符合 `status.md:5` |
| `GET /api/accounting/receipts` 權限 | 整個 group `RequireAuthorization("admin")`（`AccountingEndpoints.cs:15`），與 `IntegrationChecks.cs:299` 一致 |

---

## 9. 建議的處理順序（你確認後我照這個走）

| 批次 | 內容 | 為什麼先做 | 風險 |
|------|------|------------|------|
| 批次 A | S1 + S2（密鑰） | 洩露處理有時間成本 | 低，不動代碼邏輯 |
| 批次 B | L6（刪 `relabelAndConfirm`）+ S3（上傳白名單） | 兩個都是「繞過既有規則」，一起改 | 低，但 L6 需你確認規格 |
| 批次 C | L7（SeedData 預設密碼） | 要改整合檢查 | 中，會改變本地啟動體驗 |
| 批次 D | D1 + D5 + D7（死代碼；D6 改為補 `status.md`） | 純刪除，得先決定 L1 再動 D1 | 低 |
| 批次 E | L1 + L2 + L4 + L5（計薪一致性） | 業務決策，需你選方案 | 中，涉及行為改變 |
| 批次 F | P1–P3 + L9 驗證（specs 文字） | 最後統一 | 無 |

**請你逐條回覆：**「L1 選方案幾」、「L6 選方案幾」、「L7 是否改」、以及 D1 的四個端點哪些要保留。其餘我可以按上表的建議直接做。
