namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 外部考勤打卡 API 接口
/// </summary>
public interface IExternalAttendanceApi
{
    /// <summary>
    /// 拉取指定日期範圍的打卡記錄
    /// </summary>
    Task<List<ExternalAttendanceRecord>> FetchRecordsAsync(DateTime start, DateTime end, CancellationToken ct = default);
}

/// <summary>
/// 外部系統返回的打卡記錄
/// </summary>
public sealed class ExternalAttendanceRecord
{
    public string EmployeeId { get; set; } = "";
    public string WorkerName { get; set; } = "";
    public DateTime ClockInTime { get; set; }
    public DateTime? ClockOutTime { get; set; }
    public decimal OvertimeHours { get; set; }
    public bool IsLaborLeave { get; set; }
    public string? SiteName { get; set; }
}
