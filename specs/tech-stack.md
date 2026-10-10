# tech-stack.md — 技術棧規範

> 決定"能寫什麼"。任何不在此文件中的依賴 / 框架 / 工具，默認不能用。
>
> 原則：**夠用就好，不過度工程化。**

---

## 後端

### 運行時 & 語言

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| .NET | 10 LTS | ✅ 已採納 | LTS，支持至 2028-11-14 |
| C# | 14 | ✅ 已採納 | 隨 .NET 10 發布，使用 primary constructor 等語法糖 |
| ASP.NET Core | 10 | ✅ 已採納 | Minimal API 模式 |

### 核心框架

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Entity Framework Core | 10.x | ✅ 已採納 | ORM，Code First |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.x | ✅ 已採納 | PostgreSQL 提供程序 |
| Scrutor | 7.0.0 | ✅ 已採納 | 程序集掃描註冊 |
| .NET 置 DataAnnotations | - | ✅ 已採納 | 參數校驗，不用 FluentValidation |

### 數據庫 & 緩存

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| PostgreSQL | 16+ | ✅ 已採納 | 主數據庫 |
| IMemoryCache（.NET 內置） | - | ✅ 已採納 | 簡單系統用內存緩存即可，不引入 Redis |

### 認證 & 安全

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| JWT Bearer | 10.x | ✅ 已採納 | 身份認證 |
| Isopoh.Cryptography.Argon2 | 1.4.x | ✅ 已採納 | 密碼哈希（Argon2id） |

### 後台任務

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Hangfire | 1.8.x | ✅ 已採納 | 定時拉取外部考勤打卡數據 |
| Hangfire.PostgreSql | 1.20.x | ✅ 已採納 | PostgreSQL 存储 |

### 外部集成

| 技術 | 狀態 | 說明 |
|------|------|------|
| IHttpClientFactory | ✅ 已採納 | 調用外部考勤接口，由基礎設施層統一管理 CancellationToken |
| OCR 服務（外部） | ✅ 已採納 | 收據拍照識別，接口抽象為 `IOcrService`，待選型 Azure / 雲廠商 |
| 本地文件存儲 | ✅ 已採納 | MVP 階段文件存本地磁盤，抽象為 `IFileStorage` 接口，未來換對象存儲只需改 DI |

### 日誌

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Serilog.AspNetCore | 9.x | ✅ 已採納 | 結構化日誌，通過 DI 注入 ILogger\<T\> |
| Serilog.Sinks.Console / File | 6.x | ✅ 已採納 | 控制台 + 文件輸出，簡單系統不需要 Seq |

### API 文檔 & 健康檢查

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Scalar.AspNetCore | 2.10.x | ✅ 已採納 | API 文檔 UI |
| Microsoft.AspNetCore.OpenApi | 10.x | ✅ 已採納 | OpenAPI 生成 |
| AspNetCore.HealthChecks.NpgSql | 8.0.x | ✅ 已採納 | DB 健康檢查 |

### 禁止引入

- ~~Wolverine~~ — 已移除，簡單系統不需要消息總線
- FluentValidation — 用 .NET 內置 DataAnnotations
- AutoMapper — 手寫映射
- OpenTelemetry — 簡單系統不需要分布式可觀測性
- Redis — 低並發場景用內存緩存即可
- Seq — 日誌看文件就夠了
- MediatR / 任何 CQRS 框架 — 直接調 Service

---

## 前端

### 框架 & 語言

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Vue | 3.x | ✅ 已採納 | 漸進式前端框架 |
| TypeScript | 5.x | ✅ 已採納 | 類型安全 |
| Vite | 最新 | ✅ 已採納 | 構建工具 |

### 核心庫

| 技術 | 版本 | 狀態 | 說明 |
|------|------|------|------|
| Pinia | 最新 | ✅ 已採納 | 狀態管理 |
| Vue Router | 最新 | ✅ 已採納 | 路由 |
| Element Plus | 最新 | ✅ 已採納 | UI 組件庫（唯一 UI 庫，禁止第二個） |
| Axios | 最新 | ✅ 已採納 | HTTP 客戶端 |
| @vueuse/core | 最新 | ✅ 已採納 | 組合式工具集 |
| ECharts | 最新 | ✅ 已採納 | 圖表庫（唯一，用法遵循 specs/design-system.md 圖表規範） |

### 工程化

| 技術 | 狀態 | 說明 |
|------|------|------|
| pnpm | ✅ 已採納 | 包管理器（禁止 npm/yarn） |

### 禁止引入（前端）

- 第二個 UI 組件庫（統一 Element Plus）
- @tanstack/vue-query — 簡單系統用 Axios + Pinia 即可
- unplugin-auto-import / unplugin-vue-components — 顯式導入更清晰
- jQuery / Lodash — 原生 JS 或 VueUse 替代

---

## DevOps & 基礎設施

| 技術 | 狀態 | 說明 |
|------|------|------|
| Docker + Docker Compose | ✅ 已採納 | 本地開發棧（PostgreSQL + 前端） |
| Nginx | ✅ 已採納 | 前端反向代理 + 後端 API 轉發，不直接暴露後端端口 |

---

## 環境約束

- 數據庫：PostgreSQL 16+，不兼容 MySQL / SQL Server
- 操作系統：Linux 生產部署，開發環境不限
- 包管理器：後端 NuGet，前端 pnpm（禁止混用 npm）
- 外部流量：統一走 Nginx，後端端口不對外暴露
