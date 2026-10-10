# AIO

现代化全栈应用，基于 ASP.NET Core Minimal API + Vue 3 + TypeScript。

## 项目结构

```
AIO/
├── AGENTS.md              # AI 编程行为宪法（入口文件）
├── specs/                 # 技术规范唯一真理源
│   ├── tech-stack.md      # 技术栈规范
│   ├── coding-rules.md    # 编码规则
│   ├── architecture.md    # 架构规范
│   ├── refactor-guard.md  # 契约禁令
│   ├── protected-areas.md # 禁区清单
│   ├── failure-analysis.md# 故障分析模板
│   ├── glossary.md        # 术语表
│   └── adr/               # 架构决策记录
├── backend/               # ASP.NET Core 后端
│   ├── BuildingBlocks/    # 受控共享基础设施
│   ├── Modules/           # 业务模块（垂直切片）
│   ├── Infrastructure/    # 全局基础设施实现
│   ├── Migrations/        # EF Core 迁移文件
│   └── AIO.csproj
├── frontend/              # Vue 3 前端
│   ├── src/
│   │   ├── api/           # API 接口
│   │   ├── components/    # 组件
│   │   ├── stores/        # Pinia 状态管理
│   │   ├── views/         # 页面视图
│   │   ├── App.vue
│   │   ├── main.ts
│   │   └── style.css
│   ├── index.html
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig.json
├── docs/                  # 项目管理文档
│   ├── 01-trd/            # 业务需求
│   ├── 04-deployment/     # 部署运维
│   ├── 05-implementation/ # 实施计划
│   ├── 06-changelog/      # 修订记录
│   └── modernization-roadmap.md  # AI 指令清单
├── .github/workflows/     # CI/CD 流水线
├── .gitignore
└── README.md
```

## 文档

**技术规范**（specs/）— AI 编程和开发者的唯一真理源：给ai看的

| 文档 | 作用 |
|------|------|
| [AGENTS.md](AGENTS.md) | 入口地图 + AI 行为宪法 |
| [specs/README.md](specs/README.md) | specs 导航页 |
| [specs/tech-stack.md](specs/tech-stack.md) | 能写什么 |
| [specs/coding-rules.md](specs/coding-rules.md) | 怎么写 |
| [specs/architecture.md](specs/architecture.md) | 写在哪 |
| [specs/refactor-guard.md](specs/refactor-guard.md) | 契约禁令 |
| [specs/protected-areas.md](specs/protected-areas.md) | 禁区清单 |
| [specs/glossary.md](specs/glossary.md) | 术语表 |
| [specs/adr/index.md](specs/adr/index.md) | 架构决策记录索引 |

**项目管理文档**（docs/）— 需求、部署、实施计划：给人看的

| 目录 | 说明 |
|------|------|


## 技术栈

### 后端
- **.NET 10 LTS** - ASP.NET Core Minimal API
- **PostgreSQL** - 数据库
- **EF Core** - ORM
- **JWT** - 认证
- **Argon2id** - 密码哈希
- **Redis** - 分布式缓存
- **Hangfire** - 后台任务
- **Serilog** - 日志
- **OpenTelemetry** - 可观测性

### 前端
- **Vue 3** - 框架
- **TypeScript** - 语言
- **Vite** - 构建工具
- **Pinia** - 状态管理
- **Vue Router** - 路由
- **Element Plus** - UI 组件库
- **Axios** - HTTP 客户端
- **@tanstack/vue-query** - 服务端状态管理

完整技术栈规范见 [specs/tech-stack.md](specs/tech-stack.md)。

## 快速开始

### 环境要求
- .NET 10 SDK
- Node.js >= 20
- pnpm
- PostgreSQL
- Redis

### 启动后端

```bash
cd backend
dotnet restore
dotnet run
```

后端服务运行在 `https://localhost:5001`

### 启动前端

```bash
cd frontend
pnpm install
pnpm run dev
```

前端运行在 `http://localhost:3000`

### API 文档

访问 `https://localhost:5001/scalar` 查看 Scalar API 文档。

### 健康检查

访问 `https://localhost:5001/health` 查看服务健康状态。

## 开发

### 添加新功能模块

1. 阅读 [specs/architecture.md](specs/architecture.md) 了解模块结构
2. 在 `backend/Modules/{ModuleName}/` 下创建目录，按 API / Application / Contracts / Domain / Infrastructure 分层
4. 创建 Endpoints（实现 `IEndpointModule`）
5. 创建 ModuleInitializer（实现 `IModuleInitializer`）
6. 无需修改 `Program.cs`，自动注册

### 数据库迁移

```bash
cd backend
dotnet ef migrations add <MigrationName> --project AIO.csproj
dotnet ef database update
```

## License

MIT
