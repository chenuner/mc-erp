using MC.Erp.Features.Attendance.Entities;

namespace MC.Erp.Features.Attendance.Repositories;

public interface IAttendanceRepository
{
    // Worker
    Task<Worker?> GetWorkerByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Worker>> GetAllWorkersAsync(CancellationToken ct = default);
    Task<Worker?> GetWorkerByEmployeeIdAsync(string employeeId, CancellationToken ct = default);
    Task AddWorkerAsync(Worker worker, CancellationToken ct = default);
    Task UpdateWorkerAsync(Worker worker, CancellationToken ct = default);

    // Site
    Task<List<Site>> GetAllSitesAsync(CancellationToken ct = default);
    Task<Site?> GetSiteByIdAsync(Guid id, CancellationToken ct = default);
    Task AddSiteAsync(Site site, CancellationToken ct = default);

    // AttendanceRecord
    Task<List<AttendanceRecord>> GetRecordsAsync(Guid workerId, DateTime start, DateTime end, CancellationToken ct = default);
    Task AddRecordsAsync(List<AttendanceRecord> records, CancellationToken ct = default);

    // MonthlyWorkSummary
    Task<MonthlyWorkSummary?> GetSummaryAsync(Guid workerId, int year, int month, CancellationToken ct = default);
    Task<List<MonthlyWorkSummary>> GetSummariesByMonthAsync(int year, int month, CancellationToken ct = default);
    Task UpsertSummaryAsync(MonthlyWorkSummary summary, CancellationToken ct = default);
}
