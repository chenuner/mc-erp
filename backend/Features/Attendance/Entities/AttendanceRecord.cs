using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 打卡記錄（明細）
/// </summary>
public sealed class AttendanceRecord : Entity
{
    /// <summary>員工 ID。</summary>
    public Guid WorkerId { get; set; }
    /// <summary>員工資料的導航屬性。</summary>
    public Worker Worker { get; set; } = null!;

    /// <summary>地盤 ID。</summary>
    public Guid SiteId { get; set; }
    /// <summary>地盤資料的導航屬性。</summary>
    public Site Site { get; set; } = null!;

    /// <summary>上班打卡時間，資料庫使用 UTC，前端按香港時間展示。</summary>
    public DateTime ClockInTime { get; set; }
    /// <summary>下班打卡時間（UTC）；未打卡或勞工假時可為空。</summary>
    public DateTime? ClockOutTime { get; set; }
    /// <summary>加班時數，支持兩位小數。</summary>
    public decimal OvertimeHours { get; set; }
    /// <summary>該記錄是否為勞工假。</summary>
    public bool IsLaborLeave { get; set; }
    /// <summary>注意事項，最多 255 字元。</summary>
    public string? Remark { get; set; }
}
