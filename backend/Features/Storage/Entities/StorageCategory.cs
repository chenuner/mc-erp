using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Storage.Entities;

/// <summary>
/// 分類字典（地盤文件歸檔分類）
/// </summary>
public sealed class StorageCategory : AuditableEntity
{
    public string SiteName { get; set; } = null!;     // 哪個地盤
    public DateTime EventDate { get; set; }           // 時間
    public string EventReason { get; set; } = null!;  // 事件原因
    public string? Remark { get; set; }               // 備註

    public ICollection<FileItem> Files { get; set; } = null!;
}
