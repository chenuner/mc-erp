using MC.Erp.Data;
using MC.Erp.Features.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Attendance.Services;

public sealed class AttendanceTableService(ErpDbContext db, IExternalAttendanceApi externalApi)
{
    public async Task<(List<MonthlyWorkSummary> Items, int Total)> GetPayrollAsync(
        int year, int month, string? name, int page, int pageSize, CancellationToken ct)
    {
        var query = db.MonthlyWorkSummaries.AsNoTracking().Include(s => s.Worker)
            .Where(s => s.Year == year && s.Month == month && !s.Worker.IsDeleted);
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(s => s.Worker.Name.Contains(name.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Worker.Name).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(List<AttendanceRecord> Items, int Total)> GetPunchesAsync(
        int year, int month, string? name, int page, int pageSize, CancellationToken ct)
    {
        var (start, end) = MonthRange(year, month);
        var query = db.AttendanceRecords.AsNoTracking().Include(r => r.Worker).Include(r => r.Site)
            .Where(r => r.ClockInTime >= start && r.ClockInTime < end && !r.Worker.IsDeleted && !r.Site.IsDeleted);
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(r => r.Worker.Name.Contains(name.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.ClockInTime).ThenBy(r => r.Worker.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<MonthlyWorkSummary?> GetPayrollByIdAsync(Guid id, CancellationToken ct)
        => db.MonthlyWorkSummaries.Include(s => s.Worker).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<AttendanceRecord?> GetPunchByIdAsync(Guid id, CancellationToken ct)
        => db.AttendanceRecords.Include(r => r.Worker).Include(r => r.Site).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<int> InitializeMockAsync(int year, int month, CancellationToken ct)
    {
        var (start, end) = MonthRange(year, month);
        var source = await externalApi.FetchRecordsAsync(start, end, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var added = 0;
        var index = 0;
        var sites = await db.Sites.ToListAsync(ct);
        foreach (var group in source.GroupBy(r => r.EmployeeId))
        {
            var sample = group.First();
            var worker = await db.Workers.FirstOrDefaultAsync(w => w.EmployeeId == group.Key, ct);
            worker ??= new Worker { EmployeeId = group.Key, Name = sample.WorkerName, DailyWage = 900m + index * 50m };
            index++;
            if (await db.MonthlyWorkSummaries.AnyAsync(s => s.WorkerId == worker.Id && s.Year == year && s.Month == month, ct))
                continue;
            if (db.Entry(worker).State == EntityState.Detached) db.Workers.Add(worker);
            foreach (var item in group)
            {
                var site = sites.FirstOrDefault(s => s.Name == item.SiteName);
                if (site is null)
                {
                    site = new Site { Name = item.SiteName ?? "模擬地盤" };
                    sites.Add(site);
                    db.Sites.Add(site);
                }
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    Worker = worker, Site = site, ClockInTime = item.ClockInTime,
                    ClockOutTime = item.ClockOutTime, OvertimeHours = item.OvertimeHours,
                    IsLaborLeave = item.IsLaborLeave, Remark = "模擬打卡"
                });
            }
            db.MonthlyWorkSummaries.Add(new MonthlyWorkSummary
            {
                Worker = worker, Year = year, Month = month,
                WorkDays = 24m, LaborLeaveDays = 1m, TotalWorkDays = 25m, OvertimeHours = 8m,
                DailyWage = worker.DailyWage, OvertimeAmount = 1200m,
                MpfEmployer = 1200m, MpfEmployee = 1200m,
                ActualPay = 22500m + index * 100m, TotalAmount = 24900m + index * 100m,
                Remark = "模擬薪酬，金額為樣例值，待確認計薪規則"
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return added;
    }

    private static (DateTime Start, DateTime End) MonthRange(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-8);
        return (start, new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddHours(-8));
    }
}