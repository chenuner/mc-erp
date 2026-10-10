# tech-stack.md — 技术栈规范

> 决定"能写什么"。任何不在此文件中的依赖 / 框架 / 工具，默认不能用。

---

## 后端

### 运行时 & 语言

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| .NET | 10 LTS | ✅ 已采纳 | LTS，支持至 2028-11-14 |
| C# | 14 | ✅ 已采纳 | 随 .NET 10 发布 |
| ASP.NET Core | 10 | ✅ 已采纳 | Minimal API 模式 |

### 核心框架

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Entity Framework Core | 10.x | ✅ 已采纳 | ORM，Code First |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.x | ✅ 已采纳 | PostgreSQL 提供程序 |
| Scrutor | 7.0.0 | ✅ 已采纳 | 装饰器注册 + 程序集扫描 |
.NET 内置 DataAnnotations

### 数据库 & 缓存

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| PostgreSQL | 16+ | ✅ 已采纳 | 主数据库 |
| StackExchange.Redis | 2.8.x | ⚠️ 待升级 | 缓存，计划迁移到 FusionCache |

### 认证 & 安全

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| JWT Bearer | 10.x | ✅ 已采纳 | 身份认证 |
| Isopoh.Cryptography.Argon2 | 1.4.x | ✅ 已采纳 | 密码哈希（Argon2id） |

### 后台任务

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Hangfire | 1.8.x | ✅ 已采纳 | 定时任务 + 延迟任务 |
| Hangfire.PostgreSql | 1.20.x | ✅ 已采纳 | PostgreSQL 存储 |


### 日志 & 可观测性

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Serilog.AspNetCore | 9.x | ✅ 已采纳 | 结构化日志 |
| Serilog.Sinks.Console / File | 6.x | ✅ 已采纳 | 日志输出 |
| Serilog.Sinks.Seq | 6.x | ✅ 已采纳 | 日志集中查看（Seq Web UI） |
| Seq | latest | ✅ 已采纳 | 日志查询 Web 界面（容器部署） |
| OpenTelemetry | 1.10.x | ✅ 已采纳 | 可观测性采集 |
| OpenTelemetry Instrumentation（AspNetCore / EF Core / Http / Runtime / Process） | - | ✅ 已采纳 | 各层自动埋点 |

### API 文档

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Scalar.AspNetCore | 2.10.x | ✅ 已采纳 | API 文档 UI |
| Microsoft.AspNetCore.OpenApi | 10.x | ✅ 已采纳 | OpenAPI 生成 |

### 健康检查

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| AspNetCore.HealthChecks.NpgSql | 8.0.x | ✅ 已采纳 | DB 健康检查 |
| AspNetCore.HealthChecks.Redis | 8.0.x | ✅ 已采纳 | Redis 健康检查 |

### 禁止引入（MVP 阶段不需要）

- FluentValidation（已淘汰，用 .NET 内置 DataAnnotations）
AutoMapper 手写映射
---

## 前端

### 框架 & 语言

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Vue | 最新 | ✅ 已采纳 | 渐进式前端框架 |
| TypeScript | 最新 | ✅ 已采纳 | 类型安全 |
| Vite | 最新 | ✅ 已采纳 | 构建工具 |

### 核心库

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| Pinia | 最新 | ✅ 已采纳 | 状态管理 |
| Vue Router | 最新 | ✅ 已采纳 | 路由 |
| Element Plus | 最新 | ✅ 已采纳 | UI 组件库 |
| Axios |最新 | ✅ 已采纳 | HTTP 客户端 |
| @tanstack/vue-query | 最新 | ✅ 已采纳 | 服务端状态管理 + 缓存 |
| @vueuse/core | 最新 | ✅ 已采纳 | 组合式工具集 |

### 工程化

| 技术 | 版本 | 状态 | 说明 |
|------|------|------|------|
| unplugin-auto-import | 0.17.x | ✅ 已采纳 | 自动导入 |
| unplugin-vue-components | 0.26.x | ✅ 已采纳 | 组件自动导入 |
| ESLint | 8.x | ✅ 已采纳 | 代码检查 |
| Prettier | 3.2.x | ✅ 已采纳 | 代码格式化 |
| pnpm | latest | ✅ 已采纳 | 包管理器（禁止 npm/yarn） |

### 禁止引入（前端）

- 第二个 UI 组件库（统一 Element Plus）
- 第二个状态管理方案（统一 Pinia + Vue Query）
- jQuery / Lodash 这类老工具（原生 JS 或 VueUse 替代）

---

## DevOps & 基础设施

| 技术 | 状态 | 说明 |
|------|------|------|
| Docker + BuildKit | ✅ 已采纳 | 容器化 |
| Docker Compose | ✅ 已采纳 | 本地开发栈 |
| GitHub Actions | ✅ 已采纳 | CI/CD |
| Nginx | ✅ 已采纳 | 前端反向代理 |
---

## 环境约束

- 数据库：PostgreSQL 16+，不兼容 MySQL / SQL Server
- 缓存：Redis 7+
- 操作系统：Linux 生产部署，开发环境不限
- 包管理器：后端用 NuGet，前端用 pnpm（禁止混用 npm）
