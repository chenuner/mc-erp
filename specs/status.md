# status.md — 當前實現狀態

> specs 其餘文件描述「目標態」，本文件記錄「當前態」。動手前先看這裡，不要把未實現的功能當成已存在。
>
> **專案意志**：本地與生產的行為保持一致，不按 `ASPNETCORE_ENVIRONMENT` 切換功能（API 文檔、模擬資料初始化等）。要新增 `IsDevelopment()` / `IsProduction()` 分支前，必須先向維護者確認。
>
> 最後更新：2026-11-22

---

## 模組狀態

| 模組 | 功能 | 狀態 | 入口 |
|------|------|------|------|
| Identity | 登入、用戶/角色管理、JWT | ✅ 可用 | `Features/Identity` |
| Attendance | 薪酬表 / 打卡表查詢（薪酬表另可按當月職位快照篩選）、人工維護、按考勤卡重算薪酬 | ✅ 可用 | `AttendanceTableService` / `AttendanceRules` |
| Attendance | 員工（工人）管理：新增、編輯（姓名／工號／職位分類／日薪／在職，職位分類可選）、停用、刪除（軟刪除）；薪酬表與打卡明細的姓名篩選是員工下拉；職位分類在系統字典 `AttendancePosition` 維護，SeedData 只預置「樓面員工」，不回填現有員工 | ✅ 可用（僅 Admin，入口在考勤薪酬頁） | `AttendanceServices` / `DELETE /api/attendance/workers/{id}` |
| Attendance | 計薪（工數、MPF、OT、實付人工） | ✅ 由考勤卡同步時按 `AttendanceRules` 重算「一個員工一個月」；未做全月一鍵重算、沒有定時任務 | `AttendanceCardService.SyncAsync` |
| Attendance | 外部打卡 API 同步 | ❌ 未實現；`ExternalAttendanceApi` 只提供模擬資料 | — |
| Attendance | 模擬資料初始化 | ✅ 所有環境可用（管理員）；刻意不關閉 | `POST /api/attendance/mock` |
| Attendance | 考勤卡照片掃描（prebuilt-layout → 逐行打卡並落庫，每欄帶置信度；手寫年月姓名不計入複核，可雙擊人工修正，可按「標記為已核對」放行；按「同步」導入打卡明細並重算該員工該月薪酬） | ✅ 可用（與票據共用 Azure 憑據，需配置才啟用） | `POST /api/attendance/scan`、`GET /api/attendance/scans`、`PUT /api/attendance/scans/{id}`、`POST /api/attendance/scans/{id}/confirm`、`POST /api/attendance/scans/{id}/sync` |
| Accounting | 手動新增票據、上傳/補上原圖、OCR 建議（每欄帶置信度，低於閾值提示人工複核）、刪除票據、ZIP 匯出 | ✅ 可用（OCR 需配置才啟用） | `ReceiptService` |
| Storage | 本地文件上傳 / 下載 / 刪除、MD5 | ✅ 可用 | `FileService` |
| Storage | 按地盤 / 帳戶的檔案權限（FilePermission） | ❌ 未實現，目前只有 Admin / 非 Admin 粗粒度 | — |
| System | 系統字典：分組管理 + 字典項 CRUD，字典值自動編號、可調整排序 | ✅ 可用 | `SysDictGroupService` / `SysDictService` |
| 前端 | 登入、工作台、考勤、會計、文件、字典、用戶頁 | ✅ 可用 | `frontend/src/views` |

---

## 已知約定不一致（新代碼先別照抄，等統一）

| 項目 | 現況 |
|------|------|
| 命名 | 部分服務用中文方法名（`上傳Async`），部分英文（`ListAsync`），且繁體 / 簡體混用 |

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
