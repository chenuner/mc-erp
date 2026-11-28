using System.ComponentModel.DataAnnotations;
using MC.Erp.Common;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Services;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Attendance.Endpoints;

public static class AttendanceTableEndpoints
{
    public static void MapAttendanceTableEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/payroll", GetPayrollAsync).RequireAuthorization();
        group.MapGet("/punches", GetPunchesAsync).RequireAuthorization();
        group.MapPut("/payroll/{id:guid}", UpdatePayrollAsync).RequireAuthorization("admin");
        group.MapPut("/punches/{id:guid}", UpdatePunchAsync).RequireAuthorization("admin");
    }

    private static bool ValidQuery(int year, int month, int page, int pageSize)
        => year is >= 2000 and <= 2099 && month is >= 1 and <= 12 && page is >= 1 and <= 100000 && pageSize is >= 1 and <= 100;

    // 合計（totals）按整份篩選結果算，跟是否分頁無關；前端合計行直接用，不要對當前頁加總。
    private static async Task<Results<Ok<PagedResult<PayrollDto, PayrollTotals>>, ValidationProblem>> GetPayrollAsync(
        int year, int month, AttendanceTableService svc, CancellationToken ct,
        Guid? workerId = null, Guid? positionId = null, Guid? siteDictId = null, int page = 1, int pageSize = 20)
    {
        if (!ValidQuery(year, month, page, pageSize)) return InvalidQuery();
        var result = await svc.GetPayrollAsync(year, month, workerId, positionId, siteDictId, page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<PayrollDto, PayrollTotals>(
            result.Items.Select(PayrollDto.From).ToList(), result.Total, page, pageSize, result.Totals));
    }

    private static async Task<Results<Ok<PagedResult<PunchDto, PunchTotals>>, ValidationProblem>> GetPunchesAsync(
        int year, int month, AttendanceTableService svc, CancellationToken ct, Guid? workerId = null, int page = 1, int pageSize = 20)
    {
        if (!ValidQuery(year, month, page, pageSize)) return InvalidQuery();
        var result = await svc.GetPunchesAsync(year, month, workerId, page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<PunchDto, PunchTotals>(
            result.Items.Select(PunchDto.From).ToList(), result.Total, page, pageSize, result.Totals));
    }

    private static ValidationProblem InvalidQuery() => TypedResults.ValidationProblem(new Dictionary<string, string[]>
    {
        ["query"] = ["年份須為 2000 至 2099，月份須為 1 至 12，頁碼須為 1 至 100000，每頁須為 1 至 100。"]
    });

    // DataAnnotations（[Range] / [MaxLength]）已由全域 AddValidation 驗證；這裡只補「最多兩位小數」規則。
    private static ValidationProblem? ValidateDecimals(object request)
    {
        var errors = new List<ValidationResult>();
        foreach (var property in request.GetType().GetProperties())
        {
            if (property.GetValue(request) is decimal value && decimal.Round(value, 2) != value)
                errors.Add(new ValidationResult("最多保留兩位小數。", [property.Name]));
        }
        if (errors.Count == 0) return null;
        return TypedResults.ValidationProblem(errors.SelectMany(error => error.MemberNames.DefaultIfEmpty("request")
            .Select(member => (member, message: error.ErrorMessage ?? "輸入無效")))
            .GroupBy(error => error.member).ToDictionary(group => group.Key, group => group.Select(error => error.message).ToArray()));
    }

    private static async Task<Results<Ok<PayrollDto>, NotFound, ValidationProblem>> UpdatePayrollAsync(
        Guid id, UpdatePayrollRequest request, AttendanceTableService svc, CancellationToken ct)
    {
        if (ValidateDecimals(request) is { } invalid) return invalid;
        var row = await svc.GetPayrollByIdAsync(id, ct);
        if (row is null) return TypedResults.NotFound();
        row.WorkDays = request.WorkDays;
        row.LaborLeaveDays = request.LaborLeaveDays;
        row.TotalWorkDays = request.TotalWorkDays;
        row.OvertimeHours = request.OvertimeHours;
        row.DailyWage = request.DailyWage;
        row.OvertimeAmount = request.OvertimeAmount;
        row.MpfEmployer = request.MpfEmployer;
        row.MpfEmployee = request.MpfEmployee;
        row.ActualPay = request.ActualPay;
        row.TotalAmount = request.TotalAmount;
        row.Remark = request.Remark;
        await svc.SaveAsync(ct);
        return TypedResults.Ok(PayrollDto.From(row));
    }

    /// <summary>
    /// 修正打卡時間、加班小時、勞工假、職位、地盤及備註。
    /// 只有參與計薪的欄位真的變動才重算薪酬（只改備註不觸發）；改到別的月份時兩個月都重算。
    /// </summary>
    private static async Task<Results<Ok<PunchUpdateResultDto>, NotFound, ValidationProblem>> UpdatePunchAsync(
        Guid id, UpdatePunchRequest request, AttendanceTableService svc, SysDictService dicts, CancellationToken ct)
    {
        if (ValidateDecimals(request) is { } invalid) return invalid;
        if (request.ClockInTime.ToOffset(TimeSpan.FromHours(8)).Year is < 2000 or > 2099)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["clockInTime"] = ["打卡年份須為 2000 至 2099。"] });
        if (request.ClockOutTime is { } end && end <= request.ClockInTime)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["clockOutTime"] = ["下班時間須晚於上班時間。"] });
        // 地盤和職位要存在且分組正確；停用的選項仍可保留在歷史明細上，所以不檢查 Enabled
        var site = await dicts.GetByIdAsync(request.SiteDictId, ct);
        if (site is null || site.GroupName != "AccountingSiteCategory")
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["siteDictId"] = ["請選擇有效的地盤分類。"] });
        SysDict? position = null;
        if (request.PositionDictId is { } positionId)
        {
            position = await dicts.GetByIdAsync(positionId, ct);
            if (position is null || position.GroupName != "AttendancePosition")
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["positionDictId"] = ["請選擇有效的職位分類。"] });
        }

        var row = await svc.GetPunchByIdAsync(id, ct);
        if (row is null) return TypedResults.NotFound();
        var before = PunchPayrollState.Of(row);
        row.ClockInTime = request.ClockInTime.UtcDateTime;
        row.ClockOutTime = request.ClockOutTime?.UtcDateTime;
        row.OvertimeHours = request.OvertimeHours;
        row.IsLaborLeave = request.IsLaborLeave;
        row.SiteDictId = site.Id;
        row.Site = site;
        row.PositionDictId = position?.Id;
        row.Position = position;
        row.Remark = request.Remark;
        await svc.SaveAsync(ct);
        var recomputed = await svc.RecomputeAfterPunchEditAsync(row.WorkerId, before, PunchPayrollState.Of(row), ct);
        return TypedResults.Ok(new PunchUpdateResultDto(PunchDto.From(row), recomputed.Count));
    }
}

