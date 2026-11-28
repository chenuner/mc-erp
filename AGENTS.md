# AGENTS.md — AI 編程入口

MC ERP — 香港建築管理公司 ERP 系統。簡單系統，不過度工程化。

模組：Identity（用戶/角色/認證）、Attendance（考勤/薪酬）、Accounting（會計/收據）、Storage（文件/照片）、System（系統字典）。

---

## 驗證命令（改完必跑，順序執行）

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
| 文檔檢查（連結 + AGENTS.md 行數） | `pwsh -File scripts/check-docs.ps1` |
| 倉庫清理 | `pwsh -File scripts/clean.ps1`（默認只掃描；加 `-Apply` 才刪） |

> 三個 Checks 共用 `backend` 的 Release 輸出，**不可並行**，否則 CS2012。CI 只做建置與打包，不跑 Checks；本地以本表為準。
>
> 清理倉庫：先跑不帶參數的 `scripts/clean.ps1` 看清單，只刪「能重新建出來」的東西。`backend/storage-files`（已上傳檔案本體）、`mc-erp-temp`（後端 TMP）、`appsettings*.json`、`backend/Migrations` 一律不刪。

---

## 金科玉律

1. **簡單優先** — 不加不需要的抽象、框架、配置。三行重複代碼勝過過早抽象。
2. **用公共組件** — 所有基礎設施走 `Common/`，減少不必要錯誤。
3. **CancellationToken 在基礎設施層** — 業務代碼和普通開發者不需要關注。
4. **ILogger\<T\> 通過 DI** — 不直接調 Serilog API。
5. **不暴露後端端口** — Nginx 統一入口。
6. **C# 14 語法糖** — 構造函數參數注入、primary constructor 等。
7. **不按環境分叉行為** — 不自行新增 `IsDevelopment()` / `IsProduction()` 分支來關閉功能；本地與生產保持一致。若認為必要，先停下來提醒維護者確認。
8. **不要外鍵（死規定）** — 資料庫**一律不建 FOREIGN KEY 約束**。EF 的導航屬性、`Include()`、關聯 DTO 照用，聯表 SQL 由模型生成；刪資料、清表也不該受約束順序限制。
   - 新增實體或關聯時：只建欄位 + `HasOne/HasMany` 導航，**不要讓 migration 產生 FK**；若 EF 已產生，就在同一個 migration 用 raw SQL `ALTER TABLE ... DROP CONSTRAINT IF EXISTS ...` 補掉。
   - 既有資料庫用 [20261122091500_DropAllForeignKeys](backend/Migrations/20261122091500_DropAllForeignKeys.cs) 一次清掉全部 FK。
   - 資料完整性由程式保證（寫入前先查存在），不要靠資料庫約束。

---

## 任務 → 先讀哪份（按需讀，不要一次全讀）

| 你要做的事 | 先讀 |
|-----------|------|
| 加 / 改端點、Service、Repository；搞清楚代碼寫在哪 | [specs/architecture.md](specs/architecture.md) |
| 改打卡、工數、薪酬公式、考勤卡掃描 | [specs/domain/attendance.md](specs/domain/attendance.md) |
| 改收據欄位、OCR、裁剪上傳、ZIP 提取 | [specs/domain/accounting.md](specs/domain/accounting.md) |
| 改文件上傳 / MD5 去重 / 下載 | [specs/domain/storage.md](specs/domain/storage.md) |
| 改字典分組 / 層級 / 樹形下拉 / 分類篩選 | [specs/domain/system-dict.md](specs/domain/system-dict.md) |
| 寫前端頁面、調樣式 | [specs/design-system.md](specs/design-system.md) |
| 想加依賴 / 選技術方案 | [specs/tech-stack.md](specs/tech-stack.md)（沒列的一律不可引入） |
| 不確定功能做了沒有 | [specs/status.md](specs/status.md) |
| 想看某個取捨當初為什麼這樣定 | [docs/archive/decisions-2026-11-22.md](docs/archive/decisions-2026-11-22.md)（歷史決策記錄） |

> 全部文檔的索引在 [specs/README.md](specs/README.md)。讀完相關那一份就開干，不必先把 specs 讀完。

---

## 改完要同步的文檔

| 你改了什麼 | 同步到哪裡 |
|-----------|-----------|
| 新增 / 刪除 / 改路由、DTO、入口 | `specs/status.md` 的「入口」欄；`specs/architecture.md` 目錄樹的註釋 |
| 業務規則（計薪、ZIP 文件夾、篩選語義、停用傳播） | 對應的 `specs/domain/*.md` |
| 新依賴或升級版本 | `specs/tech-stack.md`（先確認不在禁區） |
| 新的視覺模式、組件用法 | `specs/design-system.md` |
| 資料庫結構（表 / 欄位 / 索引） | `specs/architecture.md` 的「數據庫」與目錄樹註釋 |

> 文檔過期比沒有更糟：後面的 agent 會照著錯的做。

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
