using MC.Erp.Features.Attendance.Entities;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>
/// 考勤卡 → 打卡明細 → 薪酬表 的換算規則。全部是純函數、不碰資料庫，方便直接驗證。
/// 日後要改工數算法（例如改成半天 0.5 工）、OT 倍率或 MPF 金額，只需要改這裡。
/// </summary>
public static class AttendanceRules
{
    /// <summary>OT 時薪倍率：時薪 × 1.5。</summary>
    public const decimal OvertimeRatio = 1.5m;

    /// <summary>一天工時，用來把日薪換成時薪。</summary>
    public const decimal HoursPerDay = 8m;

    /// <summary>每一個總工數的 MPF 供款，僱主和僱員各一份。</summary>
    public const decimal MpfPerWorkDay = 50m;

    /// <summary>由考勤卡導入的打卡明細都會帶這個備註，方便在打卡明細列表上認出來源。</summary>
    public const string ImportedRemark = "考勤卡導入";

    /// <summary>香港時區偏移。資料庫存 UTC，而卡上印的是香港時間。</summary>
    private static readonly TimeSpan HongKongOffset = TimeSpan.FromHours(8);

    /// <summary>六個時間欄位全空＝當天沒上班（休息日），不會產生打卡明細。</summary>
    public static bool HasTime(AttendanceCardPunch punch) =>
        punch is not { AmIn: null, AmOut: null, PmIn: null, PmOut: null, OtIn: null, OtOut: null };

    /// <summary>
    /// 一列明細換成一筆打卡記錄：上班取上午上班（缺就用下午上班），下班取下午下班（缺就用上午下班）。
    /// 卡上是香港時間，存庫一律換成 UTC；下班早於上班代表跨過午夜，往後推一天。
    /// 加班時數另計到 <see cref="AttendanceRecord.OvertimeHours"/>，不影響上下班時間。
    /// </summary>
    public static AttendanceRecord ToRecord(int year, int month, AttendanceCardPunch punch, Guid workerId, Guid siteId)
    {
        var start = punch.AmIn ?? punch.PmIn ?? punch.OtIn
            ?? throw new InvalidOperationException($"第 {punch.Day} 日沒有任何打卡時間");
        var end = punch.PmOut ?? punch.AmOut ?? punch.OtOut;

        var clockIn = HongKong(year, month, punch.Day, start);
        DateTime? clockOut = end is { } value ? HongKong(year, month, punch.Day, value) : null;
        // 上班時間比下班時間晚，只可能是跨過午夜（例如加班到凌晨），加一天才符合「下班晚於上班」
        if (clockOut is { } endTime && endTime < clockIn) clockOut = endTime.AddDays(1);

        return new AttendanceRecord
        {
            WorkerId = workerId,
            SiteId = siteId,
            ClockInTime = clockIn,
            ClockOutTime = clockOut,
            OvertimeHours = OvertimeHoursOf(punch),
            // 考勤卡只有打卡時間，沒有假期資訊，所以導入的明細一律不是勞工假
            IsLaborLeave = false,
            Remark = ImportedRemark
        };
    }

    /// <summary>
    /// 加班時數＝加班下班 − 加班上班。只有一邊就當沒有加班；
    /// 下班早於上班代表加班到凌晨，加 24 小時補回來。
    /// </summary>
    public static decimal OvertimeHoursOf(AttendanceCardPunch punch)
    {
        if (punch.OtIn is not { } start || punch.OtOut is not { } end) return 0m;
        var span = end.ToTimeSpan() - start.ToTimeSpan();
        if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1);
        return Math.Round((decimal)span.TotalHours, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 工數：目前「一天一筆打卡明細 = 1 工」。要改成半天 0.5 工或按工時折算時，只改這個方法。
    /// </summary>
    public static int WorkDaysOf(IEnumerable<AttendanceRecord> records) =>
        records.Count(record => !record.IsLaborLeave);

    /// <summary>OT 時薪＝日薪 ÷ 8 × 1.5（日薪 900 → 168.75）。</summary>
    public static decimal OvertimeRateOf(decimal dailyWage) =>
        Money(dailyWage / HoursPerDay * OvertimeRatio);

    /// <summary>
    /// 按打卡明細重算薪酬表的計算欄位：工數、總工數、O.T.、MPF、實付人工、總數。
    /// 勞工假不在考勤卡上，所以不覆寫，用薪酬表原有的值；Remark 也保持不變。
    /// 公式：總工數 = 工數 + 勞工假；OT 金額 = O.T. 小時 × OT 時薪；
    /// MPF = 總工數 × 50（僱主、僱員各一份）；
    /// 實付人工 = 日薪 × 總工數 − MPF（僱員）+ OT 金額；
    /// 總數 = MPF（僱主）+ MPF（僱員）+ 實付人工。
    /// </summary>
    public static void ApplyPayroll(MonthlyWorkSummary summary, decimal dailyWage, int workDays, decimal overtimeHours)
    {
        summary.DailyWage = Money(dailyWage);
        summary.WorkDays = workDays;
        summary.TotalWorkDays = workDays + summary.LaborLeaveDays;
        summary.OvertimeHours = Money(overtimeHours);
        summary.OvertimeAmount = Money(summary.OvertimeHours * OvertimeRateOf(summary.DailyWage));
        summary.MpfEmployer = Money(summary.TotalWorkDays * MpfPerWorkDay);
        summary.MpfEmployee = summary.MpfEmployer;
        summary.ActualPay = Money(summary.DailyWage * summary.TotalWorkDays - summary.MpfEmployee + summary.OvertimeAmount);
        summary.TotalAmount = summary.MpfEmployer + summary.MpfEmployee + summary.ActualPay;
    }

    /// <summary>某個香港月份對應的 UTC 時間範圍（含下界、不含上界），打卡明細和薪酬查詢都用它。</summary>
    public static (DateTime Start, DateTime End) MonthRange(int year, int month)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (first - HongKongOffset, first.AddMonths(1) - HongKongOffset);
    }

    /// <summary>把卡上的香港時間（年月日 + 時分）換成 UTC。</summary>
    private static DateTime HongKong(int year, int month, int day, TimeOnly time) =>
        new DateTime(year, month, day, time.Hour, time.Minute, 0, DateTimeKind.Utc) - HongKongOffset;

    /// <summary>金額一律四捨五入到 2 位小數，和資料庫的 decimal(18,2) 對齊。</summary>
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
