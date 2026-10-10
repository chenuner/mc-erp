---
name: 准备阶段：安装前后端开发技能并建立设计系统基础
overview: 确认命名规范已写入 specs（已完成），安装前端设计技能（frontend-design、element-plus-ui-vue3）与后端 .NET 规范技能（dotnet-claude-kit 精选子集），并用 ui-ux-pro-max 为 MC ERP 生成设计系统存入 specs/design-system.md，为后续开发做准备。
design:
  styleKeywords:
    - 现代企业级
    - 专业工程感
    - 沉稳清爽
    - 卡片化分区
    - 克制微动效
  fontSystem:
    fontFamily: PingFang-SC
    heading:
      size: 24px
      weight: 600
    subheading:
      size: 18px
      weight: 500
    body:
      size: 14px
      weight: 400
  colorSystem:
    primary:
      - "#1D5BBF"
      - "#2E73E8"
      - "#E8F0FC"
    background:
      - "#F5F7FA"
      - "#FFFFFF"
      - "#1B2432"
    text:
      - "#1F2937"
      - "#6B7280"
      - "#C9D4E4"
    functional:
      - "#18A058"
      - "#E6A23C"
      - "#D9363E"
todos:
  - id: verify-naming-spec
    content: 复核 specs/architecture.md 命名规范已完整覆盖“业务中文/基础设施英文/Identity 属基础设施”，措辞完整则不改，缺口才微调
    status: completed
  - id: install-frontend-skills
    content: 用 [skill:find-skills] 安装 frontend-design 与 element-plus-ui-vue3 至 ~/.codebuddy/skills/ 并验证 SKILL.md
    status: completed
  - id: install-backend-skills
    content: 用 [skill:find-skills] 安装 dotnet-claude-kit 子集（build-fix/serilog/logging/de-sloppify/project-setup），排除 opentelemetry 与 onion 架构类
    status: completed
  - id: generate-design-system
    content: 用 [skill:ui-ux-pro-max] 生成 MC ERP 设计系统，落档为 specs/design-system.md（含配色/字体/布局/图标约定，遵循简单优先）
    status: completed
    dependencies:
      - install-frontend-skills
  - id: update-agents-map
    content: 在 AGENTS.md 文件地图中补充 specs/design-system.md 一行引用，其余内容不动
    status: completed
    dependencies:
      - generate-design-system
---

## 用户需求

1. **命名规范写入 specs**：业务代码（Attendance / Accounting / Storage）允许中文方法名、中文变量名，以方便为主；基础设施（Common/ + Identity）视为通用体系，用英文命名。调研确认该规范已存在于 `specs/architecture.md`（已提交），本计划仅需复核措辞是否完整覆盖，不重复改动。
2. **准备阶段**：找技能安装使用，目标是**前端设计好看、后端设计规范**——安装前端设计类技能与 .NET 规范类技能，并用设计智能工具为 MC ERP 生成一份设计系统规范，作为后续开发的设计依据。

## 产品概述

MC ERP — 香港建筑管理公司 ERP 系统（.NET 10 后端 + Vue 3 + Element Plus 前端）。本次为开发前准备：技能装配 + 设计系统落档，不产生业务代码。

## 核心交付物

- 前端设计技能与后端规范技能安装到位（`~/.codebuddy/skills/`，含 SKILL.md 验证）
- `specs/design-system.md` — MC ERP 前端设计系统（配色、字体、布局、组件风格约定）
- `AGENTS.md` 文件地图补充 design-system.md 引用
- 命名规范复核确认（已存在，必要时微调措辞）

## 技术栈与执行方案

### 现状事实（已验证）

- `specs/architecture.md` 第 9-16 行已含命名规范表：基础设施（Common/ + Identity）英文命名；业务模块允许中文方法名/变量名，且注明“Identity 视为基础设施”。已提交（commit 4ac43d9），**无需重写，仅复核**。
- 项目栈：后端 .NET 10 + C# 14 Minimal API + EF Core + PostgreSQL + Serilog；前端 Vue 3 + TS + Element Plus（唯一 UI 库）+ Pinia + pnpm。
- 本地技能目录为空；SkillHub 搜索结果相关度低（均 < 0.05，噪声为主），优质技能来自 Vercel skills 源与 ClawHub 源。

### 技能选型（依据 find-skills 技能流程搜索所得）

**前端（设计好看）**：

| 技能 | 来源 | 安装方式 | 说明 |
| --- | --- | --- | --- |
| `anthropics/skills@frontend-design` | Vercel skills | `npx skills add anthropics/skills@frontend-design -g -y` 后复制/链接到 `~/.codebuddy/skills/` | Anthropic 官方设计技能，969.8K installs，指导有品位、不套模板的 UI |
| `element-plus-ui-vue3` | ClawHub | `npx clawhub install element-plus-ui-vue3 --workdir ~ --dir .codebuddy/skills` | Element Plus + Vue3 专用组件用法指导 |


