using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MC.Erp.Common;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Data;
using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Storage.Services;
using MC.Erp.Features.System.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Attendance.Services;

/// <summary>列表用的掃描摘要。</summary>
public sealed record AttendanceCardScanSummaryDto(Guid Id, int? Year, int? Month, string? Name, int PunchCount,
    bool NeedsReview, DateTime CreatedAt)
{
    // 打卡天數只算有時間的列，人工清空的列不算
    public static AttendanceCardScanSummaryDto From(AttendanceCardScan scan) => new(scan.Id, scan.Year, scan.Month,
        scan.Name, scan.Punches.Count(AttendanceRules.HasTime), AttendanceCardScanDto.CardOf(scan).NeedsReview,
        scan.CreatedAt);
}

/// <summary>一次掃描的完整結果；<see cref="Card"/> 的形狀與識別輸出一致，每欄帶置信度。</summary>
public sealed record AttendanceCardScanDto(Guid Id, Guid? FileItemId, string? FileName, AttendanceCard Card,
    DateTime CreatedAt)
{
    public static AttendanceCardScanDto From(AttendanceCardScan scan) => new(scan.Id, scan.FileItemId,
        scan.FileItem?.OriginalName, CardOf(scan), scan.CreatedAt);

    /// <summary>
    /// 由資料表還原掃描結果。卡級複核標記用存下來的那份（保住推不回來的 layout），
    /// 行級標記按目前閾值即時算——兩者用同一條低置信度規則。
    /// </summary>
    public static AttendanceCard CardOf(AttendanceCardScan scan) => new(
        new OcrField<int?>(scan.Year, scan.YearConfidence),
        new OcrField<int?>(scan.Month, scan.MonthConfidence),
        new OcrField<string?>(scan.Name, scan.NameConfidence),
        scan.Punches.OrderBy(punch => punch.Day).Select(RowOf).ToList(),
        scan.ReviewFields);

    private static AttendanceRow RowOf(AttendanceCardPunch punch)
    {
        var fields = new (string Name, TimeOnly? Value, float? Confidence)[]
        {
            ("amIn", punch.AmIn, punch.AmInConfidence),
            ("amOut", punch.AmOut, punch.AmOutConfidence),
            ("pmIn", punch.PmIn, punch.PmInConfidence),
            ("pmOut", punch.PmOut, punch.PmOutConfidence),
            ("otIn", punch.OtIn, punch.OtInConfidence),
            ("otOut", punch.OtOut, punch.OtOutConfidence)
        };
        return new AttendanceRow(punch.Day,
            new OcrField<string?>(Text(punch.AmIn), punch.AmInConfidence),
            new OcrField<string?>(Text(punch.AmOut), punch.AmOutConfidence),
            new OcrField<string?>(Text(punch.PmIn), punch.PmInConfidence),
            new OcrField<string?>(Text(punch.PmOut), punch.PmOutConfidence),
            new OcrField<string?>(Text(punch.OtIn), punch.OtInConfidence),
            new OcrField<string?>(Text(punch.OtOut), punch.OtOutConfidence))
        {
            // 空白是正常的（休息日、沒加班），只有讀到值卻不確定才算需複核
            ReviewFields = fields
                .Where(field => field.Value is not null
                    && (field.Confidence is not { } confidence || confidence < AzureDocumentOcrService.ConfidenceThreshold))
                .Select(field => field.Name).ToList()
        };
    }

    private static string? Text(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>人工修正識別結果的請求：年月姓名可留空，六個時間為 HH:mm、留空即清除該欄。</summary>
public sealed record UpdateCardScanRequest(
    [Range(2000, 2099)] int? Year,
    [Range(1, 12)] int? Month,
    [MaxLength(50)] string? Name,
    List<UpdateCardPunch> Records,
    bool IncludeHandwritten = false)
{
    /// <summary>人工確認過的欄位記 1，與識別置信度區分開。</summary>
    private const float Manual = 1f;

    /// <summary>
    /// 套用到已保存的掃描：只覆寫真正改動的欄位，改過的當作人工確認（置信度 1），
    /// 沒改的保留原本識別置信度——所以核對過的低置信度欄位才會從「需覆核」消失。
    /// <paramref name="IncludeHandwritten"/> 為 true 時（標記為已核對並一併檢查手寫欄位），
    /// 年份 / 月份 / 姓名也一起重新判定，使用者可以一次把整張卡核對完。
    /// </summary>
    public void ApplyTo(AttendanceCardScan scan)
    {
        if (Year != scan.Year)
        {
            scan.Year = Year;
            scan.YearConfidence = Confidence(Year);
        }
        if (Month != scan.Month)
        {
            scan.Month = Month;
            scan.MonthConfidence = Confidence(Month);
        }
        var name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        if (name != scan.Name)
        {
            scan.Name = name;
            scan.NameConfidence = Confidence(name);
        }
        foreach (var edit in Records) Apply(scan, edit);
        scan.ReviewFields = CardReviewRules.ReviewFieldsOf(scan, IncludeHandwritten);
    }

    private static void Apply(AttendanceCardScan scan, UpdateCardPunch edit)
    {
        var punch = scan.Punches.FirstOrDefault(item => item.Day == edit.Day);
        if (punch is null)
        {
            punch = new AttendanceCardPunch { Day = edit.Day };
            scan.Punches.Add(punch);
        }
        (punch.AmIn, punch.AmInConfidence) = Set(edit.AmIn, punch.AmIn, punch.AmInConfidence);
        (punch.AmOut, punch.AmOutConfidence) = Set(edit.AmOut, punch.AmOut, punch.AmOutConfidence);
        (punch.PmIn, punch.PmInConfidence) = Set(edit.PmIn, punch.PmIn, punch.PmInConfidence);
        (punch.PmOut, punch.PmOutConfidence) = Set(edit.PmOut, punch.PmOut, punch.PmOutConfidence);
        (punch.OtIn, punch.OtInConfidence) = Set(edit.OtIn, punch.OtIn, punch.OtInConfidence);
        (punch.OtOut, punch.OtOutConfidence) = Set(edit.OtOut, punch.OtOut, punch.OtOutConfidence);
    }

    /// <summary>沒改就保留原識別置信度；改了就記人工確認，清空則連置信度一併清掉。</summary>
    private static (TimeOnly? Time, float? Confidence) Set(string? text, TimeOnly? current, float? confidence)
    {
        var time = Parse(text);
        if (time == current) return (time, confidence);
        return (time, time is null ? null : Manual);
    }

    private static TimeOnly? Parse(string? text) => string.IsNullOrWhiteSpace(text)
        ? null
        : TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new InvalidOperationException($"「{text.Trim()}」不是 HH:mm 格式的時間");

    private static float? Confidence<T>(T value) => value is null ? null : Manual;
}

/// <summary>單日打卡修正，六個時間都可留空。</summary>
public sealed record UpdateCardPunch(
    [Range(1, 31)] int Day,
    string? AmIn,
    string? AmOut,
    string? PmIn,
    string? PmOut,
    string? OtIn,
    string? OtOut);

/// <summary>
/// 把整張卡標記為已核對（人工對照原卡看過）：清掉卡片層複核標記，並把每列時間欄的置信度記成人工確認，
/// 所以日期欄的紅字也一併放行，狀態由「需覆核」變「已識別」。時間值本身不改。
/// IncludeHandwritten 為 true 時連手寫的年月姓名一併判定，留着低置信度就仍然需覆核。
/// </summary>
public sealed record ConfirmCardScanRequest(bool IncludeHandwritten = false);

/// <summary>
/// 手寫欄位（年份 / 月份 / 姓名）預設不列入卡片層複核：它們的置信度普遍在 0.6–0.75，
/// 用印刷體的 0.80 去卡，每張卡都會一直掛「需覆核」。需要嚴格把關時用 <see cref="ConfirmCardScanRequest.IncludeHandwritten"/>
/// 或 <see cref="UpdateCardScanRequest.IncludeHandwritten"/> 把它們拉回複核。
/// </summary>
public static class CardReviewRules
{
    /// <summary>重算卡級複核標記；layout（表頭漏檢）推不回來，原樣保留。</summary>
    public static List<string> ReviewFieldsOf(AttendanceCardScan scan, bool includeHandwritten)
    {
        var fields = new List<string>();
        if (scan.ReviewFields.Contains("layout")) fields.Add("layout");
        if (!includeHandwritten) return fields;
        if (Unconfirmed(scan.Year, scan.YearConfidence)) fields.Add("year");
        if (Unconfirmed(scan.Month, scan.MonthConfidence)) fields.Add("month");
        if (Unconfirmed(scan.Name, scan.NameConfidence)) fields.Add("name");
        return fields;
    }

    /// <summary>沒讀到值，或讀到值但置信度低於閾值，都要人工核對（與識別時同一條規則）。</summary>
    private static bool Unconfirmed<T>(T value, float? confidence) =>
        value is null || confidence is not { } score || score < AzureDocumentOcrService.ConfidenceThreshold;
}

/// <summary>同步到打卡明細的目標：哪個員工、哪個地盤（卡上只有手寫姓名，由使用者自己配對）。</summary>
public sealed record SyncCardRequest(Guid WorkerId, Guid SiteId);

/// <summary>同步結果：今次寫入幾筆打卡明細，以及重算後的薪酬表內容。</summary>
public sealed record CardSyncResultDto(Guid ScanId, int Year, int Month, Guid WorkerId, string WorkerName,
    Guid SiteId, string SiteName, int AddedRecords, decimal WorkDays, decimal LaborLeaveDays,
    decimal TotalWorkDays, decimal OvertimeHours, decimal DailyWage, decimal OvertimeAmount,
    decimal MpfEmployer, decimal MpfEmployee, decimal ActualPay, decimal TotalAmount)
{
    public static CardSyncResultDto From(AttendanceCardScan scan, Worker worker, Site site, int added,
        MonthlyWorkSummary summary) => new(scan.Id, summary.Year, summary.Month, worker.Id, worker.Name, site.Id,
        site.Name, added, summary.WorkDays, summary.LaborLeaveDays, summary.TotalWorkDays, summary.OvertimeHours,
        summary.DailyWage, summary.OvertimeAmount, summary.MpfEmployer, summary.MpfEmployee, summary.ActualPay,
        summary.TotalAmount);
}

/// <summary>
/// 考勤卡掃描：沿用票據識別的 AZURE_DOC_ENDPOINT / AZURE_DOC_KEY（同一個 client、prebuilt-layout 模型）。
/// 掃描即落庫：原圖進 storage_files，逐日明細進 attendance_card_punches，不存任何工時或合計。
/// </summary>
public sealed class AttendanceCardService(IAttendanceCardOcrService ocr, ErpDbContext db, FileService files,
    SysDictService dicts, ILogger<AttendanceCardService> logger)
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const string CardTypeGroup = "AttendanceCard";
    private const string CardTypeValue = "card";
    private static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];

    public bool OcrConfigured => ocr.IsConfigured;

    /// <summary>掃描一張考勤卡並保存；識別失敗不寫任何資料，也不留孤兒原圖。</summary>
    public async Task<AttendanceCardScanDto> ScanAsync(IFormFile image, CancellationToken ct = default)
    {
        var content = await ReadAsync(image, ct);
        var mime = CheckImage(content);
        var card = await ocr.ScanAsync(content);
        return await SaveAsync(card, content, Path.GetFileName(image.FileName), mime, ct);
    }

    /// <summary>連結由 Azure 拉取，服務端不下載（避免任意 URL 讀內網），所以沒有原圖可存。</summary>
    public async Task<AttendanceCardScanDto> ScanAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("請提供 https 圖片連結");
        return await SaveAsync(await ocr.ScanAsync(uri), null, null, null, ct);
    }

    public async Task<(List<AttendanceCardScanSummaryDto> Items, int Total)> ListAsync(int? year, int? month,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.AttendanceCardScans.AsNoTracking();
        if (year is { } selectedYear) query = query.Where(scan => scan.Year == selectedYear);
        if (month is { } selectedMonth) query = query.Where(scan => scan.Month == selectedMonth);
        var total = await query.CountAsync(ct);
        var items = await query.Include(scan => scan.Punches)
            .OrderByDescending(scan => scan.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return (items.Select(AttendanceCardScanSummaryDto.From).ToList(), total);
    }

    public async Task<AttendanceCardScanDto> FindAsync(Guid id, CancellationToken ct = default)
    {
        var scan = await db.AttendanceCardScans.AsNoTracking()
            .Include(item => item.Punches)
            .Include(item => item.FileItem)
            .FirstOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new KeyNotFoundException("考勤卡掃描記錄不存在");
        return AttendanceCardScanDto.From(scan);
    }

    /// <summary>
    /// 標記為已核對（人工已對照原卡確認整張卡）：卡片層複核標記清空，
    /// 而且每一列時間欄的置信度一併記成人工確認（1），所以日期欄的紅字也一併放行——
    /// 狀態由「需覆核」變「已識別」。時間本身的值完全不動。
    /// <paramref name="IncludeHandwritten"/> 為 true 時仍會檢查手寫的年月姓名，低置信度的照樣保留需覆核。
    /// </summary>
    public async Task<AttendanceCardScanDto> ConfirmAsync(Guid id, ConfirmCardScanRequest request,
        CancellationToken ct = default)
    {
        var scan = await db.AttendanceCardScans
            .Include(item => item.Punches)
            .Include(item => item.FileItem)
            .FirstOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new KeyNotFoundException("考勤卡掃描記錄不存在");
        scan.ReviewFields = request.IncludeHandwritten
            ? CardReviewRules.ReviewFieldsOf(scan, includeHandwritten: true)
            : [];
        foreach (var punch in scan.Punches) ConfirmPunch(punch);
        scan.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return AttendanceCardScanDto.From(scan);
    }

    /// <summary>把一列明細裡「有時間但置信度低於閾值」的欄位記成人工確認；空欄不動。</summary>
    private static void ConfirmPunch(AttendanceCardPunch punch)
    {
        (punch.AmIn, punch.AmInConfidence) = Confirmed(punch.AmIn, punch.AmInConfidence);
        (punch.AmOut, punch.AmOutConfidence) = Confirmed(punch.AmOut, punch.AmOutConfidence);
        (punch.PmIn, punch.PmInConfidence) = Confirmed(punch.PmIn, punch.PmInConfidence);
        (punch.PmOut, punch.PmOutConfidence) = Confirmed(punch.PmOut, punch.PmOutConfidence);
        (punch.OtIn, punch.OtInConfidence) = Confirmed(punch.OtIn, punch.OtInConfidence);
        (punch.OtOut, punch.OtOutConfidence) = Confirmed(punch.OtOut, punch.OtOutConfidence);
    }

    private static (TimeOnly? Time, float? Confidence) Confirmed(TimeOnly? time, float? confidence) =>
        time is not null && (confidence is not { } score || score < AzureDocumentOcrService.ConfidenceThreshold)
            ? (time, 1f)
            : (time, confidence);

    /// <summary>人工修正識別結果；只寫回真正改動的欄位，改過的欄位記為人工確認。</summary>
    public async Task<AttendanceCardScanDto> UpdateAsync(Guid id, UpdateCardScanRequest request,
        CancellationToken ct = default)
    {
        var scan = await db.AttendanceCardScans
            .Include(item => item.Punches)
            .Include(item => item.FileItem)
            .FirstOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new KeyNotFoundException("考勤卡掃描記錄不存在");
        request.ApplyTo(scan);
        scan.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return AttendanceCardScanDto.From(scan);
    }

    /// <summary>
    /// 把一張考勤卡導入打卡明細，並按該員工該月的全部打卡明細重算薪酬表。
    /// 同一員工同一月再同步＝重算：先刪掉上次由考勤卡導入的明細再寫入，所以改完時間重按不會重複計工數。
    /// </summary>
    public async Task<CardSyncResultDto> SyncAsync(Guid id, SyncCardRequest request, CancellationToken ct = default)
    {
        if (request.WorkerId == Guid.Empty || request.SiteId == Guid.Empty)
            throw new InvalidOperationException("請選擇員工和地盤");

        var scan = await db.AttendanceCardScans.Include(item => item.Punches)
            .FirstOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new KeyNotFoundException("考勤卡掃描記錄不存在");
        if (scan.Year is not { } year || scan.Month is not { } month)
            throw new InvalidOperationException("這張考勤卡沒有年月，請先在識別結果裡補上再同步");

        var worker = await db.Workers.FirstOrDefaultAsync(item => item.Id == request.WorkerId && !item.IsDeleted, ct)
            ?? throw new KeyNotFoundException("員工不存在");
        var site = await db.Sites.FirstOrDefaultAsync(item => item.Id == request.SiteId && !item.IsDeleted, ct)
            ?? throw new KeyNotFoundException("地盤不存在");

        // 日薪是計薪基礎，0 元會算出負數的實付人工，先擋住讓使用者補
        if (worker.DailyWage <= 0)
            throw new InvalidOperationException($"員工「{worker.Name}」的日薪未設定，請先設定日薪再同步");

        var days = scan.Punches.Where(AttendanceRules.HasTime).OrderBy(punch => punch.Day).ToList();
        if (days.Count == 0) throw new InvalidOperationException("這張考勤卡沒有打卡時間，沒有可導入的明細");

        // 2 月出現 30 日這種情況先擋住；勉強跳過會少算工數
        if (days.FirstOrDefault(punch => punch.Day > DateTime.DaysInMonth(year, month)) is { } invalidDay)
            throw new InvalidOperationException($"第 {invalidDay.Day} 日超出 {year} 年 {month} 月的天數，請先修正識別結果");

        var (start, end) = AttendanceRules.MonthRange(year, month);

        // 該員工該月已有的明細：考勤卡導入的換掉，其他來源（模擬、人工）保留
        var existing = await db.AttendanceRecords
            .Where(record => record.WorkerId == worker.Id && record.ClockInTime >= start && record.ClockInTime < end)
            .ToListAsync(ct);
        var imported = existing.Where(record => record.Remark == AttendanceRules.ImportedRemark).ToList();
        db.AttendanceRecords.RemoveRange(imported);
        existing = existing.Where(record => record.Remark != AttendanceRules.ImportedRemark).ToList();

        var records = days.Select(punch => AttendanceRules.ToRecord(year, month, punch, worker.Id, site.Id)).ToList();
        db.AttendanceRecords.AddRange(records);

        // 薪酬按該月全部打卡明細重算；剛 Add 的和要刪的都還沒寫庫，用記憶體裡的清單算
        var all = existing.Concat(records).ToList();
        var summary = await db.MonthlyWorkSummaries
            .FirstOrDefaultAsync(item => item.WorkerId == worker.Id && item.Year == year && item.Month == month, ct);
        if (summary is null)
        {
            summary = new MonthlyWorkSummary { WorkerId = worker.Id, Year = year, Month = month, Remark = "由考勤卡自動計算" };
            db.MonthlyWorkSummaries.Add(summary);
        }
        // 勞工假不在卡上，ApplyPayroll 沿用薪酬表原有的值
        AttendanceRules.ApplyPayroll(summary, worker.DailyWage, AttendanceRules.WorkDaysOf(all),
            all.Sum(record => record.OvertimeHours));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("考勤卡 {ScanId} 導入 {Count} 筆打卡明細給 {Worker}（替換 {Replaced} 筆），已重算 {Year}-{Month} 薪酬",
            scan.Id, records.Count, worker.Name, imported.Count, year, month);
        return CardSyncResultDto.From(scan, worker, site, records.Count, summary);
    }

    /// <summary>軟刪除掃描記錄並清理原圖；明細留在庫裡，恢復時仍可對上。</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var scan = await db.AttendanceCardScans.FirstOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new KeyNotFoundException("考勤卡掃描記錄不存在");
        var fileId = scan.FileItemId;
        scan.IsDeleted = true;
        scan.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (fileId is not { } file) return;
        try { await files.删除Async(file, ct); }
        catch (Exception cleanup) { logger.LogWarning(cleanup, "Attendance card delete cleanup failed for {FileId}", file); }
    }

    private async Task<AttendanceCardScanDto> SaveAsync(AttendanceCard card, byte[]? content, string? fileName,
        string? mime, CancellationToken ct)
    {
        var scan = new AttendanceCardScan
        {
            Year = card.Year.Value,
            YearConfidence = card.Year.Confidence,
            Month = card.Month.Value,
            MonthConfidence = card.Month.Confidence,
            Name = card.Name.Value is { } name ? name[..Math.Min(name.Length, 50)] : null,
            NameConfidence = card.Name.Confidence,
            ReviewFields = [.. card.ReviewFields],
            Punches = card.Records.Select(ToPunch).ToList()
        };
        if (content is not null && fileName is not null && mime is not null)
        {
            using var upload = new MemoryStream(content);
            var file = await files.上传Async(await CardTypeAsync(ct), fileName, mime, content.Length, upload, ct);
            scan.FileItemId = file.Id;
            scan.FileItem = file;
        }

        db.AttendanceCardScans.Add(scan);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // 明細沒存成就不留下孤兒原圖
            if (scan.FileItemId is not { } orphan) throw;
            try { await files.删除Async(orphan, ct); }
            catch (Exception cleanup) { logger.LogWarning(cleanup, "Attendance card cleanup failed for {FileId}", orphan); }
            throw;
        }
        return AttendanceCardScanDto.From(scan);
    }

    private static AttendanceCardPunch ToPunch(AttendanceRow row) => new()
    {
        Day = row.Day,
        AmIn = Time(row.AmIn.Value),
        AmInConfidence = row.AmIn.Confidence,
        AmOut = Time(row.AmOut.Value),
        AmOutConfidence = row.AmOut.Confidence,
        PmIn = Time(row.PmIn.Value),
        PmInConfidence = row.PmIn.Confidence,
        PmOut = Time(row.PmOut.Value),
        PmOutConfidence = row.PmOut.Confidence,
        OtIn = Time(row.OtIn.Value),
        OtInConfidence = row.OtIn.Confidence,
        OtOut = Time(row.OtOut.Value),
        OtOutConfidence = row.OtOut.Confidence
    };

    private static TimeOnly? Time(string? value) => TimeOnly.TryParseExact(value, "HH:mm",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    /// <summary>考勤卡原圖的文件類別（字典 AttendanceCard / card），由 SeedData 建立。</summary>
    private async Task<Guid> CardTypeAsync(CancellationToken ct) =>
        (await dicts.ListByGroupAsync(CardTypeGroup, ct))
        .FirstOrDefault(dict => dict.Enabled && dict.Value == CardTypeValue)?.Id
        ?? throw new InvalidOperationException("缺少考勤卡文件類別字典，請重啟服務以初始化字典");

    private static async Task<byte[]> ReadAsync(IFormFile image, CancellationToken ct)
    {
        if (image.Length is 0 or > MaxImageBytes) throw new InvalidOperationException("圖片須介乎 1 byte 至 4 MB");
        using var buffer = new MemoryStream((int)image.Length);
        await using var stream = image.OpenReadStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    /// <summary>只認 JPEG / PNG，判斷檔頭不看副檔名，和票據原圖同一套規則。</summary>
    private static string CheckImage(byte[] content) =>
        content.Length >= 3 && content[0] == 255 && content[1] == 216 && content[2] == 255 ? "image/jpeg"
        : content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(PngHeader) ? "image/png"
        : throw new InvalidOperationException("第一版請使用 JPEG 或 PNG 圖片");
}
