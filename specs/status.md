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
| Attendance | 薪酬表 / 打卡表查詢、人工維護 | ✅ 可用 | `AttendanceTableService` |
| Attendance | 自動計薪、MPF、OT 計算 | ❌ 未實現；原 `PayrollService` 未註冊已移除（算法見 git 歷史） | — |
| Attendance | 外部打卡 API 同步 | ❌ 未實現；`ExternalAttendanceApi` 只提供模擬資料 | — |
| Attendance | 模擬資料初始化 | ✅ 所有環境可用（管理員）；刻意不關閉 | `POST /api/attendance/mock` |
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
