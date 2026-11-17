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
| 整合檢查（需資料庫） | `dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release -- --integration` |
| 前端型別 + 建置 | `pnpm -C frontend run build` |

> 兩個 Checks 共用 `backend` 的 Release 輸出，**不可並行**，否則 CS2012。
>
> CI 只做建置與打包，不跑 Checks；本地以本表為準。

---

## 新端點檢查清單

1. 放 `Features/{Module}/Endpoints`，Service / Repository 分層
2. 回傳 `TypedResults` + ProblemDetails，不要 `BadRequest<string>`
3. 分頁先用 `PagedResult<T>`，不要新增第三個型別
4. 權限明確：`RequireAuthorization("admin")` 或 `RequireAuthorization()`
5. 輸入驗證要真的生效：`AddValidation()` 或 endpoint filter 內 `Validator.TryValidateObject`
6. 補一個 `.http` 範例，至少一個 Check
7. 跑完驗證命令，把結果貼在 PR

---

## 禁區

- 不引入 specs/tech-stack.md 未列出的依賴
- 不引入 DDD 聚合根、CQRS、MediatR、Wolverine 等重型模式
- 不引入 Redis、OpenTelemetry、Seq
- 前端不引入第二個 UI 庫、不引入 @tanstack/vue-query
- 不繞過 specs 寫代碼，代碼和 specs 衝突時以 specs 為準
