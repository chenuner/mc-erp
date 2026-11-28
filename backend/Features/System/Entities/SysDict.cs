using MC.Erp.Common.Domain;

namespace MC.Erp.Features.System.Entities;

/// <summary>
/// 系统字典实体 - 用于存储系统级别的字典数据。
/// </summary>
public sealed class SysDict : AuditableEntity
{
    /// <summary>
    /// 字典分组名称（VARCHAR(50)）。
    /// </summary>
    public string GroupName { get; set; } = null!;

    /// <summary>
    /// 标签/显示名称（VARCHAR(100)）。
    /// </summary>
    public string Label { get; set; } = null!;

    /// <summary>
    /// 字典值（VARCHAR(50)）。
    /// </summary>
    public string Value { get; set; } = null!;

    /// <summary>
    /// 上級字典項 ID（NULL 表示頂層）。同一分組內的多層結構，例如地盤「牛房」下的「一樓」「二樓」。
    /// 只建導覽，資料庫不建外鍵。
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 上級字典項（僅為查詢方便，不靠資料庫約束）。
    /// </summary>
    public SysDict? Parent { get; set; }

    /// <summary>
    /// 下級字典項。
    /// </summary>
    public List<SysDict> Children { get; set; } = [];

    /// <summary>
    /// 排序序号（INT，默认0）。
    /// </summary>
    public int Sort { get; set; } = 0;

    /// <summary>
    /// 是否启用（BOOLEAN，默认true）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 备注（VARCHAR(255)）。
    /// </summary>
    public string? Remark { get; set; }
}
