# 會計收據（Accounting）

> **管什麼**：票據欄位與必填規則、原圖裁剪上傳、OCR 識別、分類歸屬、ZIP 提取。
> **何時讀**：動 `Features/Accounting`、`ImageCropDialog`、票據識別或匯出時。
> **改到這裡要同步**：`tests/Accounting.Checks`（含整合檢查）、`specs/status.md` 的 Accounting 條目。

---

## 票據與原圖

- 票據可直接手動錄入（不必先有原圖），原圖可稍後補上或更換。前端只有一個「保存」動作，保存後即完成，沒有草稿狀態；必填為票據類型、支出分類（不能是 pending）及地盤分類。OCR 只在使用者按「OCR 識別」時執行，回傳建議欄位，由使用者決定是否填入（只填空白欄位），不直接改寫票據資料。
- **原圖一律先裁剪再上傳**：拍照 / 選圖後打開 `components/ImageCropDialog.vue`，拉框裁掉桌面、背景和手指，確認後才上傳；已保存的原圖可再按「裁剪原圖」下載回來重裁（等於更換原圖，會清掉舊的識別結果）。裁剪用原生 Canvas（`utils/cropImage.ts`），**不引入裁剪庫**，`specs/tech-stack.md` 沒有列的不裝。
  - 解碼時按 EXIF 轉正（手機直拍不會躺倒），另備「旋轉 90°」按鈕；支援自由 / 1:1 / 4:3 / 3:4 / 16:9 比例，鎖比例時以拖動位移推另一邊。
  - 預覽是縮到 1280px 的縮圖（拖框流暢），落盤才按原圖像素裁切，所以裁剪不會降低解析度。
  - 輸出統一 JPEG（先填白底，PNG 透明不會變黑），逐級降質、必要時縮尺寸，保證 ≤ 4 MB（後端 `ReceiptService.CheckImageAsync` 的上限）。選圖端放寬到 10 MB，因為大圖裁剪後會縮下來。
- `Receipt.SiteCategoryDictId` 引用 `AccountingSiteCategory` 字典（顯示名「地盤分類」），是票據唯一的地盤歸屬，同時決定導出 ZIP 的頂層目錄；考勤的打卡明細也用同一份名單（`AttendanceRecord.SiteDictId`），**會計與考勤共用一個地盤分組**，不再各自維護。
- `DocumentTypeDictId` 引用 `AccountingDocumentType`（receipt / invoice）。支出分類由 `Receipt.Type` 持有（`Accounting` 字典），沒有原圖也可保存；有原圖時同步寫入 `FileItem.Type`，供文件模組按分類展示。
- 金額 decimal(18,2)，票據日期 DateOnly；發票金額取 InvoiceTotal，不取可能含歷史欠款的 AmountDue，也不自動視為已付款。
- 分類的停用與層級規則（上級停用→下級不可選、篩選含下級）見 [system-dict.md](system-dict.md)。

---

## OCR 識別

- `IOcrService` 位於 Common/Infrastructure/Ocr，使用官方 C# SDK `Azure.AI.DocumentIntelligence`；客戶端由 `DocumentIntelligenceClientProvider` 共用（考勤卡掃描用同一個 client）；憑據只從環境變量 `AZURE_DOC_ENDPOINT` / `AZURE_DOC_KEY` 讀取，代碼和 appsettings 不出現密鑰；收據中文識別須實測，不能保證準確率。
- 有原圖後才可識別。使用 Azure Document Intelligence `prebuilt-receipt`（收據 / 現沽單）及 `prebuilt-invoice`（發票按發票類型錄入）；只映射票據表需要的欄位（商戶 / 供應商、日期、金額、幣種、票據號碼），每個欄位帶 Azure 回報的置信度（0–1）；未識別的日期、金額及幣種保持空值。
- 置信度低於 0.80，或識別到欄位卻讀不出值（例如只認到貨幣符號 `$`、沒有金額）的欄位，標記為需人工複核，由前端提示使用者對照原圖核對。手持拍攝、褶皺反光、含手寫金額的單據識別率有限，識別結果不得當作已核對。
- 金額缺失時，從識別文字裡找「合計 / 總計 / TOTAL」等標籤後面的金額兜底：同一行優先，其次下一行的整行純數字；固定給 0.5 置信度並標記人工複核。只認明確的總額字眼，避免誤抓日期、電話或相機浮水印。
- 只保存及回傳票據欄位（商戶、日期、金額、幣種、票據號碼）與置信度；Azure 原始結果（pages / words / polygon）不入庫、不回前端，`OcrResultJson` 存的是識別建議本身。
- 僅 Admin 操作；會計圖片不能經通用 Storage 接口繞過權限或刪除。票據可經會計接口刪除（原圖一併清理），刪除不可復原。識別失敗保留原圖並可重試，識別結果不會覆寫票據欄位。

---

## ZIP 提取

- 按票據日期（含起止日）、票據類型、支出分類及地盤分類提取全部匹配原圖為 ZIP，不限當前頁，不輸出 CSV；沒有原圖的票據不進 ZIP。
- **列表篩選一律含下級**（不加開關）；ZIP 提取另給「包含下級分類」開關（默認開，`includeDescendants=false` 才只取該分類本身）。子樹展開規則見 [system-dict.md](system-dict.md)。
- ZIP 文件夾用字典完整路徑（`沙田地盤 / 一樓 / 支出分類`），多層字典的同名下級不會擠進同一個文件夾；字典項找不到時退回票據上保存的名稱快照。MD5 僅作核對，不是文件地址或唯一鍵。
- ZIP 使用臨時文件，預設上限 5000 張 / 250 MB 原圖，完成或失敗後清理；第一版單圖上限 4 MB，與 Azure F0 相容。
