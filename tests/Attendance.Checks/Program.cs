using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using Azure.AI.DocumentIntelligence;
using MC.Erp.Common;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Features.Attendance.Endpoints;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Attendance.Services;
using MC.Erp.Features.System.Entities;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

AttendanceCard Parse(string json, double threshold = AzureDocumentOcrService.ConfidenceThreshold) =>
    AttendanceCardParser.Parse(ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(json))
        ?? throw new InvalidOperationException("Cannot read analyze result"), threshold);

int At(string content, string text)
{
    var at = content.IndexOf(text, StringComparison.Ordinal);
    return at >= 0 ? at : throw new InvalidOperationException($"{text} not found in fixture content");
}

// 用真實的 prebuilt-layout 回應形狀：格子只有 content / spans，置信度要從 words 的 span 對回來。
// 所有 offset 都由 content 的實際位置算出，避免手算錯位。
string Card(string content, (int Row, int Column, int Span, string Text)[] cells,
    params (string Text, float Confidence)[] words)
{
    var wordJson = words.Select(word =>
        $$"""{"content":"{{word.Text}}","span":{"offset":{{At(content, word.Text)}},"length":{{word.Text.Length}}},"confidence":{{word.Confidence.ToString(CultureInfo.InvariantCulture)}},"polygon":[0,0,1,0,1,1,0,1]}""");
    var cellJson = cells.Select(cell =>
        $$"""{"kind":"content","rowIndex":{{cell.Row}},"columnIndex":{{cell.Column}},"rowSpan":1,"columnSpan":{{cell.Span}},"content":"{{cell.Text}}","spans":[{"offset":{{At(content, cell.Text)}},"length":{{cell.Text.Length}}}],"boundingRegions":[]}""");
    return $$"""
{"apiVersion":"2024-11-30","modelId":"prebuilt-layout","stringIndexType":"utf16CodeUnit","content":"{{content}}",
"pages":[{"pageNumber":1,"angle":0,"width":8.27,"height":11.69,"unit":"inch","words":[{{string.Join(",", wordJson)}}]}],
"tables":[{"rowCount":{{cells.Max(cell => cell.Row) + 1}},"columnCount":{{cells.Max(cell => cell.Column + cell.Span)}},"cells":[{{string.Join(",", cellJson)}}]}]}
""";
}

// 實測那張卡：表頭「日期」+ 紅字印刷時間（打卡鐘印的是「日號 + 時間」）＋頁腳干擾行
var cardText = "考勤表 2026年10月份 姓名 朱駿豪 日期 上午 下午 加班 05 0509:07 0517:05 長 231 307";
var full = Parse(Card(cardText,
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"), (0, 2, 1, "下午"), (0, 3, 1, "加班"),
    (1, 0, 1, "05"), (1, 1, 1, "0509:07"), (1, 2, 1, "0517:05"),
    (2, 0, 1, "長"), (2, 1, 1, "231"), (2, 2, 1, "307")
],
("0509:07", 0.95f), ("0517:05", 0.62f)));

Check(full.Records.Count == 1, "Header and footer rows are not turned into days");
Check(full.Records[0].Day == 5, "Day comes from the date column");
Check(full.Records[0].AmIn.Value == "09:07", "Day prefix 0509:07 becomes 09:07");
Check(full.Records[0].AmOut.Value == "17:05", "Second time column is the morning out");
Check(full.Records[0].AmIn.Confidence == 0.95f, "Confidence comes from the words inside the cell");
Check(full.Records[0].NeedsReview && full.Records[0].ReviewFields.SequenceEqual(["amOut"]),
    "Low-confidence column is the only one flagged");
Check(full.Records[0] is { PmIn.Value: null, PmOut.Value: null, OtIn.Value: null, OtOut.Value: null },
    "Blank cells stay null instead of defaulting");
Check(full.Year.Value == 2026 && full.Month.Value == 10, "Handwritten year and month are read from the text");
Check(full.Name.Value == "朱駿豪", "Handwritten name is read after the 姓名 label");
Check(full.ReviewFields.Count == 0,
    "Handwritten fields no longer hold the card in review (confidence still reported per field)");
Check(full.NeedsReview, "A low-confidence printed time still flags the card, and its row");

// 一個格子裡出現兩個時間，代表它橫跨上班 / 下班兩欄
var merged = Parse(Card("日期 上午 下午 05 0509:07 0517:05",
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"), (0, 2, 1, "下午"),
    (1, 0, 1, "05"), (1, 1, 2, "0509:07 0517:05")
],
("0509:07", 0.95f), ("0517:05", 0.94f)));
Check(merged.Records[0] is { AmIn.Value: "09:07", AmOut.Value: "17:05" },
    "Two times in one cell split into clock-in and clock-out");
