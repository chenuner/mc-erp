using MC.Erp.Common.Domain;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Accounting.Entities;

public sealed class Receipt : AuditableEntity
{
    /// <summary>票據原圖；手動錄入的票據可以先沒有原圖，之後再補上傳。</summary>
    public Guid? FileItemId { get; set; }
    public FileItem? FileItem { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public Guid DocumentTypeDictId { get; set; }
    public SysDict DocumentTypeDict { get; set; } = null!;

    /// <summary>
    /// 支出分類（Accounting 字典）。分類由票據持有，沒有原圖也可保存；
    /// 有原圖時同步寫入 FileItem.Type，供文件模組按分類展示。
    /// </summary>
    public Guid Type { get; set; }
    public SysDict TypeDict { get; set; } = null!;
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
    public string? OcrResultJson { get; set; }
    public string? OcrText { get; set; }
    public DateTime? OcrStartedAt { get; set; }
    public bool ManuallyEdited { get; set; }
    public bool IsConfirmed { get; set; }
    public Guid? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}