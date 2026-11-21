using MC.Erp.Common.Domain;
using MC.Erp.Features.Storage.Entities;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 一次考勤卡掃描（一張卡）。欄位完全對應識別結果，不存工時、加班時數或合計等衍生值，
/// 那些在查詢時按明細即時計算。
/// </summary>
public sealed class AttendanceCardScan : AuditableEntity
{
    /// <summary>卡上手寫的年份，認不出來時為空。</summary>
    public int? Year { get; set; }
    /// <summary>年份的識別置信度（0–1）；手寫通常偏低。</summary>
    public float? YearConfidence { get; set; }

    /// <summary>卡上手寫的月份，認不出來時為空。</summary>
    public int? Month { get; set; }
    /// <summary>月份的識別置信度（0–1）。</summary>
    public float? MonthConfidence { get; set; }

    /// <summary>卡上手寫的姓名，最長 50 字。</summary>
    public string? Name { get; set; }
    /// <summary>姓名的識別置信度（0–1）。</summary>
    public float? NameConfidence { get; set; }

    /// <summary>
    /// 卡片層級的複核標記：year / month / name / layout。
    /// 存下來而不是即時推導，因為 layout（表頭漏檢、按順序填欄）推不回來。
    /// </summary>
    public List<string> ReviewFields { get; set; } = [];

    /// <summary>掃描使用的原圖，存 storage_files；用於回看對照，不重複存儲。</summary>
    public Guid? FileItemId { get; set; }
    public FileItem? FileItem { get; set; }

    /// <summary>逐日明細。</summary>
    public List<AttendanceCardPunch> Punches { get; set; } = [];
}
