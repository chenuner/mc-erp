using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 月結工數表
/// </summary>
public sealed class MonthlyWorkSummary : Entity
{
    /// <summary>員工 ID，姓名通過 Worker 導航屬性讀取。</summary>
    public Guid WorkerId { get; set; }
    /// <summary>員工資料，不是獨立的資料庫欄位。</summary>
    public Worker Worker { get; set; } = null!;

    /// <summary>薪酬所屬年份。</summary>
    public int Year { get; set; }
    /// <summary>薪酬所屬月份（1 至 12）。</summary>
    public int Month { get; set; }

    /// <summary>工數，支持小數。</summary>
    public decimal WorkDays { get; set; }
    /// <summary>勞工假天數，支持小數。</summary>
    public decimal LaborLeaveDays { get; set; }
    /// <summary>總工數；現階段由人工維護，不自動計算。</summary>
    public decimal TotalWorkDays { get; set; }
    /// <summary>O.T. 加班小時數。</summary>
    public decimal OvertimeHours { get; set; }
    /// <summary>日薪，港元。</summary>
    public decimal DailyWage { get; set; }
    /// <summary>OT 金額，港元。</summary>
    public decimal OvertimeAmount { get; set; }
    /// <summary>MPF 僱主供款，港元。</summary>
    public decimal MpfEmployer { get; set; }
    /// <summary>MPF 僱員供款，港元。</summary>
    public decimal MpfEmployee { get; set; }
    /// <summary>實付人工，港元。</summary>
    public decimal ActualPay { get; set; }
    /// <summary>總數，港元。</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>注意事項，可變長度，最多 255 字元。</summary>
    public string? Remark { get; set; }
}