Check(merged.Records[0].NeedsReview == false, "Clear printed times need no review");

// 1009:05 是「10 日 09:05」，不是 10 點
var doubleDigitDay = Parse(Card("日期 上午 10 1009:05",
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"), (1, 0, 1, "10"), (1, 1, 1, "1009:05")
], ("1009:05", 0.96f)));
Check(doubleDigitDay.Records[0].Day == 10, "Two-digit day is not confused with the hour");
Check(doubleDigitDay.Records[0].AmIn.Value == "09:05", "Two-digit day prefix is dropped from the time");

// 小計欄有數字（或看起來像時間）都不算打卡
var subtotal = Parse(Card("日期 上午 05 0509:07 2312:30",
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"),
    (1, 0, 1, "05"), (1, 1, 1, "0509:07"), (1, 7, 1, "2312:30")
], ("0509:07", 0.95f), ("2312:30", 0.95f)));
Check(subtotal.Records[0] is { AmIn.Value: "09:07", OtOut.Value: null },
    "Columns after the six time columns are ignored");

// 休息日只有日號沒有時間，仍要保留一列，否則前端對不上日期
var restDay = Parse(Card("日期 上午 下午 03",
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"), (0, 2, 1, "下午"), (1, 0, 1, "03")
]));
Check(restDay.Records.Count == 1 && restDay.Records[0].Day == 3, "A day with no punches is kept");
Check(restDay.Records[0] is { AmIn.Value: null, OtOut.Value: null } && !restDay.Records[0].NeedsReview,
    "An empty day is not flagged as low confidence");

// 表頭漏檢時退回按閱讀順序填欄，並要求整張卡人工核對
var noHeader = Parse(Card("上午 下午 0509:07 0517:05",
[
    (0, 0, 1, "上午"), (0, 1, 1, "下午"), (1, 0, 1, "0509:07"), (1, 1, 1, "0517:05")
], ("0509:07", 0.95f), ("0517:05", 0.95f)));
Check(noHeader.Records[0] is { Day: 5, AmIn.Value: "09:07", AmOut.Value: "17:05" },
    "Without a date header the times are filled in reading order");
Check(noHeader.ReviewFields.Contains("layout"), "A missing table header is flagged for review");

// 沒有表格就不猜
var noTable = false;
try
{
    Parse("""
    {"apiVersion":"2024-11-30","modelId":"prebuilt-layout","stringIndexType":"utf16CodeUnit","content":"","pages":[],"tables":[]}
    """);
}
catch (InvalidOperationException) { noTable = true; }
Check(noTable, "An image without a table is rejected instead of guessed");

// 姓名在右邊的格子時用它自己的置信度
var named = Parse(Card("姓名 朱駿豪 日期 05 0509:07",
[
    (0, 0, 1, "姓名"), (0, 1, 1, "朱駿豪"), (1, 0, 1, "日期"), (2, 0, 1, "05"), (2, 1, 1, "0509:07")
], ("朱駿豪", 0.93f), ("0509:07", 0.95f)));
Check(named.Name is { Value: "朱駿豪", Confidence: 0.93f }, "Name uses the confidence of its own cell");
Check(!named.ReviewFields.Contains("name"), "A confident name is not flagged");

// 印刷清晰的年月不需要複核
var printedMonth = Parse(Card("2026年10月份 日期 05 0509:07",
[
    (0, 0, 1, "日期"), (1, 0, 1, "05"), (1, 1, 1, "0509:07")
], ("2026", 0.95f), ("10", 0.95f), ("0509:07", 0.95f)));
Check(printedMonth.Year is { Value: 2026, Confidence: 0.95f } && printedMonth.Month is { Value: 10, Confidence: 0.95f },
    "Year and month carry their own confidence");
Check(!printedMonth.ReviewFields.Contains("year") && !printedMonth.ReviewFields.Contains("month"),
    "A confident year and month are not flagged");

// 閾值可調
var relaxed = Parse(Card(cardText,
[
    (0, 0, 1, "日期"), (0, 1, 1, "上午"), (0, 2, 1, "下午"), (0, 3, 1, "加班"),
    (1, 0, 1, "05"), (1, 1, 1, "0509:07"), (1, 2, 1, "0517:05")
],
("0509:07", 0.95f), ("0517:05", 0.62f)), 0.5);
Check(!relaxed.Records[0].NeedsReview, "Threshold is configurable per call");

