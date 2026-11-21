using System.Globalization;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;

namespace MC.Erp.Common.Infrastructure.Ocr;

/// <summary>
/// 考勤卡解析：prebuilt-layout 只回表格格子，格子本身沒有置信度（SDK 的 <c>DocumentTableCell</c>
/// 只有 content / spans / boundingRegions），所以按 RowIndex / ColumnIndex 還原行列，
/// 再用格子的 Spans 對回逐字置信度並取最低值。
/// </summary>
public static class AttendanceCardParser
{
    /// <summary>卡片固定的 6 個時間欄：上午上班 / 下班、下午上班 / 下班、加班上班 / 下班。</summary>
    private const int TimeColumns = 6;

    /// <summary>欄位名，與前端同名。</summary>
    private static readonly string[] TimeFields = ["amIn", "amOut", "pmIn", "pmOut", "otIn", "otOut"];

    /// <summary>
    /// 打卡鐘印的是「日號 + 時間」（0509:07 = 05 日 09:07），正則從左掃，只會匹配到 09:07，
    /// 不會把 50 當小時；秒數（07:16:00）一律丟掉，只留 HH:mm。
    /// </summary>
    private static readonly Regex ClockTime = new(@"(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.CultureInvariant);

    /// <summary>格子開頭的兩位日號，用來在日期欄被切歪時兜底。</summary>
    private static readonly Regex DayPrefix = new(@"^(?<day>\d{1,2})(?<hour>\d{2}):(?<minute>\d{2})", RegexOptions.CultureInvariant);

    private static readonly Regex YearMonth = new(@"(?<year>20\d{2})\s*年", RegexOptions.CultureInvariant);
    private static readonly Regex MonthLabel = new(@"(?<month>0?[1-9]|1[0-2])\s*月", RegexOptions.CultureInvariant);
    private static readonly Regex NameAfterLabel = new(@"姓名\s*[:：]?\s*(?<name>[\u4e00-\u9fa5]{2,4})", RegexOptions.CultureInvariant);

    private static readonly Slot Empty = new(null, null);

    private readonly record struct Slot(string? Value, float? Confidence);
    private sealed record Cell(int Row, int Column, int Span, string Content, float? Confidence);

    /// <summary>解析一張考勤卡；沒有任何表格時拋出，代表圖片不是打卡卡或拍得太斜。</summary>
    public static AttendanceCard Parse(AnalyzeResult result, double threshold = AzureDocumentOcrService.ConfidenceThreshold)
    {
        // 一張卡常被切成多個表格（標題、姓名欄、主網格），格子最多的那張才是打卡網格
        var table = result.Tables
            .OrderByDescending(item => item.Cells.Count).ThenByDescending(item => item.RowCount).FirstOrDefault()
            ?? throw new InvalidOperationException("圖片中找不到表格結構，請整張平拍後重試");
        var content = result.Content ?? "";
        var words = result.Pages.SelectMany(page => page.Words).ToList();
        var rows = table.Cells
            .Select(cell => new Cell(cell.RowIndex, cell.ColumnIndex, Math.Max(cell.ColumnSpan ?? 1, 1),
                (cell.Content ?? "").Trim(), CellConfidence(cell, words)))
            .GroupBy(cell => cell.Row)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(cell => cell.Column).ToList())
            .ToList();

        var headerRow = rows.FindIndex(row => row.Any(cell => cell.Content.Contains("日期", StringComparison.Ordinal)));
        var dayColumn = headerRow < 0
            ? -1
            : rows[headerRow].First(cell => cell.Content.Contains("日期", StringComparison.Ordinal)).Column;

        var year = YearField(content, words);
        var month = MonthField(content, words);
        var name = Name(rows, content, words);
        var review = new List<string>();
        // 表頭被切歪或漏檢：退化為按閱讀順序填欄，整張卡都要人工核對
        if (headerRow < 0) review.Add("layout");
        Flag(review, "year", year, threshold);
        Flag(review, "month", month, threshold);
        Flag(review, "name", name, threshold);

        var records = new List<AttendanceRow>();
        for (var index = headerRow + 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (DayOf(row, dayColumn) is not { } day) continue;
            var slots = Slots(row, dayColumn);
            // 沒有表頭時只收有打卡的列，避免把「2026 年 10 月」那行當成 10 日
            if (dayColumn < 0 && slots.All(slot => slot.Value is null)) continue;
            records.Add(BuildRow(day, slots, threshold));
        }
        return new AttendanceCard(year, month, name, records, review);
    }

    /// <summary>把整列格子按欄位位置還原成 6 個時間欄。</summary>
    private static Slot[] Slots(List<Cell> row, int dayColumn)
    {
        var slots = Enumerable.Repeat(Empty, TimeColumns).ToArray();
        var next = 0;
        foreach (var cell in row)
        {
            var found = TimesIn(cell.Content);
            if (found.Count == 0) continue;
            // 日期欄可能跟第一個時間印在同一格，這種情況時間算上班
            var offset = dayColumn >= 0 ? Math.Max(cell.Column - dayColumn - 1, 0) : next;
            if (offset >= TimeColumns) break;   // 小計欄及之後不屬於打卡時間
            // 一格出現兩個時間（"09:07 17:05"）代表它橫跨上班 / 下班兩欄
            for (var index = 0; index < found.Count; index++)
            {
                var target = offset + index;
                if (target >= TimeColumns) break;
                slots[target] = new Slot(found[index], cell.Confidence);
            }
            if (dayColumn < 0) next = Math.Min(offset + found.Count, TimeColumns);
        }
        return slots;
    }

