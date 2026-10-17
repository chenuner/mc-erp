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

## 會計票據

- `/accounting` 僅管理員可用。上傳 JPEG / PNG（每張最多 4 MB）後保存原圖及草稿；已配置 Azure 時自動識別，失敗可重試或手動填寫。
- Azure 官方 SDK 1.0.0 / API 2024-11-30，支援 `prebuilt-receipt` 及 `prebuilt-invoice`。發票明確支援香港繁體中文；中文收據必須用實際樣本測試。票據會發送到 Azure，部署前確認服務地域、資料處理要求及費用。
- 後端使用環境變量 `Ocr__Endpoint` / `Ocr__Key`（或本機 User Secrets 的 `Ocr:Endpoint` / `Ocr:Key`）。不得將密鑰提交到 Git、傳到前端或貼到聊天。未配置時仍可手動錄入；`Ocr__TimeoutSeconds` 預設 60 秒，範圍 10–120 秒。
- 系統字典自動初始化 `AccountingDocumentType`（receipt / invoice）及 `Accounting` 支出分類。管理員須在系統字典新增 `AccountingSiteCategory` 分組的實際導出分類；停用分類不影響歷史查詢，已引用分類不能刪除。
- 實際支出地盤來自考勤 `Site`，導出地盤分類來自系統字典，兩者獨立。确认前須填寫日期、正數金額、幣種、支出及導出分類；修改已確認資料會回到待確認。重新識別只更新識別結果，不覆寫人工保存的資料。
- 金額採用票據本次總額（收據 Total / 發票 InvoiceTotal），不將含舊欠款的 AmountDue 當支出，也不自動判定發票已付款。缺失金額、日期及幣種保持空值。
- 提取圖片沿用查詢日期、票據類型、支出分類、實際地盤及確認狀態，另外選擇導出地盤分類。下載 ZIP 包含全部匹配圖片，不限當前頁；按導出分類 / 支出分類分文件夾，上限 5000 張 / 250 MB。缺圖時整包報錯，不靜默漏圖。MD5 僅作一致性核對，文件按 ID 讀取，不按 MD5 去重。
- 遷移隨後端啟動自動執行。重啟後端後，在前端 `http://localhost:5173/accounting` 操作；現有 Vite 代理指向本機後端 5209。
- 備份須同時包含 PostgreSQL 資料庫及 Storage 原圖目錄。通用 Storage 不能刪除已關聯票據的原圖。

驗證識別映射與分類規則（不呼叫 Azure、不需要新增測試框架）：

```bash
dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release
```

## License

MIT
