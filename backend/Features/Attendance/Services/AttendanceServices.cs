using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Repositories;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// Attendance 模組查詢/操作服務（供 Endpoints 使用）
/// </summary>
public sealed class AttendanceServices(IAttendanceRepository repo, SysDictService dicts)
{
    /// <summary>職位分類的字典分組代碼；由 SeedData 建立，選項由用戶在系統字典維護。</summary>
    private const string PositionGroup = "AttendancePosition";

    public Task<List<Worker>> GetWorkersAsync(CancellationToken ct = default)
        => repo.GetAllWorkersAsync(ct);

    /// <summary>新增員工；職位分類可選，不傳即留空。</summary>
    public async Task<Worker> CreateWorkerAsync(string name, string? employeeId, decimal dailyWage,
        Guid? positionId = null, CancellationToken ct = default)
    {
        var worker = new Worker { Name = name, EmployeeId = employeeId, DailyWage = dailyWage };
        if (positionId is { } selected && selected != Guid.Empty)
        {
            var position = await PositionAsync(selected, ct);
            worker.PositionDictId = position.Id;
            worker.Position = position;
        }
        await repo.AddWorkerAsync(worker, ct);
        return worker;
    }

    /// <summary>
    /// 更新員工；positionId 不傳＝不改職位，傳 Guid.Empty＝清空職位。
    /// 只改有給的欄位；姓名不允許改成空白，工號給空字串代表清空。
    /// </summary>
    public async Task<Worker?> UpdateWorkerAsync(Guid id, string? name, string? employeeId, decimal? dailyWage,
        bool? isActive, Guid? positionId = null, CancellationToken ct = default)
    {
        var worker = await repo.GetWorkerByIdAsync(id, ct);
        if (worker is null) return null;
        if (!string.IsNullOrWhiteSpace(name)) worker.Name = name.Trim();
        if (employeeId is not null) worker.EmployeeId = string.IsNullOrWhiteSpace(employeeId) ? null : employeeId.Trim();
        if (dailyWage is not null) worker.DailyWage = dailyWage.Value;
        if (isActive is not null) worker.IsActive = isActive.Value;
        if (positionId is { } selected)
        {
            if (selected == Guid.Empty)
            {
                worker.PositionDictId = null;
                worker.Position = null;
            }
            else
            {
                var position = await PositionAsync(selected, ct);
                worker.PositionDictId = position.Id;
                worker.Position = position;
            }
        }
        await repo.UpdateWorkerAsync(worker, ct);
        return worker;
    }

    /// <summary>職位分類必須存在且屬於 AttendancePosition 分組；停用項仍可保留在員工身上。</summary>
    private async Task<SysDict> PositionAsync(Guid id, CancellationToken ct)
    {
        var position = await dicts.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("職位分類不存在");
        if (position.GroupName != PositionGroup)
            throw new InvalidOperationException("請選擇有效的職位分類");
        return position;
    }

    public async Task DeleteWorkerAsync(Guid id, CancellationToken ct = default)
    {
        var worker = await repo.GetWorkerByIdAsync(id, ct) ?? throw new KeyNotFoundException("員工不存在");
        // 軟刪除：已入庫的打卡明細和薪酬表保留，只是列表、下拉和薪酬查詢都不再顯示
        worker.IsDeleted = true;
        worker.DeletedAt = DateTime.UtcNow;
        await repo.UpdateWorkerAsync(worker, ct);
    }
}
