using MC.Erp.Common.Domain;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Accounting.Entities;

public sealed class Receipt : AuditableEntity
{
    public Guid FileItemId { get; set; }
    public FileItem FileItem { get; set; } = null!;
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public Guid DocumentTypeDictId { get; set; }
    public SysDict DocumentTypeDict { get; set; } = null!;
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