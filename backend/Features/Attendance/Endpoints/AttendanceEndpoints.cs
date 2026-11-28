using System.ComponentModel.DataAnnotations;
using MC.Erp.Common;
using MC.Erp.Common.Errors;
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
        group.AddEndpointFilter<ExceptionProblemFilter>();

        // 工人
        group.MapGet("/workers", ListWorkersAsync).RequireAuthorization();
        group.MapPost("/workers", CreateWorkerAsync).RequireAuthorization("admin");
        group.MapPut("/workers/{id:guid}", UpdateWorkerAsync).RequireAuthorization("admin");
        group.MapDelete("/workers/{id:guid}", DeleteWorkerAsync).RequireAuthorization("admin");

        // 地盤：系統字典（AccountingSiteCategory）維護，不再有 /sites 端點

        // 考勤卡掃描：掃描即落庫，可再查詢、人工修正及刪除
        group.MapPost("/scan", ScanCardAsync).RequireAuthorization("admin").DisableAntiforgery();
        group.MapGet("/scans", ListCardScansAsync).RequireAuthorization();
        group.MapGet("/scans/{id:guid}", GetCardScanAsync).RequireAuthorization();
        group.MapPut("/scans/{id:guid}", UpdateCardScanAsync).RequireAuthorization("admin");
        // 人工對照原卡核對完：清掉卡片層複核標記
        group.MapPost("/scans/{id:guid}/confirm", ConfirmCardScanAsync).RequireAuthorization("admin");
        group.MapDelete("/scans/{id:guid}", DeleteCardScanAsync).RequireAuthorization("admin");
        // 同步：把識別結果導入打卡明細，並重算該員工該月的薪酬表
        group.MapPost("/scans/{id:guid}/sync", SyncCardAsync).RequireAuthorization("admin");

        // 薪酬表（含人工迴寫）
        group.MapAttendanceTableEndpoints();
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
        var worker = await svc.CreateWorkerAsync(req.Name, req.EmployeeId, req.DailyWage, req.PositionId, ct);
        return TypedResults.Created($"/api/attendance/workers/{worker.Id}", WorkerDto.From(worker));
    }

    private static async Task<Results<Ok<WorkerDto>, NotFound>> UpdateWorkerAsync(
        Guid id, UpdateWorkerRequest req, AttendanceServices svc, CancellationToken ct)
    {
        var worker = await svc.UpdateWorkerAsync(id, req.Name, req.EmployeeId, req.DailyWage, req.IsActive, req.PositionId, ct);
        return worker is null ? TypedResults.NotFound() : TypedResults.Ok(WorkerDto.From(worker));
    }

    /// <summary>刪除員工：軟刪除，已入庫的打卡明細和薪酬保留但不顯示；要暫時隱藏請用停用（isActive=false）。</summary>
    private static async Task<NoContent> DeleteWorkerAsync(Guid id, AttendanceServices svc, CancellationToken ct)
    {
        await svc.DeleteWorkerAsync(id, ct);
        return TypedResults.NoContent();
    }

    // --- 考勤卡掃描 ---
    private static async Task<Ok<AttendanceCardScanDto>> ScanCardAsync(
        IFormFile file, AttendanceCardService svc, CancellationToken ct)
        => TypedResults.Ok(await svc.ScanAsync(file, ct));

    private static bool ValidScanQuery(int? year, int? month, int page, int pageSize)
        => (year is null or >= 2000 and <= 2099) && (month is null or >= 1 and <= 12)
        && page is >= 1 and <= 100000 && pageSize is >= 1 and <= 100;

    private static ValidationProblem InvalidScanQuery() => TypedResults.ValidationProblem(new Dictionary<string, string[]>
    {
        ["query"] = ["年份須為 2000 至 2099、月份須為 1 至 12（兩者可不填），頁碼須為 1 至 100000，每頁須為 1 至 100。"]
    });

    private static async Task<Results<Ok<PagedResult<AttendanceCardScanSummaryDto>>, ValidationProblem>> ListCardScansAsync(
        int? year, int? month, AttendanceCardService svc, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (!ValidScanQuery(year, month, page, pageSize)) return InvalidScanQuery();
        var result = await svc.ListAsync(year, month, page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<AttendanceCardScanSummaryDto>(result.Items, result.Total, page, pageSize));
    }

    private static async Task<Ok<AttendanceCardScanDto>> GetCardScanAsync(
        Guid id, AttendanceCardService svc, CancellationToken ct)
        => TypedResults.Ok(await svc.FindAsync(id, ct));

    /// <summary>人工修正識別結果（年月、姓名、每日時間）；只寫回真正改動的欄位。</summary>
    private static async Task<Ok<AttendanceCardScanDto>> UpdateCardScanAsync(
        Guid id, UpdateCardScanRequest request, AttendanceCardService svc, CancellationToken ct)
        => TypedResults.Ok(await svc.UpdateAsync(id, request, ct));

    /// <summary>人工核對完成：清掉卡片層的複核標記並記下核對時間（時間欄不改），狀態由「需覆核」變「已核對」；這是同步的前提。</summary>
    private static async Task<Ok<AttendanceCardScanDto>> ConfirmCardScanAsync(
        Guid id, ConfirmCardScanRequest request, AttendanceCardService svc, CancellationToken ct)
        => TypedResults.Ok(await svc.ConfirmAsync(id, request, ct));

    private static async Task<NoContent> DeleteCardScanAsync(
        Guid id, AttendanceCardService svc, CancellationToken ct)
    {
        await svc.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }

    /// <summary>把識別結果導入打卡明細（員工、地盤由使用者指定），並重算該員工該月的薪酬表。</summary>
    private static async Task<Ok<CardSyncResultDto>> SyncCardAsync(
        Guid id, SyncCardRequest request, AttendanceCardService svc, CancellationToken ct)
    {
        var result = await svc.SyncAsync(id, request, ct);
        return TypedResults.Ok(new CardSyncResultDto(result.ScanId, result.Year, result.Month, result.WorkerId,
            result.WorkerName, result.SiteDictId, result.AddedRecords,
            result.Summaries.Select(CardPayrollRowDto.From).ToList()));
    }
}

