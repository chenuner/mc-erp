using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 考勤卡單日明細，對應掃描結果的一列。時間只存打卡鐘印出來的原始時刻（time），
/// 工時、加班時數不落庫——計薪規則未確認，查詢時再按上下班時間算。
/// </summary>
public sealed class AttendanceCardPunch : Entity
{
    /// <summary>所屬的掃描記錄。</summary>
    public Guid ScanId { get; set; }
    public AttendanceCardScan Scan { get; set; } = null!;

    /// <summary>卡上的日（1–31），不是完整日期；年月記在掃描記錄上。</summary>
    public int Day { get; set; }

    /// <summary>上午上班。</summary>
    public TimeOnly? AmIn { get; set; }
    /// <summary>上午上班的識別置信度（0–1）；沒有值時為空。</summary>
    public float? AmInConfidence { get; set; }

    /// <summary>上午下班。</summary>
    public TimeOnly? AmOut { get; set; }
    public float? AmOutConfidence { get; set; }

    /// <summary>下午上班。</summary>
    public TimeOnly? PmIn { get; set; }
    public float? PmInConfidence { get; set; }

    /// <summary>下午下班。</summary>
    public TimeOnly? PmOut { get; set; }
    public float? PmOutConfidence { get; set; }

    /// <summary>加班上班。</summary>
    public TimeOnly? OtIn { get; set; }
    public float? OtInConfidence { get; set; }

    /// <summary>加班下班。</summary>
    public TimeOnly? OtOut { get; set; }
    public float? OtOutConfidence { get; set; }
}
