using MC.Erp.Features.Attendance.Entities;

namespace MC.Erp.Features.Attendance.Repositories;

public interface IAttendanceRepository
{
    // Worker
    Task<Worker?> GetWorkerByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Worker>> GetAllWorkersAsync(CancellationToken ct = default);
    Task AddWorkerAsync(Worker worker, CancellationToken ct = default);
    Task UpdateWorkerAsync(Worker worker, CancellationToken ct = default);

    // Site
    // 地盤是系統字典項（AccountingSiteCategory），由 SysDict 維護，不再有獨立的地盤表

    // 打卡明細與薪酬表查詢由 AttendanceTableService 直接用 DbContext 處理（列表、合計共用同一份篩選）
}
