using System.ComponentModel.DataAnnotations;
using MC.Erp.Common.Security;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Attendance.Endpoints;

public static class AttendanceEndpoints
{
    public static void MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attendance").WithTags("Attendance");

        // 工人
        group.MapGet("/workers", ListWorkersAsync).RequireAuthorization();
        group.MapPost("/workers", CreateWorkerAsync).RequireAuthorization("admin");
        group.MapPut("/workers/{id:guid}", UpdateWorkerAsync).RequireAuthorization("admin");

        // 地盤
        group.MapGet("/sites", ListSitesAsync).RequireAuthorization();
        group.MapPost("/sites", CreateSiteAsync).RequireAuthorization("admin");

        // 打卡記錄
        group.MapGet("/records/{workerId:guid}", GetRecordsAsync).RequireAuthorization();

        // 月結工數表
        group.MapGet("/summaries", GetSummariesAsync).RequireAuthorization();
        group.MapGet("/summaries/{workerId:guid}", GetWorkerSummaryAsync).RequireAuthorization();

        // 操作
        group.MapPost("/sync", TriggerSyncAsync).RequireAuthorization("admin");
        group.MapPost("/calculate", TriggerCalculateAsync).RequireAuthorization("admin");
    }

    // --- 工人 ---
    private static async Task<Ok<List<WorkerDto>>> ListWorkersAsync(AttendanceServices svc, CancellationToken ct)
    {
        var workers = await svc.GetWorkersAsync(ct);
        return TypedResults.Ok(workers.Select(WorkerDto.From).ToList());
    }

    private static async Task<Created<WorkerDto>> CreateWorkerAsync(
        CreateWorkerRequest req, AttendanceServices svc, CancellationToken ct)
    {
        var worker = await svc.CreateWorkerAsync(req.Name, req.EmployeeId, req.DailyWage, ct);
        return TypedResults.Created($"/api/attendance/workers/{worker.Id}", WorkerDto.From(worker));
    }

    private static async Task<Results<Ok<WorkerDto>, NotFound>> UpdateWorkerAsync(
        Guid id, UpdateWorkerRequest req, AttendanceServices svc, CancellationToken ct)
    {
        var worker = await svc.UpdateWorkerAsync(id, req.DailyWage, req.IsActive, ct);
        return worker is null ? TypedResults.NotFound() : TypedResults.Ok(WorkerDto.From(worker));
    }

    // --- 地盤 ---
    private static async Task<Ok<List<SiteDto>>> ListSitesAsync(AttendanceServices svc, CancellationToken ct)
    {
        var sites = await svc.GetSitesAsync(ct);
        return TypedResults.Ok(sites.Select(SiteDto.From).ToList());
    }

    private static async Task<Created<SiteDto>> CreateSiteAsync(
        CreateSiteRequest req, AttendanceServices svc, CancellationToken ct)
    {
        var site = await svc.CreateSiteAsync(req.Name, req.Address, ct);
        return TypedResults.Created($"/api/attendance/sites/{site.Id}", SiteDto.From(site));
    }

    // --- 打卡記錄 ---
    private static async Task<Ok<List<RecordDto>>> GetRecordsAsync(
        Guid workerId, DateTime? start, DateTime? end, AttendanceServices svc, CancellationToken ct)
    {
        var to = end ?? DateTime.UtcNow;
        var from = start ?? to.AddDays(-30);
        var records = await svc.GetRecordsAsync(workerId, from, to, ct);
        return TypedResults.Ok(records.Select(RecordDto.From).ToList());
    }

    // --- 月結 ---
    private static async Task<Ok<List<SummaryDto>>> GetSummariesAsync(
        int year, int month, AttendanceServices svc, CancellationToken ct)
    {
        var summaries = await svc.GetSummariesAsync(year, month, ct);
        return TypedResults.Ok(summaries.Select(SummaryDto.From).ToList());
    }

    private static async Task<Results<Ok<SummaryDto>, NotFound>> GetWorkerSummaryAsync(
        Guid workerId, int year, int month, AttendanceServices svc, CancellationToken ct)
    {
        var summary = await svc.GetSummaryAsync(workerId, year, month, ct);
        return summary is null ? TypedResults.NotFound() : TypedResults.Ok(SummaryDto.From(summary));
    }

    // --- 操作 ---
    private static async Task<Ok> TriggerSyncAsync(AttendanceSyncService sync, CancellationToken ct)
    {
        await sync.SyncDailyAsync(ct);
        return TypedResults.Ok();
    }

    private static async Task<Ok> TriggerCalculateAsync(
        int year, int month, PayrollService payroll, CancellationToken ct)
    {
        await payroll.計算月結Async(year, month, ct);
        return TypedResults.Ok();
    }
}

// DTOs
public sealed record WorkerDto(Guid Id, string Name, string? EmployeeId, decimal DailyWage, bool IsActive)
{
    public static WorkerDto From(Worker w) => new(w.Id, w.Name, w.EmployeeId, w.DailyWage, w.IsActive);
}
public sealed record SiteDto(Guid Id, string Name, string? Address, bool IsActive)
{
    public static SiteDto From(Site s) => new(s.Id, s.Name, s.Address, s.IsActive);
}
public sealed record RecordDto(Guid Id, Guid WorkerId, Guid SiteId, DateTime ClockInTime, DateTime? ClockOutTime, decimal OvertimeHours, bool IsLaborLeave, string? Remark)
{
    public static RecordDto From(AttendanceRecord r) => new(r.Id, r.WorkerId, r.SiteId, r.ClockInTime, r.ClockOutTime, r.OvertimeHours, r.IsLaborLeave, r.Remark);
}
public sealed record SummaryDto(Guid Id, Guid WorkerId, int Year, int Month, decimal WorkDays, decimal LaborLeaveDays, decimal TotalWorkDays, decimal OvertimeHours, decimal DailyWage, decimal OvertimeAmount, decimal MpfEmployer, decimal MpfEmployee, decimal ActualPay, decimal TotalAmount, string? Remark)
{
    public static SummaryDto From(MonthlyWorkSummary s) => new(s.Id, s.WorkerId, s.Year, s.Month, s.WorkDays, s.LaborLeaveDays, s.TotalWorkDays, s.OvertimeHours, s.DailyWage, s.OvertimeAmount, s.MpfEmployer, s.MpfEmployee, s.ActualPay, s.TotalAmount, s.Remark);
}

public sealed record CreateWorkerRequest([Required] string Name, string? EmployeeId, decimal DailyWage);
public sealed record UpdateWorkerRequest(decimal? DailyWage, bool? IsActive);
public sealed record CreateSiteRequest([Required] string Name, string? Address);
