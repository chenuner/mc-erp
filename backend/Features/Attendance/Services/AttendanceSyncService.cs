using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Repositories;
using Microsoft.Extensions.Logging;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 考勤數據同步服務（Hangfire 定時調用）
/// </summary>
public sealed class AttendanceSyncService(
    IExternalAttendanceApi externalApi,
    IAttendanceRepository repo,
    ILogger<AttendanceSyncService> logger)
{
    /// <summary>
    /// 同步昨天和今天的打卡數據（Hangfire 每天調用）
    /// </summary>
    public async Task SyncDailyAsync(CancellationToken ct = default)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-1);

        logger.LogInformation("開始同步考勤數據 {Start} ~ {End}", start, end);

        var records = await externalApi.FetchRecordsAsync(start, end, ct);
        if (records.Count == 0)
        {
            logger.LogInformation("無新考勤數據");
            return;
        }

        var mapped = new List<AttendanceRecord>();
        foreach (var r in records)
        {
            var worker = await repo.GetWorkerByEmployeeIdAsync(r.EmployeeId, ct);
            if (worker is null)
            {
                logger.LogWarning("找不到工號 {EmployeeId} 對應的工人，跳過", r.EmployeeId);
                continue;
            }

            Site? site = null;
            if (!string.IsNullOrEmpty(r.SiteName))
            {
                var sites = await repo.GetAllSitesAsync(ct);
                site = sites.FirstOrDefault(s => s.Name == r.SiteName);
            }
            site ??= sites.FirstOrDefault() ?? await EnsureDefaultSite(ct);

            mapped.Add(new AttendanceRecord
            {
                WorkerId = worker.Id,
                SiteId = site.Id,
                ClockInTime = r.ClockInTime,
                ClockOutTime = r.ClockOutTime,
                OvertimeHours = r.OvertimeHours,
                IsLaborLeave = r.IsLaborLeave
            });
        }

        await repo.AddRecordsAsync(mapped, ct);
        logger.LogInformation("同步完成，寫入 {Count} 條打卡記錄", mapped.Count);
    }

    private List<Site> sites = [];

    private async Task<Site> EnsureDefaultSite(CancellationToken ct)
    {
        sites = await repo.GetAllSitesAsync(ct);
        var site = sites.FirstOrDefault();
        if (site is null)
        {
            site = new Site { Name = "默認地盤" };
            await repo.AddSiteAsync(site, ct);
            sites.Add(site);
        }
        return site;
    }
}
