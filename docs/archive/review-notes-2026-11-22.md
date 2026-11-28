# docs/archive/review-notes-2026-11-22.md — 那一輪 review 的未結項

> 完整原文（含已修項目、已過時的判斷）已於 2026-11-28 清理時刪除，不再保留。
> 這裡只留**原樣未動、今天仍然存在**的欠賬，審查時照這張表看。

| 編號 | 內容 | 現狀 | 依據 |
|------|------|------|------|
| L8 | `SeedData` 每次啟動都覆寫系統分組的名稱與排序 | 行為未改，已記入 `status.md` | 原 D 段外的 SeedData 段 |
| L9 | 首次登入用 `db.Users.AnyAsync()`，受 `HasQueryFilter(!IsDeleted)` 影響：管理員被軟刪後重啟會再建一份 `admin`，撞 `Role` 唯一索引導致啟動失敗 | 未修，仍未驗證 `UserService.DeleteAsync` 是否軟刪 | 原 L9 節 |
| S1 / S2 | `appsettings.Development.json` 明文 Azure key；`.gitignore` 沒排除 `appsettings*.json` | 依維護者指示保留，key 待自行輪換 | 原 S1/S2 節 |
| L7 | 非 Production 自動建 `admin / Admin@123` | 依維護者指示保留，已記為金科玉律 7 的已知例外 | 原 L7 節 |
| S3 | Storage 上傳類別沒有白名單 | 待產品決定（判斷後認為「地盤照片按地盤分類」是正當用途） | 原 S3 節 |
| D8 | `Permission` / `RolePermission` 已建模但未接線 | 已記入 `status.md` | 原 D8 節 |
| D4 | `AccountingFileProtectionService` 是 1 行轉發 | **保留**：擋住 Storage → Accounting Repo 的直接依賴 | 原 D4 節 |

已確認處理完的項目（S4、S5、L1–L6、D1、D2、D3、D5、D6、D7、P1–P3）不再列。
