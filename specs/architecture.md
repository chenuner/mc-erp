# architecture.md — 架构规范

> 决定"写在哪"。分层、模块边界、依赖流向、物理结构。

---

## 总体架构

模块化单体（Modular Monolith）+ 领域驱动设计（DDD）+ CQRS。

每个模块是独立的业务域，有自己的 Domain / Application / Infrastructure / API 四层，通过 Contracts 层与其他模块通信。

---

## 分层架构

```
┌─────────────────────────────────┐
│          API 层（端点）          │  ← HTTP 请求入口，薄，只做参数绑定和响应
├─────────────────────────────────┤
│      Application 层（用例）      │  ← Command/Query/Handler，业务流程编排
├─────────────────────────────────┤
│       Domain 层（领域核心）      │  ← 实体 / 值对象 / 仓储接口 / 领域服务
├─────────────────────────────────┤
│    Infrastructure 层（实现）     │  ← EF Core / 仓储实现 / 外部服务 / 缓存
└─────────────────────────────────┘
```

### 依赖规则（严格执行）

1. **Domain 层不依赖任何其他层** — 纯业务逻辑，无基础设施依赖
2. **Application 层依赖 Domain 层** — 调用领域对象和仓储接口
3. **Infrastructure 层依赖 Domain + Application** — 实现仓储接口和外部服务
4. **API 层依赖 Application + Contracts** — 只做 HTTP 适配

### Contracts 层

Contracts 是模块对外暴露的公共契约，其他模块只能引用 Contracts，不能直接引用 Domain / Infrastructure。

```
Contracts/
├── Dto/       # 数据传输对象
├── Events/    # 领域事件（其他模块可以订阅）
├── Queries/   # 查询接口（其他模块可以调用）
└── Commands/  # 命令接口（其他模块可以调用，谨慎使用）
```

---

## 模块边界

### 模块清单

| 模块 | 职责 | 核心聚合 |
|------|------|---------|
| Account | 用户、认证（JWT/密码/第三方登录）、提现收款账号（AES 加密） | User / ThirdPartyIdentity / WithdrawalAccount |
| Administration | 系统管理、字典、敏感词、系统参数 | BannedWord / DictionaryItem / SystemParameter |
| SystemConfigs | 系统配置 | SystemConfig |


### 模块间通信规则

1. **只能通过 Contracts 层通信** — 禁止直接引用其他模块的 Domain / Infrastructure
2. **查询用 Queries 接口** — 其他模块的只读查询通过 `I*Queries` 接口
3. **写操作用领域事件** — 模块间通过发布/订阅领域事件解耦
4. **禁止跨模块直接修改数据** — 只能通过对方模块暴露的 Command 接口（极少用）
5. **禁止循环依赖** — A → B → A 是不允许的






## 物理目录结构

```
backend/
├── AIO.csproj          # 主项目（单项目多模块）
├── Program.cs              # 入口
├── appsettings.json        # 配置
├── BuildingBlocks/         # 公共基础设施
│   ├── Domain/             # 领域基类：Entity / AggregateRoot / IDomainEvent 等
│   ├── Application/        # 应用基类：PagedQuery / PagedResult 等
│   ├── Aop/                # 横切关注点：身份认证、权限检查，要可以user.id直接获取用户信息
│   ├── Middleware/         # 中间件：DomainExceptionHandler 等
│   └── Infrastructure/     # 基础设施基类：EF Core 仓储基类等
├── Modules/                # 业务模块
│   └── {ModuleName}/
│       ├── API/
│       ├── Application/
│       ├── Contracts/
│       ├── Domain/
│       └── Infrastructure/
└── tests/
    └── Architecture.Tests/ # 架构测试

frontend/
├── src/
│   ├── api/                # API 调用
│   ├── components/         # 通用组件
│   ├── views/              # 页面
│   ├── stores/             # Pinia stores
│   ├── router/             # 路由
│   ├── utils/              # 工具函数
│   ├── composables/        # 组合式函数
│   └── assets/             # 静态资源
├── package.json
├── vite.config.ts
└── tsconfig.json
```

---

## 数据库

- 每个模块有自己的 DbContext
- 所有表在同一个 PostgreSQL 数据库中
- 表命名：`{module_}table_name`（下划线小写）
- 主键：`Guid` 类型，字段名 `id`
- 创建时间：`created_at`
- 更新时间：`updated_at`
- 软删除：`is_deleted` + `deleted_at`（聚合根如果需要）

---

## API 规范

- RESTful 风格
- 路由前缀：`/api/{module}/{resource}`
- 版本：URL 版本化（如 `/api/v1/orders`）
- 统一返回格式：`TypedResults` + ProblemDetails（RFC 9457）
- 分页：`page` + `pageSize` 参数，返回 `{ items, total, page, pageSize }`
- 认证：Bearer JWT Token
- 授权：`[Authorize]` + Policy

---

## 架构演进状态

| 决策 | 状态 | ADR |
|------|------|-----|
| 模块化单体 | ✅ 已采纳 | - |
| DDD 分层 | ✅ 已采纳 | - |
| CQRS | ✅ 已采纳 | - |
| Identity 合并到 Account | ✅ 已采纳 
| 删除 WalletType 单钱包 | ⚠️ Phase 4 待执行 
| WithdrawalAccount AES-GCM 加密 | ✅ 已采纳 
| VSA（Vertical Slice Architecture） | ✅ 已采纳 
