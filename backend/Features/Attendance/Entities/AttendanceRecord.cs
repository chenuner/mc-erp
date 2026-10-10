using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 打卡記錄（明細）
/// </summary>
public sealed class AttendanceRecord : Entity
{
    public Guid WorkerId { get; set; }
    public Worker Worker { get; set; } = null!;

    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public DateTime ClockInTime { get; set; }    // 上班打卡
    public DateTime? ClockOutTime { get; set; }   // 下班打卡
    public decimal OvertimeHours { get; set; }     // 加班時數
    public bool IsLaborLeave { get; set; }         // 勞工假
    public string? Remark { get; set; }
}
