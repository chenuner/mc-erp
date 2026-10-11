using Microsoft.Extensions.Logging;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 外部考勤 API 實現（待對接真實接口後替換，屆時再注入 IHttpClientFactory）
/// </summary>
public sealed class ExternalAttendanceApi(ILogger<ExternalAttendanceApi> logger) : IExternalAttendanceApi
{
    public Task<List<ExternalAttendanceRecord>> FetchRecordsAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var records = new List<ExternalAttendanceRecord>();
        string[] names = ["陳大文", "李志明", "黃建華", "張偉強", "何國輝", "林永安"];
        for (var date = start.Date; date < end; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            for (var index = 0; index < names.Length; index++)
            {
                var clockIn = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                if (clockIn < start || clockIn >= end) continue;
                var leave = date.Day == 1;
                var overtime = !leave && (date.Day + index) % 5 == 0 ? 2m : 0m;
                records.Add(new ExternalAttendanceRecord
                {
                    EmployeeId = $"MOCK-{index + 1:000}",
                    WorkerName = names[index],
                    ClockInTime = clockIn,
                    ClockOutTime = leave ? null : clockIn.AddHours(9 + (double)overtime),
                    OvertimeHours = overtime,
                    IsLaborLeave = leave,
                    SiteName = index < 3 ? "九龍灣地盤" : "沙田地盤"
                });
            }
        }
        logger.LogInformation("生成模擬打卡數據 {Count} 條，範圍 {Start} ~ {End}", records.Count, start, end);
        return Task.FromResult(records);
    }
}
