using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Repositories;
using Microsoft.Extensions.Logging;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 薪酬計算服務
/// </summary>
public sealed class PayrollService(
    IAttendanceRepository repo,
    ILogger<PayrollService> logger)
{
    // 香港 MPF 參數
    private const decimal Mpf費率 = 0.05m;          // 5%
    private const decimal Mpf最高入息 = 30000m;       // 月入息上限
    private const decimal Mpf最高供款 = 1500m;         // 每月最高供款

    /// <summary>
    /// 計算並生成月結工數表
    /// </summary>
    public async Task 計算月結Async(int year, int month, CancellationToken ct = default)
    {
        var workers = await repo.GetAllWorkersAsync(ct);
        logger.LogInformation("開始計算 {Year}年{Month}月 薪酬，共 {Count} 名工人", year, month, workers.Count);

        foreach (var worker in workers)
        {
            var summary = await 計算工人月結Async(worker, year, month, ct);
            await repo.UpsertSummaryAsync(summary, ct);
        }

        logger.LogInformation("月結計算完成");
    }

    /// <summary>
    /// 計算單個工人的月結
    /// </summary>
    private async Task<MonthlyWorkSummary> 計算工人月結Async(Worker worker, int year, int month, CancellationToken ct)
    {
        var 月初 = new DateTime(year, month, 1);
        var 月末 = 月初.AddMonths(1);

        var records = await repo.GetRecordsAsync(worker.Id, 月初, 月末, ct);

        var 工數 = records.Count(r => !r.IsLaborLeave);
        var 勞工假 = records.Count(r => r.IsLaborLeave);
        var 總工數 = 工數 + 勞工假;
        var OT小時 = records.Sum(r => r.OvertimeHours);
        var 日薪 = worker.DailyWage;

        var OT金額 = 計算加班費(日薪, OT小時);
        var (MPF僱主, MPF僱員) = 計算MPF(日薪, 總工數);
        var 實付人工 = (日薪 * 工數) + OT金額 - MPF僱員;
        var 總數 = (日薪 * 工數) + OT金額 + MPF僱主;

        return new MonthlyWorkSummary
        {
            WorkerId = worker.Id,
            Year = year,
            Month = month,
            WorkDays = 工數,
            LaborLeaveDays = 勞工假,
            TotalWorkDays = 總工數,
            OvertimeHours = OT小時,
            DailyWage = 日薪,
            OvertimeAmount = OT金額,
            MpfEmployer = MPF僱主,
            MpfEmployee = MPF僱員,
            ActualPay = 實付人工,
            TotalAmount = 總數
        };
    }

    /// <summary>
    /// 計算加班費：時薪 × 1.5 × OT時數
    /// </summary>
    private decimal 計算加班費(decimal 日薪, decimal OT小時)
    {
        var 時薪 = 日薪 / 8m;  // 假設每天 8 小時
        return 時薪 * 1.5m * OT小數(OT小時);
    }

    private decimal OT小數(decimal v) => v;

    /// <summary>
    /// 計算 MPF（僱主 + 僱員）
    /// </summary>
    private (decimal 僱主, decimal 僱員) 計算MPF(decimal 日薪, decimal 總工數)
    {
        var 月入息 = 日薪 * 總工數;

        // 超過上限按上限計算
        if (月入息 > Mpf最高入息)
            月入息 = Mpf最高入息;

        var 供款 = 月入息 * Mpf費率;
        if (供款 > Mpf最高供款)
            供款 = Mpf最高供款;

        return (供款, 供款);
    }
}
