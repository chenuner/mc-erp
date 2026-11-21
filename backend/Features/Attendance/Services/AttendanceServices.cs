using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Repositories;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// Attendance 模組查詢/操作服務（供 Endpoints 使用）
/// </summary>
public sealed class AttendanceServices(IAttendanceRepository repo)
{
    public Task<List<Worker>> GetWorkersAsync(CancellationToken ct = default)
        => repo.GetAllWorkersAsync(ct);

    public async Task<Worker> CreateWorkerAsync(string name, string? employeeId, decimal dailyWage, CancellationToken ct = default)
    {
        var worker = new Worker { Name = name, EmployeeId = employeeId, DailyWage = dailyWage };
        await repo.AddWorkerAsync(worker, ct);
        return worker;
    }

    public async Task<Worker?> UpdateWorkerAsync(Guid id, string? name, string? employeeId, decimal? dailyWage, bool? isActive, CancellationToken ct = default)
    {
        var worker = await repo.GetWorkerByIdAsync(id, ct);
        if (worker is null) return null;
        // 只改有給的欄位；姓名不允許改成空白，工號給空字串代表清空
        if (!string.IsNullOrWhiteSpace(name)) worker.Name = name.Trim();
        if (employeeId is not null) worker.EmployeeId = string.IsNullOrWhiteSpace(employeeId) ? null : employeeId.Trim();
        if (dailyWage is not null) worker.DailyWage = dailyWage.Value;
        if (isActive is not null) worker.IsActive = isActive.Value;
        await repo.UpdateWorkerAsync(worker, ct);
        return worker;
    }

    public async Task DeleteWorkerAsync(Guid id, CancellationToken ct = default)
    {
        var worker = await repo.GetWorkerByIdAsync(id, ct) ?? throw new KeyNotFoundException("員工不存在");
        // 軟刪除：已入庫的打卡明細和薪酬表保留，只是列表、下拉和薪酬查詢都不再顯示
        worker.IsDeleted = true;
        worker.DeletedAt = DateTime.UtcNow;
        await repo.UpdateWorkerAsync(worker, ct);
    }

    public Task<List<Site>> GetSitesAsync(CancellationToken ct = default)
        => repo.GetAllSitesAsync(ct);

    public async Task<Site> CreateSiteAsync(string name, string? address, CancellationToken ct = default)
    {
        var site = new Site { Name = name, Address = address };
        await repo.AddSiteAsync(site, ct);
        return site;
    }

    public async Task<List<AttendanceRecord>> GetRecordsAsync(Guid workerId, DateTime start, DateTime end, CancellationToken ct = default)
        => await repo.GetRecordsAsync(workerId, start, end, ct);

    public Task<List<MonthlyWorkSummary>> GetSummariesAsync(int year, int month, CancellationToken ct = default)
        => repo.GetSummariesByMonthAsync(year, month, ct);

    public Task<MonthlyWorkSummary?> GetSummaryAsync(Guid workerId, int year, int month, CancellationToken ct = default)
        => repo.GetSummaryAsync(workerId, year, month, ct);
}