// 落庫再讀回來的形狀要和掃描結果一致
var stored = AttendanceCardScanDto.From(new AttendanceCardScan
{
    Year = 2026,
    YearConfidence = 0.9f,
    Month = 10,
    MonthConfidence = 0.55f,
    Name = "朱駿豪",
    NameConfidence = 0.31f,
    ReviewFields = ["month", "name"],
    Punches =
    [
        new AttendanceCardPunch
        {
            Day = 5, AmIn = new TimeOnly(9, 7), AmInConfidence = 0.95f,
            AmOut = new TimeOnly(17, 5), AmOutConfidence = 0.62f
        },
        new AttendanceCardPunch { Day = 3 }
    ]
});
Check(stored.Card is { Year.Value: 2026, Month.Value: 10 }, "Year and month survive the database round-trip");
Check(stored.Card.Records.Select(row => row.Day).SequenceEqual([3, 5]), "Rows come back in day order");
Check(stored.Card.Records[1] is { AmIn.Value: "09:07", AmOut.Value: "17:05" }, "Times are formatted back to HH:mm");
Check(stored.Card.Records[1].ReviewFields.SequenceEqual(["amOut"]), "Row-level review flags are recomputed on read");
Check(stored.Card.Records[0].NeedsReview == false, "An empty day stays clean after a round-trip");
Check(stored.Card.NeedsReview, "The card still needs review after a round-trip");
var summary = AttendanceCardScanSummaryDto.From(new AttendanceCardScan
{
    Year = 2026,
    Month = 10,
    ReviewFields = ["month"],
    Punches = [new AttendanceCardPunch { Day = 5, OtIn = new TimeOnly(19, 0), OtInConfidence = 0.5f }]
});
Check(summary is { PunchCount: 1, NeedsReview: true }, "Summary counts rows and keeps low-confidence flags");
Check(AttendanceCardScanSummaryDto.From(new AttendanceCardScan { Punches = [new AttendanceCardPunch { Day = 3 }] }).PunchCount == 0,
    "A day with every time cleared is not counted as a punch day");

// 列表要靠核對時間分「待核對」和「已核對」；同步也以這個時間為前提
var confirmedAt = new DateTime(2026, 11, 22, 9, 30, 0, DateTimeKind.Utc);
Check(AttendanceCardScanSummaryDto.From(new AttendanceCardScan { ConfirmedAt = confirmedAt }).ConfirmedAt == confirmedAt,
    "The summary carries the confirmation time so the list can tell 待核對 from 已核對");
Check(AttendanceCardScanSummaryDto.From(new AttendanceCardScan()).ConfirmedAt is null,
    "A freshly scanned card has no confirmation time until it is marked checked");

// 人工修正：只覆寫改動的欄位，改過的記人工確認，沒改的保留原識別置信度
var corrected = new AttendanceCardScan
{
    Year = 2026,
    YearConfidence = 0.4f,
    Month = 9,
    MonthConfidence = 0.4f,
    Name = "朱駿豪",
    NameConfidence = 0.9f,
    ReviewFields = ["layout", "year", "month"],
    Punches = [new AttendanceCardPunch { Day = 5, AmIn = new TimeOnly(9, 7), AmInConfidence = 0.5f }]
};
new UpdateCardScanRequest(2026, 10, "朱駿豪",
    [new UpdateCardPunch(5, "09:10", "17:05", null, null, null, null)]).ApplyTo(corrected);
Check(corrected is { Month: 10, MonthConfidence: 1f }, "A corrected month is stored as manually confirmed");
Check(corrected is { YearConfidence: 0.4f, NameConfidence: 0.9f }, "Untouched fields keep their OCR confidence");
Check(corrected.ReviewFields.SequenceEqual(["layout"]),
    "Handwritten year/month/name never hold the card in review; layout stays because it cannot be recomputed");
Check(corrected.Punches[0] is { AmInConfidence: 1f } && corrected.Punches[0].AmIn == new TimeOnly(9, 10),
    "A corrected time is stored as manually confirmed");
Check(corrected.Punches[0].AmOut == new TimeOnly(17, 5), "A new time fills the empty column of that day");

// 清空時間、補一日、清空姓名
new UpdateCardScanRequest(2026, 10, "",
[
    new UpdateCardPunch(5, "", null, null, null, null, null),
    new UpdateCardPunch(6, "08:00", null, null, null, null, null)
]).ApplyTo(corrected);
Check(corrected.Punches[0] is { AmIn: null, AmInConfidence: null }, "An empty time clears the column and its confidence");
Check(corrected.Punches[1] is { Day: 6 } && corrected.Punches[1].AmIn == new TimeOnly(8, 0),
    "A day that was not on the scan can be added by hand");
