# 文件存儲（Storage）

> **管什麼**：文件元數據與物理文件的分離、MD5 去重、刪除語義、兩條下載路徑。
> **何時讀**：動 `Features/Storage`、上傳 / 下載 / 刪除、文件類別篩選時。
> **改到這裡要同步**：`specs/status.md` 的 Storage 條目。

---

- `IFileStorage` 接口抽象文件讀寫，定義在 `Storage/Services/`
- MVP 階段：`LocalStorageProvider` 實現，文件存本地磁盤
- 未來：換 `S3StorageProvider` / `OssStorageProvider` 只需改 DI 註冊，業務代碼不動
- 文件元數據（名稱、路徑、分類、權限）存數據庫，實際文件內容走 `IFileStorage`
- 上傳時流式計算 MD5，資料庫 `storage_files.Md5` 保存 32 位小寫十六進制摘要，不保存文件內容。歷史文件的 MD5 可為空，磁盤存儲名用 Guid + 原擴展名。
- **按 MD5 去重**：同內容在磁盤上只存一份。上傳時若已有同 MD5 且未刪除的記錄，就丟掉剛寫的那份、共用既有的 `StoredName`；每筆記錄仍各有一行（原名、類別、上傳人、時間各記各的），所以同一張圖可以一次當票據原圖、一次當考勤卡。MD5 為空的歷史資料不參與去重。
- 刪除是軟刪除，且**只有再沒有任何未刪除記錄指向同一個 `StoredName` 時才刪磁盤文件**——去重後一個物理文件可能被多筆記錄共用，少做這一步就會出現「刪了 A，B 的圖也不見了」。
- 下載有兩條路，職責不同：
  - `GET /api/storage/files/{id}/download` —— 按記錄取，**元數據（檔名、ContentType）一定屬於這筆記錄**；
  - `GET /api/storage/files/by-md5/{md5}/download` —— 內容尋址，去重後同一摘要的物理文件唯一，回最早上傳那一筆的元數據。
  - 前端預覽**按 id 取圖、按 MD5 做快取鍵**：同內容只下載一份、只快取一份，同時檔名和 ContentType 永遠正確。
- 文件類別引用系統字典（`FileItem.Type`），但只認**白名單分組** `FileService.FileTypeGroups`（支出分類、票據類型、地盤分類、考勤卡圖片）。其他分組（職位分類、驗證分組）不能當文件類別，上傳會被拒——新增一類文件就在白名單加一個分組代碼。
- 類別選項由 `GET /api/storage/types` 提供（白名單分組 + 其字典項，含停用項），前端不再拉整份字典。
- 列表篩選：`type=<字典項 id>` 連同整棵子樹（子樹按 `ParentId` 走）；`type=group:<分組代碼>` 代表整個分組（分組在另一張表，沒有字典項 id）；不傳即全部。層級與停用規則見 [system-dict.md](system-dict.md)。
- 權限：目前只有 Admin / 非 Admin 粗粒度；會計分組的圖片不能經通用 Storage 接口繞過權限或刪除（`AccountingFileProtectionService` 擋住 Storage→Accounting 的直接依賴）。`FilePermission` 尚未實現。
