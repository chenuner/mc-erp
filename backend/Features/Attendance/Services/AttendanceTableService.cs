using MC.Erp.Data;
using MC.Erp.Features.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>薪酬表合計，按整份篩選結果計算；日薪是每人單價，不參與合計。</summary>
public sealed record PayrollTotals(decimal WorkDays, decimal LaborLeaveDays, decimal TotalWorkDays,
    decimal OvertimeHours, decimal OvertimeAmount, decimal MpfEmployer, decimal MpfEmployee,
    decimal ActualPay, decimal TotalAmount);

/// <summary>打卡明細合計：打卡筆數及 O.T. 小時（明細本身不存工時，工時只在薪酬表按工數算）。</summary>
public sealed record PunchTotals(int Records, decimal OvertimeHours);

/// <summary>
/// 打卡明細中參與計薪的欄位快照。改明細前後各取一次，只有這些欄位變動才重算薪酬——
/// 只改備註不會重算。職位和地盤不影響金額，但決定薪酬表的分組行，所以也在裡面。
/// </summary>
public readonly record struct PunchPayrollState(int Year, int Month, decimal OvertimeHours,
    bool IsLaborLeave, Guid? PositionDictId, Guid? SiteDictId)
{
    public static PunchPayrollState Of(AttendanceRecord record)
    {
        var (year, month) = AttendanceRules.HongKongMonth(record.ClockInTime);
        return new PunchPayrollState(year, month, record.OvertimeHours, record.IsLaborLeave,
            record.PositionDictId, record.SiteDictId);
    }
}

public sealed class AttendanceTableService(ErpDbContext db)
{
    /// <summary>
    /// 薪酬表篩選條件；列表、筆數和合計共用同一份，口徑不會漂移。
    /// 職位和地盤都按當月快照篩（改字典名稱不影響已生成的月份）。
    /// </summary>
    private IQueryable<MonthlyWorkSummary> PayrollQuery(int year, int month, Guid? workerId, Guid? positionId,
        Guid? siteDictId)
    {
        var query = db.MonthlyWorkSummaries.AsNoTracking()
            .Where(s => s.Year == year && s.Month == month && !s.Worker.IsDeleted);
        if (workerId is { } selected && selected != Guid.Empty) query = query.Where(s => s.WorkerId == selected);
        if (positionId is { } selectedPosition && selectedPosition != Guid.Empty)
            query = query.Where(s => s.PositionDictId == selectedPosition);
        if (siteDictId is { } selectedSite && selectedSite != Guid.Empty)
            query = query.Where(s => s.SiteDictId == selectedSite);
        return query;
    }

    /// <summary>打卡明細篩選條件；列表、筆數和合計共用同一份。</summary>
    private IQueryable<AttendanceRecord> PunchQuery(int year, int month, Guid? workerId)
    {
        var (start, end) = AttendanceRules.MonthRange(year, month);
        var query = db.AttendanceRecords.AsNoTracking()
            .Where(r => r.ClockInTime >= start && r.ClockInTime < end && !r.Worker.IsDeleted);
        return workerId is { } selected && selected != Guid.Empty ? query.Where(r => r.WorkerId == selected) : query;
    }

