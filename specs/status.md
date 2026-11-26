# status.md — 當前實現狀態

> specs 其餘文件描述「目標態」，本文件記錄「當前態」。動手前先看這裡，不要把未實現的功能當成已存在。
>
> **專案意志**：本地與生產的行為保持一致，不按 `ASPNETCORE_ENVIRONMENT` 切換功能（API 文檔等）。要新增 `IsDevelopment()` / `IsProduction()` 分支前，必須先向維護者確認。
>
> 最後更新：2026-11-23
---

## 模組狀態

| 模組 | 功能 | 狀態 | 入口 |
|------|------|------|------|
| Identity | 登入、用戶/角色管理、JWT | ✅ 可用 | `Features/Identity` |
| Identity | 角色分配 UI（把角色指派給用戶） | ❌ 未實現（後端 `POST/DELETE /api/identity/users/{userId}/roles/{roleId}` 已就緒，前端沒有入口） | — |
| Identity | `Permission` / `RolePermission` 授權 | ⚠️ 已建模但未接線：token 會帶 `permission` claim，但沒有任何 policy 用到，授權只有 Role-based 的 `admin` | `Program.cs` |
| Attendance | 薪酬表 / 打卡表查詢（薪酬表另可按當月職位快照篩選）、人工維護、按考勤卡重算薪酬 | ✅ 可用 | `AttendanceTableService` / `AttendanceRules` |
| Attendance | 薪酬按「員工 × 職位 × 地盤」分組（一行一個組合），打卡明細帶職位和地盤，可逐日換 | ✅ 可用 | `AttendanceTableService.RecomputePayrollAsync` |
| Attendance | 改打卡明細後自動重算該月薪酬（只有時間 / O.T. / 勞工假 / 職位 / 地盤變動才觸發，只改備註不觸發；跨月時兩個月都重算） | ✅ 可用 | `PUT /api/attendance/punches/{id}` |
| Attendance | 員工（工人）管理：新增、編輯（姓名／工號／職位分類／日薪／在職，職位分類可選）、停用、刪除（軟刪除）；薪酬表與打卡明細的姓名篩選是員工下拉；職位分類在系統字典 `AttendancePosition` 維護，SeedData 只預置「樓面員工」，不回填現有員工 | ✅ 可用（僅 Admin，入口在考勤薪酬頁） | `AttendanceServices` / `DELETE /api/attendance/workers/{id}` |
| Attendance | 地盤：和會計票據共用系統字典 `AccountingSiteCategory`（「系統字典 → 地盤分類」維護，不再有獨立地盤表或 `/attendance/sites` 端點）；考勤卡同步時從這裡選地盤，停用不出現在下拉，已被打卡明細或票據引用時不能刪除 | ✅ 可用（字典頁，僅 Admin） | `SysDictService` / `POST /api/system/dicts` |
| Attendance | 計薪（工數、MPF、OT、實付人工） | ✅ 由考勤卡同步或改打卡明細時按 `AttendanceRules` 重算「一個員工一個月的一個職位×地盤組合」；未做全月一鍵重算、沒有定時任務 | `AttendanceTableService.RecomputePayrollAsync` |
| Attendance | 外部打卡 API 同步 | ❌ 未實現 | — |
| Attendance | 考勤卡照片掃描（prebuilt-layout → 逐行打卡並落庫，每欄帶置信度；手寫年月姓名不計入複核，可雙擊人工修正；兩步流程：先「標記為已核對」，成功後才出現「同步」，按「同步」導入打卡明細並重算該員工該月薪酬；未核對不能同步，改過內容要重新按「標記為已核對」） | ✅ 可用（與票據共用 Azure 憑據，需配置才啟用） | `POST /api/attendance/scan`、`GET /api/attendance/scans`、`PUT /api/attendance/scans/{id}`、`POST /api/attendance/scans/{id}/confirm`、`POST /api/attendance/scans/{id}/sync` |
| Accounting | 手動新增票據、上傳/補上原圖（**先裁剪再上傳**，已存原圖可重裁）、OCR 建議（每欄帶置信度，低於閾值提示人工複核）、刪除票據、ZIP 匯出 | ✅ 可用（OCR 需配置才啟用） | `ReceiptService` / `ImageCropDialog.vue` |
| Storage | 本地文件上傳 / 下載 / 刪除、MD5、**按 MD5 去重**（同內容共用一個物理文件，最後一筆記錄刪掉時才刪磁盤） | ✅ 可用 | `FileService` |
| Storage | 按地盤 / 帳戶的檔案權限（FilePermission） | ❌ 未實現，目前只有 Admin / 非 Admin 粗粒度 | — |
| Storage | 上傳類別的限制 | ⚠️ 只擋了「Accounting 分組需 Admin」，其餘分組任何已登入用戶都能上傳；是否需要更嚴的白名單待定 | `FileService.上传Async` |
| System | 系統字典：分組管理 + 字典項 CRUD，字典值自動編號、可調整排序 | ✅ 可用 | `SysDictGroupService` / `SysDictService` |
| 前端 | 登入、工作台、考勤、會計、文件、字典、用戶頁 | ✅ 可用 | `frontend/src/views` |

---

## 已知約定不一致（新代碼先別照抄，等統一）

| 項目 | 現況 |
|------|------|
| 命名 | 部分服務用中文方法名（`上傳Async`），部分英文（`ListAsync`），且繁體 / 簡體混用 |
| 種子資料 | `SeedData` 在非 Production 環境會自動建 `admin / Admin@123`（Production 要求 `Bootstrap:AdminPassword`）。**這是 AGENTS.md 金科玉律 7「不按環境分叉」的已知例外**，維護者已確認暫時保留；新代碼不要照抄這個模式 |
| Repository 分層 | Attendance 的打卡明細和薪酬表查詢直接用 `ErpDbContext`（`AttendanceTableService`），只有員工 CRUD 走 `IAttendanceRepository`；Accounting / System 有 Repository，Storage 直接用 DbContext。**不要為了統一而給每個 Service 硬套 Repository** |
| 系統分組 | SeedData 每次啟動會把內建分組的名稱和排序改回程式內建值，用戶對系統分組改名 / 調排序會在下次啟動被蓋掉 |
| 遷移時間戳 | 既有 migration 用的是**本地時間**命名，而 EF 10 的 `migrations add` 產生 **UTC** 時間。下午（本地）新增的 migration 可能排到同日較早建立的 migration **之前**，導致欄位還沒建立就建索引。新增後一定要跑 `dotnet ef migrations list` 確認它排在最後；不在最後就自己改名（id 只是排序用的字串，改檔案名 + `.Designer.cs` 裡的 `[Migration("...")]` 即可） |

---

## 環境與入口

| 項目 | 值 |
|------|-----|
| 後端（Development） | `http://localhost:5209`（`dotnet run`） |
| 前端（Development） | `http://localhost:5173`（`pnpm -C frontend dev`） |
| API 文件 | 所有環境開放 `/scalar` |
| 本地資料庫 | `localhost:5433`，`docker compose up -d postgres` |
| 一鍵驗證 | `pwsh -File scripts/check.ps1` |
| 輸入驗證 | 全域 `AddValidation()`：DataAnnotations 自動生效，失敗回 400 ProblemDetails |
| 錯誤格式 | 統一 ProblemDetails：業務異常由 `ExceptionProblemFilter` 轉 400/403/404/409/500；空 body 的 4xx/5xx 由 `UseStatusCodePages` 補齊 |
| CI | PR 與手動觸發；只做 Release 建置與打包，不跑 Checks；部署僅限默認分支手動觸發 |