**后端（设计规范）**：`codewithmukesh/dotnet-claude-kit` 与 specs 相容子集，`npx skills add codewithmukesh/dotnet-claude-kit@<name> -g -y`：

- `build-fix` — 构建错误修复流程
- `serilog` / `logging` — Serilog 日志规范（项目已用 Serilog，通过 DI 注入 ILogger\<T\>）
- `de-sloppify` — 代码清理去 sloppy
- `project-setup` — .NET 项目结构规范

**明确排除**（与 `specs/tech-stack.md` 禁令冲突，避免误导后续会话）：

- `opentelemetry` — specs 明令禁止
- `dotnet-onion-api` — Onion 架构违反“简单分层”原则
- `container-publish` — 与 Nginx 统一入口部署约定无关且超范围

### 关键决策

1. **技能 ≠ 项目依赖**：技能装在用户目录（`~/.codebuddy/skills/`），不进入 `package.json` / `.csproj`，不触碰 specs 禁区条款。
2. **设计系统落档而非口头约定**：用内置 ui-ux-pro-max 生成设计系统，写入 `specs/design-system.md`，与 tech-stack.md / architecture.md 并列，保证后续每个 AI 会话视觉一致；遵循“简单优先”，一份文件够用，不拆多文件。
3. **图标约定**：设计系统中明确“禁止 emoji 图标，统一用 SVG 图标（Lucide 系）”，与内置 lucide-icons 技能衔接。

### 执行注意事项

- 安装后逐一验证 `~/.codebuddy/skills/<skill>/SKILL.md` 存在；Vercel 源装到 `~/.agents/skills/`，需复制到 CodeBuddy 目录（Windows 无 symlink 权限时用 `Copy-Item -Recurse`）。
- 命名规范复核只读确认，若措辞已完整表达“业务中文/基础设施英文/Identity 属基础设施”三点则不做任何修改，避免无谓 churn。
- AGENTS.md 仅在文件地图表新增一行 design-system.md 引用，不改其他内容。

## 设计系统方向（MC ERP Web 端，桌面优先）

**风格定位**：现代企业级管理后台 — “专业工程感”。建筑管理行业气质：沉稳、清爽、高效，拒绝通用后台模板脸。整体浅色工作区 + 深色侧边栏的对比布局，数据密集页面（考勤报表、会计收据）保持高可读性。

- **布局**：左侧深色固定导航（四大模块 + 二级菜单）+ 顶部工具栏（面包屑、当前用户、通知）；内容区卡片化分区，间距统一 16/24px 栅格。
- **组件风格**：Element Plus 基础上定制主题变量 — 圆角 8px、卡片 1px 边框 + 轻投影；表格斑马纹、页签胶囊化；空状态用 SVG 插画而非文字堆砌。
- **交互**：按钮/卡片 hover 微上浮 + 阴影加深过渡 200ms；表单校验即时反馈；金额、日期等关键字段等宽数字字体对齐；危险操作（删除、月结）二次确认。
- **氛围**：主色为工程蓝（安全帽/工程图意象），辅以审计绿、预警琥珀，状态色语义化贯穿考勤、会计、文件模块。
- **图标**：全部使用 Lucide 线性 SVG 图标，24x24 基准，禁止 emoji。
- **微动效**：路由切换淡入、数字统计卡片计数动画，克制不炫技，符合“简单优先”。

设计系统最终以 ui-ux-pro-max 生成的 `specs/design-system.md` 为准，本描述为生成时的输入方向。

## Agent Extensions 使用计划

### Skill

- **find-skills**（内置）
- Purpose: 按其流程完成外部技能的发现与安装：Vercel 源 `npx skills add <owner/repo@skill> -g -y`、ClawHub 源 `npx clawhub install <slug> --workdir ~ --dir .codebuddy/skills`，并将 `~/.agents/skills/` 下技能复制到 `~/.codebuddy/skills/`，逐一验证 SKILL.md 存在。
- Expected outcome: frontend-design、element-plus-ui-vue3、dotnet-claude-kit 子集（build-fix / serilog / logging / de-sloppify / project-setup）全部在 `~/.codebuddy/skills/` 可用。

- **ui-ux-pro-max**（内置）
- Purpose: 以 "ERP construction management admin dashboard enterprise" 为关键词运行 `--design-system` 生成完整设计系统（风格、配色、字体、UX 准则）。
- Expected outcome: 产出落档为 `specs/design-system.md`，与项目"简单优先"原则匹配，作为后续所有前端页面开发的设计依据。

- **lsp-code-analysis**（内置）
- Purpose: 不适用本次任务（无代码修改），仅在复核 specs 命名规范措辞时无需使用。
- Expected outcome: 跳过。

> 说明：其余 MCP / SubAgent（CNB、code-explorer 等）与本准备任务无关，不使用。