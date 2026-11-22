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
│   ├── status.md          # 當前實現狀態
│   ├── tech-stack.md      # 技術棧規範
│   ├── architecture.md    # 架構規範
│   └── design-system.md   # 前端設計系統
├── backend/               # ASP.NET Core 後端
│   ├── Common/            # 公共基礎設施
│   ├── Features/          # 業務模組
│   │   ├── Identity/      # 用戶/角色/認證
│   │   ├── Attendance/    # 考勤/薪酬
│   │   ├── Accounting/    # 會計/收據
│   │   └── Storage/       # 文件/照片
│   ├── Data/              # DbContext
│   ├── Dockerfile          # 打包後端發布產物
│   ├── MC.Erp.slnx        # 唯一 .NET 解決方案入口
│   └── MC.Erp.csproj
├── frontend/              # Vue 3 前端
│   ├── Dockerfile          # 打包前端構建產物
│   ├── nginx.conf          # 靜態文件及 API 代理
│   └── src/
│       ├── api/           # API 接口
│       ├── components/    # 通用組件
│       ├── views/         # 頁面
│       ├── stores/        # Pinia 狀態
│       └── router/        # 路由
├── tests/                 # 後端檢查程序
├── deploy/                # 初始化、部署腳本及操作說明
├── scripts/               # 本地驗證腳本
│   └── check.ps1          # 一鍵 build + Checks + 前端建置
├── docker-compose.yml     # 本地開發資料庫
├── docker-compose.prod.yml # 生產前端、後端及資料庫
├── global.json            # .NET SDK 版本
├── Directory.Build.props  # 全倉編譯設定
├── .editorconfig          # 格式約定
├── .gitignore
└── README.md
```

## 技術棧

### 後端
- **.NET 10 LTS** — ASP.NET Core Minimal API
- **PostgreSQL 16+** — 主數據庫
- **EF Core 10** — ORM，Code First
- **JWT** — 認證，Argon2id 密碼哈希
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

### 本地資料庫

在倉庫根目錄執行：

```bash
docker compose up -d postgres
```

開發資料庫使用 `localhost:5433`，資料庫名 `mc_erp`，用戶及密碼均為 `postgres`，與後端開發配置一致。前後端在本機執行，生產環境使用獨立的 `docker-compose.prod.yml`，不要混用。

### 啟動後端

```bash
cd backend
dotnet restore
dotnet run
```

後端服務運行在 `http://localhost:5209`（`https` profile 另開 `7224`），API 文檔在 `/scalar`。

### 啟動前端

```bash
cd frontend
pnpm install
pnpm run dev
```

前端運行在 `http://localhost:5173`。

### 部署

- Nginx 作為統一入口，轉發前端靜態文件和後端 API
- 後端端口不對外暴露

## 開發

### 添加新功能

1. 閱讀 [specs/architecture.md](specs/architecture.md) 了解模組結構
2. 在 `backend/Features/{ModuleName}/` 下按 Endpoints / Services / Entities / Repositories 分層
3. 在 `ErpDbContext` 中註冊實體
4. 在 `Program.cs` 中註冊 DI（本項目手動註冊，未用 Scrutor 掃描）
5. 跑 `pwsh -File scripts/check.ps1` 驗證

### 數據庫遷移

```bash
cd backend
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## 會計票據

- `/accounting` 僅管理員可用。上傳 JPEG / PNG（每張最多 4 MB）後保存原圖；按「OCR 識別」才會讀取圖中資料，失敗可重試或手動填寫。
- Azure 官方 SDK 1.0.0 / API 2024-11-30，支援 `prebuilt-receipt` 及 `prebuilt-invoice`。發票明確支援香港繁體中文；中文收據必須用實際樣本測試。票據會發送到 Azure，部署前確認服務地域、資料處理要求及費用。
- 識別憑據只從環境變量 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY` 讀取；本機用 `dotnet user-secrets set AZURE_DOC_ENDPOINT ...` 存同名鍵（backend 項目已配置 `UserSecretsId`），生產 / CI 由環境變量注入。User Secrets 僅供本機開發，文件未加密；生產環境用受控 Secret Store 或伺服器 `.env`。不得將密鑰提交到 Git、傳到前端或貼到聊天。未配置時仍可手動錄入；識別超時 60 秒（代碼內限制 10–120 秒）。
- 每個識別欄位都帶 Azure 回報的置信度（0–1）。金額、商戶、票據日期、票據號碼低於 0.80 時，前端標記「需人工複核」。手持拍攝、有褶皺反光、含手寫金額的照片識別率有限，預建模型結果只作建議，不能當成已核對。
- 系統字典自動初始化 `AccountingDocumentType`（receipt / invoice）及 `Accounting` 支出分類。管理員須在系統字典的「地盤分類」分組（代碼 `AccountingSiteCategory`）新增實際地盤分類；停用分類不影響歷史查詢，已引用分類不能刪除。
- 地盤只走系統字典的「地盤分類」分組（代碼 `AccountingSiteCategory`），考勤和會計票據共用同一份名單，兩邊不再各自維護；考勤卡同步時選的也是這裡的字典項。保存前須填寫票據類型、支出分類及地盤分類；日期、金額、幣種可以稍後補，有填才校驗（金額為正數、幣種三位英文字母）。前端只有一個「保存」按鈕。OCR 只在按「OCR 識別」時執行，結果只作建議（只填空白欄位），不會自動改寫票據。
- 金額採用票據本次總額（收據 Total / 發票 InvoiceTotal），不將含舊欠款的 AmountDue 當支出，也不自動判定發票已付款。缺失金額、日期及幣種保持空值。
- 提取圖片沿用查詢日期、票據類型、支出分類及地盤分類。下載 ZIP 包含全部匹配圖片，不限當前頁；按地盤分類 / 支出分類分文件夾，上限 5000 張 / 250 MB。缺圖時整包報錯，不靜默漏圖。MD5 僅作一致性核對，文件按 ID 讀取，不按 MD5 去重。
- 遷移隨後端啟動自動執行。重啟後端後，在前端 `http://localhost:5173/accounting` 操作；現有 Vite 代理指向本機後端 5209。
- 備份須同時包含 PostgreSQL 資料庫及 Storage 原圖目錄。通用 Storage 不能刪除已關聯票據的原圖。

本機配置識別憑據（只寫入 User Secrets，不進倉庫）：

```bash
cd backend
dotnet user-secrets set "AZURE_DOC_ENDPOINT" "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AZURE_DOC_KEY" "<key>"
```

驗證識別映射與分類規則（不呼叫 Azure、不需要新增測試框架）：

```bash
dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release
```

## GitHub Actions

工作流位於 [.github/workflows/ci.yml](.github/workflows/ci.yml)，在 Pull Request 及手動觸發（**Actions → CI → Run workflow**）時執行，只負責建置與打包，不跑檢查。

- 後端：使用 .NET 10 進行 Release 構建，上傳發布產物。
- 前端：使用 Node.js 24、pnpm（版本見 `package.json` 的 `packageManager`），按鎖文件安裝依賴，執行 Vite 生產構建。
- 在默認分支手動觸發且建置成功後，GitHub runner 構建前後端鏡像並 `docker save`，透過 SSH 上傳後由伺服器 `docker load` 再啟動前端、後端及獨立的正式 PostgreSQL；PR 不部署。

本地驗證以 `scripts/check.ps1` 為準（建置 + Identity / Accounting Checks + 前端型別與建置）。首次伺服器初始化命令、GitHub Secrets / Variables 清單及登入方法見 [deploy/README.md](deploy/README.md)。

## License

MIT
