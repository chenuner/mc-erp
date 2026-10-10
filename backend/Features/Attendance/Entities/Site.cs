using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 地盤（施工現場）
/// </summary>
public sealed class Site : AuditableEntity
{
    public string Name { get; set; } = "";        // 地盤名稱
    public string? Address { get; set; }          // 地址
    public bool IsActive { get; set; } = true;

    public List<AttendanceRecord> Records { get; set; } = [];
}