    private static List<string> TimesIn(string content)
    {
        var times = new List<string>();
        foreach (Match match in ClockTime.Matches(content))
        {
            if (!int.TryParse(match.Groups["hour"].Value, out var hour) || hour > 23) continue;
            if (!int.TryParse(match.Groups["minute"].Value, out var minute) || minute > 59) continue;
            times.Add($"{hour:00}:{minute:00}");
        }
        return times;
    }

    /// <summary>日期欄優先；被合併或切歪時退回格子開頭的兩位日號。</summary>
    private static int? DayOf(List<Cell> row, int dayColumn)
    {
        var dayCell = dayColumn >= 0
            ? row.FirstOrDefault(cell => cell.Column <= dayColumn && cell.Column + cell.Span > dayColumn)
            : row.FirstOrDefault();
        return DayIn(dayCell?.Content)
            ?? row.Select(cell => DayIn(cell.Content)).FirstOrDefault(day => day is not null);
    }

    /// <summary>只有整格是 1–31，或「兩位日號 + 時間」才算日期，避免把標題、頁腳合計、年月當成日號。</summary>
    private static int? DayIn(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var text = content.Trim();
        if (text.Length <= 2 && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var plain)
            && plain is >= 1 and <= 31) return plain;
        var prefixed = DayPrefix.Match(text);
        if (!prefixed.Success) return null;
        var day = int.Parse(prefixed.Groups["day"].Value, CultureInfo.InvariantCulture);
        return day is >= 1 and <= 31 ? day : null;
    }

    private static AttendanceRow BuildRow(int day, Slot[] slots, double threshold)
    {
        var review = new List<string>();
        for (var index = 0; index < TimeColumns; index++)
        {
            // 空白是正常的（休息日、沒加班），只有讀到值卻不確定才要人工核對
            if (slots[index].Value is null) continue;
            if (slots[index].Confidence is not { } confidence || confidence < threshold) review.Add(TimeFields[index]);
        }
        return new AttendanceRow(day,
            Field(slots[0]), Field(slots[1]), Field(slots[2]),
            Field(slots[3]), Field(slots[4]), Field(slots[5]))
        { ReviewFields = review };
    }

    private static OcrField<string?> Field(Slot slot) => new(slot.Value, slot.Confidence);

    private static OcrField<int?> YearField(string content, IReadOnlyList<DocumentWord> words)
    {
        var match = YearMonth.Match(content);
        return match.Success ? Number(match, "year", words) : new OcrField<int?>(null, null);
    }

    private static OcrField<int?> MonthField(string content, IReadOnlyList<DocumentWord> words)
    {
        var match = MonthLabel.Match(content);
        return match.Success ? Number(match, "month", words) : new OcrField<int?>(null, null);
    }

    private static OcrField<int?> Number(Match match, string group, IReadOnlyList<DocumentWord> words)
    {
        var value = match.Groups[group];
        return new OcrField<int?>(int.Parse(value.Value, CultureInfo.InvariantCulture),
            ConfidenceIn(words, value.Index, value.Length));
    }

    /// <summary>「姓名」常與手寫名字合併成一格，也可能名字在右邊的下一格。</summary>
    private static OcrField<string?> Name(List<List<Cell>> rows, string content, IReadOnlyList<DocumentWord> words)
    {
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Count; index++)
            {
                if (!row[index].Content.Contains("姓名", StringComparison.Ordinal)) continue;
                foreach (var cell in row.Skip(index + 1))
                {
                    if (cell.Content.Length == 0 || cell.Content is "姓名" or "部門" or "職位") continue;
                    return new OcrField<string?>(cell.Content, cell.Confidence);
                }
                var inline = NameAfterLabel.Match(row[index].Content);
                if (inline.Success) return new OcrField<string?>(inline.Groups["name"].Value, row[index].Confidence);
            }
        }
        var match = NameAfterLabel.Match(content);
        return match.Success
            ? new OcrField<string?>(match.Groups["name"].Value,
                ConfidenceIn(words, match.Groups["name"].Index, match.Groups["name"].Length))
            : new OcrField<string?>(null, null);
    }

    private static float? CellConfidence(DocumentTableCell cell, IReadOnlyList<DocumentWord> words)
    {
        float? lowest = null;
        foreach (var span in cell.Spans)
        {
            if (ConfidenceIn(words, span.Offset, span.Length) is not { } confidence) continue;
            if (lowest is null || confidence < lowest) lowest = confidence;
        }
        return lowest;
    }

    /// <summary>取一段字元範圍內所有字的最低置信度；範圍內沒有字就回 null。</summary>
    private static float? ConfidenceIn(IReadOnlyList<DocumentWord> words, int start, int length)
    {
        if (length <= 0) return null;
        var end = start + length;
        float? lowest = null;
        foreach (var word in words)
        {
            if (word.Span.Offset < start || word.Span.Offset + word.Span.Length > end) continue;
            var confidence = (float)word.Confidence;
            if (lowest is null || confidence < lowest) lowest = confidence;
        }
        return lowest;
    }

    private static void Flag<T>(List<string> review, string field, OcrField<T> value, double threshold)
    {
        if (value.Value is null || value.Confidence is not { } confidence || confidence < threshold) review.Add(field);
    }
}
