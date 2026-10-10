using MC.Erp.Common.Domain;

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

    public List<AttendanceRecord> Records { get; set; } = [];
    public List<MonthlyWorkSummary> Summaries { get; set; } = [];
}
