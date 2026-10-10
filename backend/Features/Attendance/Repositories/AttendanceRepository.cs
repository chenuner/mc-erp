using MC.Erp.Data;
using MC.Erp.Features.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Attendance.Repositories;

public sealed class AttendanceRepository(ErpDbContext db) : IAttendanceRepository
{
    // --- Worker ---
    public Task<Worker?> GetWorkerByIdAsync(Guid id, CancellationToken ct = default)
        => db.Workers.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<List<Worker>> GetAllWorkersAsync(CancellationToken ct = default)
        => db.Workers.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync(ct);

    public Task<Worker?> GetWorkerByEmployeeIdAsync(string employeeId, CancellationToken ct = default)
        => db.Workers.FirstOrDefaultAsync(w => w.EmployeeId == employeeId, ct);

    public async Task AddWorkerAsync(Worker worker, CancellationToken ct = default)
    {
        await db.Workers.AddAsync(worker, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateWorkerAsync(Worker worker, CancellationToken ct = default)
    {
        worker.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // --- Site ---
    public Task<List<Site>> GetAllSitesAsync(CancellationToken ct = default)
        => db.Sites.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);

    public Task<Site?> GetSiteByIdAsync(Guid id, CancellationToken ct = default)
        => db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddSiteAsync(Site site, CancellationToken ct = default)
    {
        await db.Sites.AddAsync(site, ct);
        await db.SaveChangesAsync(ct);
    }

    // --- AttendanceRecord ---
    public async Task<List<AttendanceRecord>> GetRecordsAsync(Guid workerId, DateTime start, DateTime end, CancellationToken ct = default)
    {
        return await db.AttendanceRecords
            .Where(r => r.WorkerId == workerId && r.ClockInTime >= start && r.ClockInTime < end)
            .OrderBy(r => r.ClockInTime)
            .ToListAsync(ct);
    }

    public async Task AddRecordsAsync(List<AttendanceRecord> records, CancellationToken ct = default)
    {
        await db.AttendanceRecords.AddRangeAsync(records, ct);
        await db.SaveChangesAsync(ct);
    }

    // --- MonthlyWorkSummary ---
    public Task<MonthlyWorkSummary?> GetSummaryAsync(Guid workerId, int year, int month, CancellationToken ct = default)
        => db.MonthlyWorkSummaries.FirstOrDefaultAsync(s => s.WorkerId == workerId && s.Year == year && s.Month == month, ct);

    public Task<List<MonthlyWorkSummary>> GetSummariesByMonthAsync(int year, int month, CancellationToken ct = default)
        => db.MonthlyWorkSummaries.Where(s => s.Year == year && s.Month == month).ToListAsync(ct);

    public async Task UpsertSummaryAsync(MonthlyWorkSummary summary, CancellationToken ct = default)
    {
        var existing = await GetSummaryAsync(summary.WorkerId, summary.Year, summary.Month, ct);
        if (existing is not null)
        {
            existing.WorkDays = summary.WorkDays;
            existing.LaborLeaveDays = summary.LaborLeaveDays;
            existing.TotalWorkDays = summary.TotalWorkDays;
            existing.OvertimeHours = summary.OvertimeHours;
            existing.DailyWage = summary.DailyWage;
            existing.OvertimeAmount = summary.OvertimeAmount;
            existing.MpfEmployer = summary.MpfEmployer;
            existing.MpfEmployee = summary.MpfEmployee;
            existing.ActualPay = summary.ActualPay;
            existing.TotalAmount = summary.TotalAmount;
            existing.Remark = summary.Remark;
        }
        else
        {
            await db.MonthlyWorkSummaries.AddAsync(summary, ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
