# AGENTS.md — AI 編程行為規範

> 這是 AI 編程的入口文件。寫代碼前先讀這裡。

---

## 項目概覽

MC ERP — 香港建築管理公司 ERP 系統。簡單系統，不過度工程化。

四大模組：Identity（用戶/角色/認證）、Attendance（考勤/薪酬）、Accounting（會計/收據）、Storage（文件/照片）。

---

## 金科玉律

1. **簡單優先** — 不加不需要的抽象、框架、配置。三行重複代碼勝過過早抽象。
2. **用公共組件** — 所有基礎設施走 `Common/`，減少不必要錯誤。
3. **CancellationToken 在基礎設施層** — 業務代碼和普通開發者不需要關注。
4. **ILogger\<T\> 通過 DI** — 不直接調 Serilog API。
5. **不暴露後端端口** — Nginx 統一入口。
6. **C# 14 語法糖** — 構造函數參數注入、primary constructor 等。
7. **不按環境分叉行為** — 不自行新增 `IsDevelopment()` / `IsProduction()` 分支來關閉功能；本地與生產保持一致。若認為必要，先停下來提醒維護者確認。
8. **不要外鍵（死規定）** — 資料庫**一律不建 FOREIGN KEY 約束**。EF 的導航屬性、`Include()`、關聯 DTO 照用，聯表 SQL 由模型生成，不需要資料庫約束；刪資料、清表也不該受約束順序限制。
   - 新增實體或關聯時：只建欄位 + `HasOne/HasMany` 導航，**不要讓 migration 產生 FK**；若 EF 已產生，就在同一個 migration 用 raw SQL `ALTER TABLE ... DROP CONSTRAINT IF EXISTS ...` 補掉。
   - 既有資料庫用 [20261122091500_DropAllForeignKeys](backend/Migrations/20261122091500_DropAllForeignKeys.cs) 一次清掉全部 FK；新環境由這支 migration 在最後把約束刪掉。
   - 資料完整性由程式保證（寫入前先查存在），不要靠資料庫約束。

---

## 文件地圖

| 讀什麼 | 去哪讀 |
|--------|--------|
| 功能到底做了沒有 | [specs/status.md](specs/status.md) |
| 能用什麼技術 | [specs/tech-stack.md](specs/tech-stack.md) |
| 代碼寫在哪、怎麼分層 | [specs/architecture.md](specs/architecture.md) |
| 前端長什麼樣 | [specs/design-system.md](specs/design-system.md) |
| 項目概覽和快速開始 | [README.md](README.md) |

---

## 閱讀順序

1. 讀 [specs/status.md](specs/status.md) — 知道現在有什麼、沒有什麼
2. 讀 [specs/tech-stack.md](specs/tech-stack.md) — 知道能用什麼
3. 讀 [specs/architecture.md](specs/architecture.md) — 知道寫在哪
4. 開干，改完跑驗證命令

---

## 驗證命令（每次改動後必跑，順序執行）

| 目的 | 命令 |
|------|------|
| 一鍵全部 | `pwsh -File scripts/check.ps1` |
| 後端建置 | `dotnet build backend/MC.Erp.slnx` |
| 身分檢查 | `dotnet run --project tests/Identity.Checks/Identity.Checks.csproj -c Release` |
| 會計檢查 | `dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release` |
| 考勤檢查 | `dotnet run --project tests/Attendance.Checks/Attendance.Checks.csproj -c Release` |
| 整合檢查（需資料庫） | `dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release -- --integration` |
| 前端型別 + 建置 | `pnpm -C frontend run build` |
| 前端純函數檢查（裁剪 / 字典層級） | `pnpm -C frontend run check` |
| 倉庫清理 | `pwsh -File scripts/clean.ps1`（默認只掃描；加 `-Apply` 才刪） |

> 清理倉庫時：先跑一次不帶參數的 `scripts/clean.ps1` 看清單，只刪「能重新建出來」的東西（構建產物、臨時目錄、日誌、`scripts/clean.ps1` 的 Basic 段）。`backend/storage-files`（已上傳檔案本體）、`mc-erp-temp`（後端 TMP）、`appsettings*.json`、`backend/Migrations`、`backend/storage-files` 相關資料一律不刪；刪完跑 `scripts/check.ps1`。

> 三個 Checks 共用 `backend` 的 Release 輸出，**不可並行**，否則 CS2012。
>
> CI 只做建置與打包，不跑 Checks；本地以本表為準。

---

## 新端點檢查清單

1. 放 `Features/{Module}/Endpoints`，Service / Repository 分層
2. 回傳 `TypedResults` + ProblemDetails，不要 `BadRequest<string>`；業務異常直接 `throw`，由 `ExceptionProblemFilter` 統一轉換
3. 分頁用 `Common/PagedResult<T>`，不要另定義第三個型別
4. 權限明確：`RequireAuthorization("admin")` 或 `RequireAuthorization()`
5. 輸入驗證：`AddValidation()` 已全域啟用，DTO 直接標 DataAnnotations（record 主構造參數同樣生效）
6. 補一個 `.http` 範例，至少一個 Check
7. 跑完驗證命令，把結果貼在 PR

---

## 禁區

- 不引入 specs/tech-stack.md 未列出的依賴
- 不引入 DDD 聚合根、CQRS、MediatR、Wolverine 等重型模式
- 不引入 Redis、OpenTelemetry、Seq
- 前端不引入第二個 UI 庫、不引入 @tanstack/vue-query
- 不繞過 specs 寫代碼，代碼和 specs 衝突時以 specs 為準
