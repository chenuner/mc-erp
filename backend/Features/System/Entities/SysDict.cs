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
