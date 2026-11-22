using MC.Erp.Common.Domain;
using MC.Erp.Features.System.Entities;

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

    /// <summary>地盤（系統字典 AccountingSiteCategory 的字典項 ID）。</summary>
    public Guid SiteDictId { get; set; }
    /// <summary>地盤字典項的導航屬性，不新增資料庫欄位。</summary>
    public SysDict Site { get; set; } = null!;

    /// <summary>
    /// 當天的職位分類（AttendancePosition 字典項）；同步考勤卡時取員工當下的職位。
    /// 職位和地盤一起決定薪酬表的分組行，所以必須記在明細上而不是只留在員工身上。
    /// </summary>
    public Guid? PositionDictId { get; set; }
    /// <summary>職位分類的字典導航屬性，不新增資料庫欄位。</summary>
    public SysDict? Position { get; set; }

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
