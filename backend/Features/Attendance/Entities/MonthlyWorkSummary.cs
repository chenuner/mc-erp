using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Attendance.Entities;

/// <summary>
/// 月結工數表
/// </summary>
public sealed class MonthlyWorkSummary : Entity
{
    public Guid WorkerId { get; set; }
    public Worker Worker { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }

    public decimal WorkDays { get; set; }           // 工數
    public decimal LaborLeaveDays { get; set; }       // 勞工假
    public decimal TotalWorkDays { get; set; }       // 總工數
    public decimal OvertimeHours { get; set; }        // O.T.(小時)
    public decimal DailyWage { get; set; }            // 日薪
    public decimal OvertimeAmount { get; set; }      // OT金額
    public decimal MpfEmployer { get; set; }          // MPF（僱主）
    public decimal MpfEmployee { get; set; }           // MPF(僱員)
    public decimal ActualPay { get; set; }            // 實付人工
    public decimal TotalAmount { get; set; }           // 總數
    public string? Remark { get; set; }               // 注意！
}
