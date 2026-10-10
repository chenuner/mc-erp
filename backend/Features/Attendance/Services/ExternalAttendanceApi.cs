using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 外部考勤 API 實現（待對接真實接口後替換，屆時再注入 IHttpClientFactory）
/// </summary>
public sealed class ExternalAttendanceApi(ILogger<ExternalAttendanceApi> logger) : IExternalAttendanceApi
{
    public async Task<List<ExternalAttendanceRecord>> FetchRecordsAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        // TODO: 對接真實外部考勤 API
        // var client = httpClientFactory.CreateClient("AttendanceApi");
        // var response = await client.GetFromJsonAsync<ExternalAttendanceResponse>(
        //     $"/api/records?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}", ct);

        logger.LogInformation("外部考勤API尚未對接，返回空數據。start={Start}, end={End}", start, end);
        await Task.CompletedTask;
        return [];
    }
}
