using MC.Erp.Common.Domain;

namespace MC.Erp.Features.System.Entities;

/// <summary>
/// 歸檔分類字典（系統級共享）。
/// 以「地盤 / 時間 / 事件原因」為歸檔維度，地盤文件、會計收據等模組均可引用。
/// </summary>
public sealed class ArchiveCategory : AuditableEntity
{
    public string SiteName { get; set; } = null!;     // 哪個地盤
    public DateTime EventDate { get; set; }           // 時間
    public string EventReason { get; set; } = null!;  // 事件原因
    public string? Remark { get; set; }               // 備註
}
