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
6. **C# 12+ 語法糖** — 構造函數參數注入、primary constructor 等。

---

## 文件地圖

| 讀什麼 | 去哪讀 |
|--------|--------|
| 能用什麼技術 | [specs/tech-stack.md](specs/tech-stack.md) |
| 代碼寫在哪、怎麼分層 | [specs/architecture.md](specs/architecture.md) |
| 前端長什麼樣 | [specs/design-system.md](specs/design-system.md) |
| 項目概覽和快速開始 | [README.md](README.md) |

---

## 閱讀順序

1. 讀 [specs/tech-stack.md](specs/tech-stack.md) — 知道能用什麼
2. 讀 [specs/architecture.md](specs/architecture.md) — 知道寫在哪
3. 開干

---

## 禁區

- 不引入 specs/tech-stack.md 未列出的依賴
- 不引入 DDD 聚合根、CQRS、MediatR、Wolverine 等重型模式
- 不引入 Redis、OpenTelemetry、Seq
- 前端不引入第二個 UI 庫、不引入 @tanstack/vue-query
- 不繞過 specs 寫代碼，代碼和 specs 衝突時以 specs 為準
