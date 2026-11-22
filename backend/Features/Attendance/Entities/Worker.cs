using MC.Erp.Common.Domain;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 工人
/// </summary>
public sealed class Worker : AuditableEntity
{
    public string Name { get; set; } = "";        // 姓名
    public string? EmployeeId { get; set; }       // 工號（外部系統）
    public decimal DailyWage { get; set; }        // 日薪
    public bool IsActive { get; set; } = true;

    /// <summary>職位分類（AttendancePosition 字典），可為空；薪酬表按每月快照記錄，不靠這個欄位回看歷史。</summary>
    public Guid? PositionDictId { get; set; }

    /// <summary>職位分類的字典導航屬性，不新增資料庫欄位。</summary>
    public SysDict? Position { get; set; }

    public List<AttendanceRecord> Records { get; set; } = [];
    public List<MonthlyWorkSummary> Summaries { get; set; } = [];
}