Check(corrected is { Name: null, NameConfidence: null }, "An empty name is cleared instead of stored as an empty string");
Check(corrected.ReviewFields.SequenceEqual(["layout"]),
    "A cleared handwritten field is not treated as needing review either");

// 勾了「一併檢查手寫欄位」才把低置信度、漏讀的手寫欄拉回複核
var strict = new AttendanceCardScan
{
    Year = 2026, YearConfidence = 0.4f, Month = 10, MonthConfidence = 1f,
    Name = "朱駿豪", NameConfidence = 0.7f, ReviewFields = ["layout"]
};
Check(CardReviewRules.ReviewFieldsOf(strict, includeHandwritten: true).SequenceEqual(["layout", "year", "name"]),
    "Asking to include handwritten fields puts the low-confidence ones back in review");
Check(CardReviewRules.ReviewFieldsOf(strict, includeHandwritten: false).SequenceEqual(["layout"]),
    "By default handwritten fields stay out of review");

// 人工標記為已核對：卡片層標記清空，時間欄不動（先用 IncludeHandwritten 檢查一次，再按「標記為已核對」清掉）
new UpdateCardScanRequest(2026, 10, null, [], IncludeHandwritten: true).ApplyTo(corrected);
Check(corrected.ReviewFields.SequenceEqual(["layout", "year", "name"]),
    "Checking the handwritten switch flags only the still-unconfirmed handwritten fields");
var confirmedBefore = corrected.Punches[1].AmIn;
corrected.ReviewFields = [];
Check(corrected.ReviewFields.Count == 0 && corrected.Punches[1].AmIn == confirmedBefore,
    "Confirming clears the card flags without touching the punch times");

// 保存沒有改動時不算改動，已核對的卡不會被白白要求重新核對；真的改了就要重新核對
Check(!new UpdateCardScanRequest(2026, 10, null, []).ApplyTo(corrected),
    "Saving without changes keeps an existing confirmation valid");
Check(new UpdateCardScanRequest(2026, 10, null,
        [new UpdateCardPunch(6, "08:05", null, null, null, null, null)]).ApplyTo(corrected),
    "Changing a time invalidates the confirmation so it has to be checked again");

var badTime = false;
try
{
    new UpdateCardScanRequest(2026, 10, null, [new UpdateCardPunch(5, "9.10", null, null, null, null, null)])
        .ApplyTo(corrected);
}
catch (InvalidOperationException) { badTime = true; }
Check(badTime, "A time that is not HH:mm is rejected instead of silently dropped");

// --- 考勤卡 → 打卡明細 ---
var workerId = Guid.NewGuid();
var siteDictId = Guid.NewGuid();
var worked = new AttendanceCardPunch
{
    Day = 5, AmIn = new TimeOnly(9, 7), AmOut = new TimeOnly(12, 0),
    PmIn = new TimeOnly(13, 0), PmOut = new TimeOnly(17, 5),
    OtIn = new TimeOnly(18, 0), OtOut = new TimeOnly(20, 30)
};
Check(AttendanceRules.HasTime(worked), "A day with any punched time counts as worked");
Check(!AttendanceRules.HasTime(new AttendanceCardPunch { Day = 5 }), "A rest day with no time at all is not a work day");

var cardRecord = AttendanceRules.ToRecord(2026, 10, worked, workerId, siteDictId);
Check(cardRecord.ClockInTime == new DateTime(2026, 10, 5, 1, 7, 0, DateTimeKind.Utc),
    "09:07 on the card (Hong Kong) is stored as 01:07 UTC");
Check(cardRecord.ClockOutTime == new DateTime(2026, 10, 5, 9, 5, 0, DateTimeKind.Utc),
    "17:05 on the card (Hong Kong) is stored as 09:05 UTC");
Check(cardRecord.OvertimeHours == 2.5m, "Overtime is the gap between the overtime punches");
Check(cardRecord is { IsLaborLeave: false, Remark: AttendanceRules.ImportedRemark },
    "Imported rows are marked as card imports and are never labour leave");
Check(cardRecord.WorkerId == workerId && cardRecord.SiteDictId == siteDictId, "Imported rows are linked to the chosen worker and site");

var midnightRecord = AttendanceRules.ToRecord(2026, 10,
    new AttendanceCardPunch { Day = 31, AmIn = new TimeOnly(22, 0), AmOut = new TimeOnly(1, 30) }, workerId, siteDictId);
