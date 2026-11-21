using MC.Erp.Common.Domain;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Accounting.Entities;

public sealed class Receipt : AuditableEntity
{
    /// <summary>票據原圖；手動錄入的票據可以先沒有原圖，之後再補上傳。</summary>
    public Guid? FileItemId { get; set; }
    public FileItem? FileItem { get; set; }
    public Guid DocumentTypeDictId { get; set; }
    public SysDict DocumentTypeDict { get; set; } = null!;

    /// <summary>
    /// 支出分類（Accounting 字典）。分類由票據持有，沒有原圖也可保存；
    /// 有原圖時同步寫入 FileItem.Type，供文件模組按分類展示。
    /// </summary>
    public Guid Type { get; set; }
    public SysDict TypeDict { get; set; } = null!;

    /// <summary>地盤分類（AccountingSiteCategory 字典），票據唯一的地盤歸屬，亦是導出 ZIP 的頂層目錄。</summary>
    public Guid? SiteCategoryDictId { get; set; }
    public SysDict? SiteCategoryDict { get; set; }
    public DateOnly? ReceiptDate { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? MerchantName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Remark { get; set; }
    public string OcrStatus { get; set; } = "NotStarted";
    public string? OcrError { get; set; }
    /// <summary>識別建議（票據欄位 + 置信度 + 待複核欄位）；不保存 Azure 原始結果，避免存 pages / words 等無用資料。</summary>
    public string? OcrResultJson { get; set; }
    public string? OcrText { get; set; }
    public DateTime? OcrStartedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}