    /// <summary>
    /// 薪酬表查詢；workerId 有值就只看該員工（姓名下拉選的是 id，重名也不會混在一起），
    /// positionId／siteDictId 有值就只看該月快照符合的行。合計不含日薪。
    /// 一個員工一個月可能有多行（按職位 × 地盤分組），合計把它們加在一起。
    /// </summary>
    public async Task<(List<MonthlyWorkSummary> Items, int Total, PayrollTotals Totals)> GetPayrollAsync(
        int year, int month, Guid? workerId, Guid? positionId, Guid? siteDictId, int page, int pageSize,
        CancellationToken ct)
    {
        var query = PayrollQuery(year, month, workerId, positionId, siteDictId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(s => s.Worker)
            .OrderBy(s => s.Worker.Name).ThenBy(s => s.Position).ThenBy(s => s.Site).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var totals = await PayrollQuery(year, month, workerId, positionId, siteDictId)
            .GroupBy(_ => 1)
            .Select(group => new PayrollTotals(
                group.Sum(s => s.WorkDays), group.Sum(s => s.LaborLeaveDays), group.Sum(s => s.TotalWorkDays),
                group.Sum(s => s.OvertimeHours), group.Sum(s => s.OvertimeAmount), group.Sum(s => s.MpfEmployer),
                group.Sum(s => s.MpfEmployee), group.Sum(s => s.ActualPay), group.Sum(s => s.TotalAmount)))
            .FirstOrDefaultAsync(ct) ?? new PayrollTotals(0, 0, 0, 0, 0, 0, 0, 0, 0);
        return (items, total, totals);
    }

    /// <summary>打卡明細查詢；workerId 有值就只看該員工。</summary>
    public async Task<(List<AttendanceRecord> Items, int Total, PunchTotals Totals)> GetPunchesAsync(
        int year, int month, Guid? workerId, int page, int pageSize, CancellationToken ct)
    {
        var query = PunchQuery(year, month, workerId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(r => r.Worker).Include(r => r.Site).Include(r => r.Position)
            .OrderByDescending(r => r.ClockInTime).ThenBy(r => r.Worker.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var overtime = await PunchQuery(year, month, workerId).SumAsync(r => (decimal?)r.OvertimeHours, ct) ?? 0m;
        return (items, total, new PunchTotals(total, overtime));
    }

    /// <summary>
    /// 按該員工該月「全部」打卡明細重算薪酬表，按（職位 × 地盤）分組，每個組合一行。
    /// 同一個人在兩個地盤做過就是兩行，各自算工數、O.T.、MPF 和實付；
    /// 公式對工數是線性的，所以分行之後各行的合計和未分組時一致。
    /// 勞工假不在考勤卡上，沿用該行原有的值；備註也保留。找不到對應組合的行整行刪掉。
    /// </summary>
    public async Task<List<MonthlyWorkSummary>> RecomputePayrollAsync(Guid workerId, int year, int month,
        CancellationToken ct)
    {
        var worker = await db.Workers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == workerId, ct)
            ?? throw new KeyNotFoundException("員工不存在");
        var (start, end) = AttendanceRules.MonthRange(year, month);
        var records = await db.AttendanceRecords.AsNoTracking()
            .Include(record => record.Site)
            .Include(record => record.Position)
            .Where(record => record.WorkerId == workerId && record.ClockInTime >= start && record.ClockInTime < end)
            .ToListAsync(ct);

        var existing = await db.MonthlyWorkSummaries
            .Where(summary => summary.WorkerId == workerId && summary.Year == year && summary.Month == month)
            .ToListAsync(ct);
        // 整個月一筆明細都沒有：不動薪酬表，避免把人工維護的行清掉
        if (records.Count == 0) return existing;

        // 依（職位 × 地盤）找回舊行，保住人工填的勞工假和備註
        var reusable = existing.ToDictionary(summary => (summary.PositionDictId, summary.SiteDictId));
        var rows = new List<MonthlyWorkSummary>();
        foreach (var group in records.GroupBy(record => (record.PositionDictId, record.SiteDictId)))
        {
            if (!reusable.Remove(group.Key, out var summary))
            {
                summary = new MonthlyWorkSummary
                {
                    WorkerId = workerId, Year = year, Month = month, Remark = "由考勤卡自動計算"
                };
                db.MonthlyWorkSummaries.Add(summary);
            }
            summary.PositionDictId = group.Key.PositionDictId;
            summary.Position = group.First().Position?.Label;
            summary.SiteDictId = group.Key.SiteDictId;
            summary.Site = group.First().Site?.Label;
            AttendanceRules.ApplyPayroll(summary, worker.DailyWage,
                AttendanceRules.WorkDaysOf(group), group.Sum(record => record.OvertimeHours));
            rows.Add(summary);
        }
        // 組合已經沒有明細了，整行移除（該行的人工勞工假也一併消失——分組變動的必然結果）
        db.MonthlyWorkSummaries.RemoveRange(reusable.Values);
        await db.SaveChangesAsync(ct);
        return rows;
    }

    /// <summary>
    /// 改完打卡明細後按需要重算薪酬：只有參與計薪的欄位真的變動才重算，
    /// 月份跨月時兩個月的薪酬都要重算。回傳重算後的行；沒有需要重算時回空清單。
    /// </summary>
    public async Task<List<MonthlyWorkSummary>> RecomputeAfterPunchEditAsync(Guid workerId,
        PunchPayrollState before, PunchPayrollState after, CancellationToken ct)
    {
        if (before == after) return [];
        var months = new HashSet<(int Year, int Month)> { (before.Year, before.Month), (after.Year, after.Month) };
        var rows = new List<MonthlyWorkSummary>();
        foreach (var (year, month) in months)
            rows.AddRange(await RecomputePayrollAsync(workerId, year, month, ct));
        return rows;
    }

    public Task<MonthlyWorkSummary?> GetPayrollByIdAsync(Guid id, CancellationToken ct)
        => db.MonthlyWorkSummaries.Include(s => s.Worker).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<AttendanceRecord?> GetPunchByIdAsync(Guid id, CancellationToken ct)
        => db.AttendanceRecords.Include(r => r.Worker).Include(r => r.Site).Include(r => r.Position)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