// DTOs
public sealed record WorkerDto(Guid Id, string Name, string? EmployeeId, decimal DailyWage, bool IsActive,
    Guid? PositionId)
{
    public static WorkerDto From(Worker w) => new(w.Id, w.Name, w.EmployeeId, w.DailyWage, w.IsActive,
        w.PositionDictId);
}

public sealed record CreateWorkerRequest([Required, MaxLength(50)] string Name, [MaxLength(50)] string? EmployeeId,
    [Range(typeof(decimal), "0", "999999.99")] decimal DailyWage, Guid? PositionId = null);
/// <summary>員工管理：只改有給的欄位（姓名、工號、日薪、在職、職位分類）；positionId 傳空 Guid 代表清空職位。</summary>
public sealed record UpdateWorkerRequest([MaxLength(50)] string? Name, [MaxLength(50)] string? EmployeeId,
    [Range(typeof(decimal), "0", "999999.99")] decimal? DailyWage, bool? IsActive, Guid? PositionId = null);

/// <summary>同步後該員工該月薪酬表的一行（按職位 × 地盤分組）；員工資訊在結果頂層。</summary>
public sealed record CardPayrollRowDto(Guid Id, Guid? PositionDictId, string? Position, Guid? SiteDictId, string? Site,
    decimal WorkDays, decimal LaborLeaveDays, decimal TotalWorkDays, decimal OvertimeHours, decimal DailyWage,
    decimal OvertimeAmount, decimal MpfEmployer, decimal MpfEmployee, decimal ActualPay, decimal TotalAmount)
{
    public static CardPayrollRowDto From(MonthlyWorkSummary row) => new(row.Id, row.PositionDictId, row.Position,
        row.SiteDictId, row.Site, row.WorkDays, row.LaborLeaveDays, row.TotalWorkDays, row.OvertimeHours,
        row.DailyWage, row.OvertimeAmount, row.MpfEmployer, row.MpfEmployee, row.ActualPay, row.TotalAmount);
}

/// <summary>同步結果：寫入幾筆打卡明細，以及重算後該員工該月薪酬表的行（按職位 × 地盤分組，可能多行）。</summary>
public sealed record CardSyncResultDto(Guid ScanId, int Year, int Month, Guid WorkerId, string WorkerName,
    Guid SiteDictId, int AddedRecords, List<CardPayrollRowDto> Rows);