Check(midnightRecord.ClockOutTime == midnightRecord.ClockInTime.AddHours(3).AddMinutes(30),
    "An out time earlier than the in time is treated as after midnight");
Check(AttendanceRules.OvertimeHoursOf(new AttendanceCardPunch { OtIn = new TimeOnly(22, 0), OtOut = new TimeOnly(1, 0) }) == 3m,
    "Overtime that runs past midnight is counted forward");
Check(AttendanceRules.OvertimeHoursOf(new AttendanceCardPunch { OtIn = new TimeOnly(18, 0) }) == 0m,
    "A half-entered overtime pair is not counted");

// --- 打卡明細 → 薪酬表 ---
Check(AttendanceRules.OvertimeRateOf(900m) == 168.75m, "Overtime rate is daily wage / 8 * 1.5");
var payroll = new MonthlyWorkSummary { Year = 2026, Month = 10, LaborLeaveDays = 1m, Remark = "人工備註" };
AttendanceRules.ApplyPayroll(payroll, 900m, 22, 6.5m);
Check(payroll is { WorkDays: 22m, TotalWorkDays: 23m, OvertimeHours: 6.5m, DailyWage: 900m },
    "Work days, total work days (with labour leave kept), overtime hours and daily wage are filled in");
Check(payroll.OvertimeAmount == 1096.88m, "Overtime amount is hours * rate, rounded to 2 decimals");
Check(payroll is { MpfEmployer: 1150m, MpfEmployee: 1150m }, "MPF is 50 per total work day on both sides");
Check(payroll.ActualPay == 20646.88m, "Actual pay is daily wage * total work days - employee MPF + overtime");
Check(payroll.TotalAmount == 22946.88m, "Total is employer MPF + employee MPF + actual pay");
Check(payroll.Remark == "人工備註", "Recomputing keeps the manual remark");

var counted = new List<AttendanceRecord>
{
    new() { ClockInTime = DateTime.UtcNow },
    new() { ClockInTime = DateTime.UtcNow },
    new() { ClockInTime = DateTime.UtcNow, IsLaborLeave = true }
};
Check(AttendanceRules.WorkDaysOf(counted) == 2, "Work days count every punch row that is not labour leave");

var (monthStart, monthEnd) = AttendanceRules.MonthRange(2026, 10);
Check(monthStart == new DateTime(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc)
    && monthEnd == new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc),
    "A Hong Kong month maps to the UTC range shared with the attendance tables");

// --- 列表合計（totals）---
// 合計跟 items 一起回，按整份篩選結果算；日薪是每人單價，不能出現在合計裡。
var totalsWorker = new Worker { Name = "陳大文", EmployeeId = "W001", DailyWage = 900m };
var floorPosition = new SysDict { GroupName = "AttendancePosition", Value = "floor", Label = "樓面員工" };
var payrollPage = new PagedResult<PayrollDto, PayrollTotals>(
    [PayrollDto.From(new MonthlyWorkSummary
    {
        Worker = totalsWorker, Year = 2026, Month = 10, WorkDays = 22m, DailyWage = 900m,
        PositionDictId = floorPosition.Id, Position = floorPosition.Label
    })],
    2, 1, 1, new PayrollTotals(22m, 1m, 23m, 6.5m, 1096.88m, 1150m, 1150m, 20646.88m, 22946.88m));
Check(payrollPage.Items[0].Position == "樓面員工" && payrollPage.Items[0].PositionId == floorPosition.Id,
    "Payroll keeps the position category recorded with that month");
var workerDto = WorkerDto.From(new Worker
{
    Name = totalsWorker.Name, DailyWage = totalsWorker.DailyWage,
    PositionDictId = floorPosition.Id, Position = floorPosition
});
Check(workerDto.Position == "樓面員工" && workerDto.PositionId == floorPosition.Id,
    "Worker keeps the optional position category");
using var totalsJson = JsonDocument.Parse(JsonSerializer.Serialize(payrollPage, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
var totalsNode = totalsJson.RootElement.GetProperty("totals");
Check(totalsNode.GetProperty("workDays").GetDecimal() == 22m && totalsNode.GetProperty("totalAmount").GetDecimal() == 22946.88m,
    "Payroll totals are serialized next to items and cover the whole filtered result");
Check(!totalsNode.TryGetProperty("dailyWage", out _),
    "Payroll totals never add up the per-person daily wage");

Console.WriteLine($"PASS: {checks} attendance checks (no Azure calls)");
