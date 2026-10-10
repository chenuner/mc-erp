# specs — 唯一真理源

> specs/ 是本项目所有技术规范的唯一真理源。Git 管理，禁止在外部定义规范。

---

## 文档地图

| 文档 | 作用 | 什么时候读 |
|------|------|-----------|
| [tech-stack.md](./tech-stack.md) | 决定"能写什么" — 语言、框架、库版本、环境约束 | 引入新依赖、选技术方案前 |
| [coding-rules.md](./coding-rules.md) | 决定"怎么写" — 命名、格式化、静态分析、提交规范 | 写代码、Code Review 前 |
| [architecture.md](./architecture.md) | 决定"写在哪" — 分层、模块边界、依赖流向、物理结构 | 新建文件、加模块、跨模块调用前 |
| [refactor-guard.md](./refactor-guard.md) | 契约禁令 — API/DTO/Contracts/序列化兼容规则 | 重构、改公开接口前 |
| [protected-areas.md](./protected-areas.md) | 禁区清单 — 文件路径、模块名、变更审批人 | 碰核心模块前 |
| [failure-analysis.md](./failure-analysis.md) | 故障分析六连模板 — 现象/级别/根因/修复/预防/关联ADR | 出故障、写复盘时 |
| [glossary.md](./glossary.md) | 术语表 — 业务词、代码映射、Agent 消歧专用 | 遇到不确定的业务术语时 |
| [adr/README.md](./adr/README.md) | ADR 编写规范 | 写 ADR 前 |
| [adr/index.md](./adr/index.md) | ADR 索引表 — 编号/标题/状态/日期/影响域 | 找历史决策时 |

---

## 阅读顺序

**通用编码任务**：tech-stack.md → architecture.md → coding-rules.md → 开干

**重构任务**：architecture.md → refactor-guard.md → protected-areas.md → tech-stack.md → 开干

**排障任务**：failure-analysis.md → architecture.md → glossary.md → 排查

**架构决策**：adr/index.md → 相关 ADR → architecture.md → 写新 ADR

---

## 状态定义

| 状态 | 含义 |
|------|------|
| ✅ 已采纳 | 当前有效规范，必须遵守 |
| ⚠️ 迁移中 | 新旧并存，新代码用新方案，旧代码逐步迁移 |
| ❌ 已废弃 | 不再使用，仅作历史参考 |
| 📝 草稿 | 讨论中，未正式采纳，不强制执行 |

---

## 修改流程

1. 发现 specs 需要修改
2. 修改对应文件
3. 如果是架构级变更 → 新增 ADR，更新 `adr/index.md`
4. 合并后立即同步代码实现

**规则**：代码和 specs 不一致时，以 specs 为准。specs 错了就改 specs，但不能绕过 specs。
