using MC.Erp.Common.Domain;

namespace MC.Erp.Features.System.Entities;

/// <summary>
/// 字典分組 - 字典項的容器。Code 與 SysDict.GroupName 對應，建立後不可修改。
/// </summary>
public sealed class SysDictGroup : AuditableEntity
{
    /// <summary>
    /// 分組代碼（VARCHAR(50)）。程式以它引用分組，例如 Accounting。
    /// </summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// 分組顯示名稱（VARCHAR(100)）。
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 排序序號（INT，默认0）。
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// 說明（VARCHAR(255)）。
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 是否為程式內建分組。內建分組不可刪除，其字典項識別碼不可修改。
    /// </summary>
    public bool IsSystem { get; set; }
}
