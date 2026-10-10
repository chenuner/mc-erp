# MC ERP

建築管理公司 ERP 系統。面向香港建築行業，涵蓋打卡考勤、薪酬計算、會計記賬、文件管理。

## 功能模組

| 模組 | 說明 |
|------|------|
| **Identity** | 用戶、角色、權限、JWT 認證 |
| **Attendance** | 外部打卡數據拉取、工數統計、薪酬計算（MPF/OT）、月結報表 |
| **Accounting** | 收據拍照上傳、OCR 識別、支出按地盤分類 |
| **Storage** | 雲盤文件管理、地盤照片分類存儲、賬戶權限 |

## 項目結構

```
mc-erp/
├── AGENTS.md              # AI 編程行為規範
├── specs/                 # 技術規範（唯一真理源）
│   ├── README.md          # specs 導航
│   ├── tech-stack.md      # 技術棧規範
│   └── architecture.md    # 架構規範
├── backend/               # ASP.NET Core 後端
│   ├── Common/            # 公共基礎設施
│   ├── Features/          # 業務模組
│   │   ├── Identity/      # 用戶/角色/認證
│   │   ├── Attendance/    # 考勤/薪酬
│   │   ├── Accounting/    # 會計/收據
│   │   └── Storage/       # 文件/照片
│   ├── Data/              # DbContext
│   └── MC.Erp.csproj
├── frontend/              # Vue 3 前端
│   └── src/
│       ├── api/           # API 接口
│       ├── components/    # 通用組件
│       ├── views/         # 頁面
│       ├── stores/        # Pinia 狀態
│       └── router/        # 路由
├── docker-compose.yml     # 本地開發棧
├── .gitignore
└── README.md
```

## 技術棧

### 後端
- **.NET 10 LTS** — ASP.NET Core Minimal API
- **PostgreSQL 16+** — 主數據庫
- **EF Core 10** — ORM，Code First
- **JWT** — 認證，Argon2id 密碼哈希
- **Hangfire** — 定時拉取外部考勤數據
- **Serilog** — 日誌（Console + File）
- **Scalar.AspNetCore** — API 文檔

### 前端
- **Vue 3 + TypeScript** — 框架
- **Vite** — 構建工具
- **Pinia** — 狀態管理
- **Element Plus** — UI 組件庫
- **Axios** — HTTP 客戶端

完整技術棧規範見 [specs/tech-stack.md](specs/tech-stack.md)。

## 快速開始

### 環境要求
- .NET 10 SDK
- Node.js >= 20 + pnpm
- PostgreSQL 16+
- Docker（可選，用於本地 PostgreSQL）

### 啟動後端

```bash
cd backend
dotnet restore
dotnet run
```

後端服務運行在 `https://localhost:5001`，API 文檔在 `/scalar`。

### 啟動前端

```bash
cd frontend
pnpm install
pnpm run dev
```

前端運行在 `http://localhost:3000`。

### 部署

- Nginx 作為統一入口，轉發前端靜態文件和後端 API
- 後端端口不對外暴露

## 開發

### 添加新功能

1. 閱讀 [specs/architecture.md](specs/architecture.md) 了解模組結構
2. 在 `backend/Features/{ModuleName}/` 下按 Endpoints / Services / Entities / Repositories 分層
3. 在 `ErpDbContext` 中註冊實體
4. 在 `Program.cs` 中註冊 DI（或用 Scrutor 自動掃描）

### 數據庫遷移

```bash
cd backend
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## License

MIT