/// <summary>員工薪酬表的一行。薪資按「員工 × 職位 × 地盤」分組，所以同一人同一月可能有多行。</summary>
public sealed record PayrollDto(Guid Id, Guid WorkerId, string Name, string? EmployeeId,
    Guid? PositionId, string? Position, Guid? SiteDictId, string? Site, int Year, int Month,
    decimal WorkDays, decimal LaborLeaveDays, decimal TotalWorkDays, decimal OvertimeHours, decimal DailyWage,
    decimal OvertimeAmount, decimal MpfEmployer, decimal MpfEmployee, decimal ActualPay, decimal TotalAmount, string? Remark)
{
    public static PayrollDto From(MonthlyWorkSummary row) => new(row.Id, row.WorkerId, row.Worker.Name, row.Worker.EmployeeId,
        row.PositionDictId, row.Position, row.SiteDictId, row.Site, row.Year, row.Month, row.WorkDays, row.LaborLeaveDays,
        row.TotalWorkDays, row.OvertimeHours, row.DailyWage, row.OvertimeAmount, row.MpfEmployer, row.MpfEmployee,
        row.ActualPay, row.TotalAmount, row.Remark);
}

/// <summary>打卡明細，時間帶 UTC 時區偏移；職位和地盤決定它在薪酬表歸到哪一行。字典名稱由前端按 id 映射。</summary>
public sealed record PunchDto(Guid Id, Guid WorkerId, string Name, string? EmployeeId,
    Guid SiteDictId, Guid? PositionDictId,
    DateTimeOffset ClockInTime, DateTimeOffset? ClockOutTime, decimal OvertimeHours, bool IsLaborLeave, string? Remark)
{
    public static PunchDto From(AttendanceRecord row) => new(row.Id, row.WorkerId, row.Worker.Name, row.Worker.EmployeeId,
        row.SiteDictId, row.PositionDictId,
        new DateTimeOffset(row.ClockInTime, TimeSpan.Zero),
        row.ClockOutTime is { } end ? new DateTimeOffset(end, TimeSpan.Zero) : null,
        row.OvertimeHours, row.IsLaborLeave, row.Remark);
}

/// <summary>修改打卡明細的結果；PayrollRows 是這次重算出來的薪酬行數，0 表示沒有涉及計薪的改動。</summary>
public sealed record PunchUpdateResultDto(PunchDto Punch, int PayrollRows);

/// <summary>人工維護薪酬欄位，不觸發自動計算。</summary>
public sealed record UpdatePayrollRequest
{
    [Range(typeof(decimal), "0", "999999.99")] public decimal WorkDays { get; init; }
    [Range(typeof(decimal), "0", "999999.99")] public decimal LaborLeaveDays { get; init; }
    [Range(typeof(decimal), "0", "999999.99")] public decimal TotalWorkDays { get; init; }
    [Range(typeof(decimal), "0", "999999.99")] public decimal OvertimeHours { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal DailyWage { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal OvertimeAmount { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal MpfEmployer { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal MpfEmployee { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal ActualPay { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal TotalAmount { get; init; }
    [MaxLength(255)] public string? Remark { get; init; }
}

/// <summary>修正打卡時間、加班小時、勞工假、職位、地盤及備註。</summary>
public sealed record UpdatePunchRequest
{
    public required DateTimeOffset ClockInTime { get; init; }
    public DateTimeOffset? ClockOutTime { get; init; }
    [Range(typeof(decimal), "0", "999999.99")] public decimal OvertimeHours { get; init; }
    public bool IsLaborLeave { get; init; }
    public required Guid SiteDictId { get; init; }
    public Guid? PositionDictId { get; init; }
    [MaxLength(255)] public string? Remark { get; init; }
}
