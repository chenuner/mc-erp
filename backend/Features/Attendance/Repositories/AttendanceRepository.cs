using MC.Erp.Data;
using MC.Erp.Features.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Attendance.Repositories;

public sealed class AttendanceRepository(ErpDbContext db) : IAttendanceRepository
{
    // --- Worker ---
    public Task<Worker?> GetWorkerByIdAsync(Guid id, CancellationToken ct = default)
        => db.Workers.Include(w => w.Position).FirstOrDefaultAsync(w => w.Id == id, ct);

    // 停用的員工仍要列出來（員工管理要能再啟用），只排除已刪除的
    public Task<List<Worker>> GetAllWorkersAsync(CancellationToken ct = default)
        => db.Workers.Include(w => w.Position).Where(w => !w.IsDeleted).OrderBy(w => w.Name).ToListAsync(ct);

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

    // --- MonthlyWorkSummary ---
    public Task<MonthlyWorkSummary?> GetSummaryAsync(Guid workerId, int year, int month, CancellationToken ct = default)
        => db.MonthlyWorkSummaries.FirstOrDefaultAsync(s => s.WorkerId == workerId && s.Year == year && s.Month == month, ct);

    public Task<List<MonthlyWorkSummary>> GetSummariesByMonthAsync(int year, int month, CancellationToken ct = default)
        => db.MonthlyWorkSummaries.Where(s => s.Year == year && s.Month == month).ToListAsync(ct);
}
