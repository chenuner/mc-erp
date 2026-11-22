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

public sealed class AttendanceTableService(ErpDbContext db)
{
    /// <summary>薪酬表篩選條件；列表、筆數和合計共用同一份，口徑不會漂移。職位按當月快照篩。</summary>
    private IQueryable<MonthlyWorkSummary> PayrollQuery(int year, int month, Guid? workerId, Guid? positionId)
    {
        var query = db.MonthlyWorkSummaries.AsNoTracking()
            .Where(s => s.Year == year && s.Month == month && !s.Worker.IsDeleted);
        if (workerId is { } selected && selected != Guid.Empty) query = query.Where(s => s.WorkerId == selected);
        if (positionId is { } selectedPosition && selectedPosition != Guid.Empty)
            query = query.Where(s => s.PositionDictId == selectedPosition);
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
    /// positionId 有值就只看該月職位快照符合的員工。合計不含日薪。
    /// </summary>
    public async Task<(List<MonthlyWorkSummary> Items, int Total, PayrollTotals Totals)> GetPayrollAsync(
        int year, int month, Guid? workerId, Guid? positionId, int page, int pageSize, CancellationToken ct)
    {
        var query = PayrollQuery(year, month, workerId, positionId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(s => s.Worker).OrderBy(s => s.Worker.Name).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var totals = await PayrollQuery(year, month, workerId, positionId)
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
        var items = await query.Include(r => r.Worker).Include(r => r.Site)
            .OrderByDescending(r => r.ClockInTime).ThenBy(r => r.Worker.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var overtime = await PunchQuery(year, month, workerId).SumAsync(r => (decimal?)r.OvertimeHours, ct) ?? 0m;
        return (items, total, new PunchTotals(total, overtime));
    }

    public Task<MonthlyWorkSummary?> GetPayrollByIdAsync(Guid id, CancellationToken ct)
        => db.MonthlyWorkSummaries.Include(s => s.Worker).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<AttendanceRecord?> GetPunchByIdAsync(Guid id, CancellationToken ct)
        => db.AttendanceRecords.Include(r => r.Worker).Include(r => r.Site).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
