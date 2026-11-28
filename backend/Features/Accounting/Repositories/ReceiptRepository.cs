using MC.Erp.Data;
using MC.Erp.Features.Accounting.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Accounting.Repositories;

/// <summary>
/// 票據篩選；字典欄位是「自己 + 整棵子樹」的 id 清單（ReceiptService.ExpandFilterAsync 擴張），
/// null = 不篩。列表選上級 = 連下級一起查，口徑與樹形下拉一致。
/// </summary>
public sealed record ReceiptFilter(List<Guid>? DocumentTypeDictIds = null,
    List<Guid>? TypeIds = null, List<Guid>? SiteCategoryDictIds = null, DateOnly? Start = null, DateOnly? End = null);

/// <summary>列表一行：只有列表欄位。字典名稱由前端按 id 映射，接口不重複返回 label。</summary>
public sealed record ReceiptListItem(Guid Id, Guid Version, Guid? FileItemId, string? FileName, string? ContentType,
    string? Md5, long? SizeBytes, Guid DocumentTypeDictId,
    Guid Type, Guid? SiteCategoryDictId,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError);

/// <summary>詳情：列表欄位加上 OCR 原文和識別建議。</summary>
public sealed record ReceiptDetail(Guid Id, Guid Version, Guid? FileItemId, string? FileName, string? ContentType,
    string? Md5, long? SizeBytes, Guid DocumentTypeDictId,
    Guid Type, Guid? SiteCategoryDictId,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError,
    string? OcrText, string? OcrResultJson);

/// <summary>導出 ZIP 需要的欄位：原圖信息和目錄名。</summary>
public sealed record ReceiptExport(Guid Id, Guid FileItemId, DateOnly? ReceiptDate, Guid? SiteCategoryDictId,
    string? SiteCategoryLabel, Guid Type, string TypeLabel, string FileName, long SizeBytes);

/// <summary>單一幣種的金額合計；CurrencyCode 為 null 代表票據有金額但沒填幣種。</summary>
public sealed record ReceiptCurrencyTotal(string? CurrencyCode, int Count, decimal Amount);

/// <summary>整份篩選結果的金額合計。不同幣種不混加；MissingAmount 是未填金額的張數。</summary>
public sealed record ReceiptTotals(List<ReceiptCurrencyTotal> Amounts, int MissingAmount);

public sealed class ReceiptRepository(ErpDbContext db, IHttpContextAccessor context)
{
    private CancellationToken Token => context.HttpContext?.RequestAborted ?? default;

    /// <summary>篩選條件（不含 Include）；列表、筆數、合計共用同一份，口徑不會漂移。</summary>
    private IQueryable<Receipt> Filtered(ReceiptFilter filter) => db.Receipts
        // 沒有原圖的手動票據也要列出；原圖被軟刪除的票據維持不列出
        .Where(receipt => receipt.FileItemId == null
            || (receipt.FileItem != null && receipt.FileItem.TypeDict != null && receipt.FileItem.TypeDict.GroupName == "Accounting"))
        .Where(receipt => filter.DocumentTypeDictIds == null || filter.DocumentTypeDictIds.Contains(receipt.DocumentTypeDictId))
        .Where(receipt => filter.TypeIds == null || filter.TypeIds.Contains(receipt.Type))
        .Where(receipt => filter.SiteCategoryDictIds == null
            || (receipt.SiteCategoryDictId != null && filter.SiteCategoryDictIds.Contains(receipt.SiteCategoryDictId.Value)))
        .Where(receipt => filter.Start == null || receipt.ReceiptDate >= filter.Start)
        .Where(receipt => filter.End == null || receipt.ReceiptDate <= filter.End);

    /// <summary>列表只投影需要的欄位；OcrText、OcrResultJson 這類大欄位留在列表之外，不讓每一頁都拖下來。</summary>
    private IQueryable<ReceiptListItem> ProjectList(IQueryable<Receipt> query) => query.Select(receipt => new ReceiptListItem(
        receipt.Id, receipt.Version, receipt.FileItemId,
        receipt.FileItem == null ? null : receipt.FileItem.OriginalName,
        receipt.FileItem == null ? null : receipt.FileItem.ContentType,
        receipt.FileItem == null ? null : receipt.FileItem.Md5,
        receipt.FileItem == null ? null : (long?)receipt.FileItem.SizeBytes,
        receipt.DocumentTypeDictId,
        receipt.Type, receipt.SiteCategoryDictId,
        receipt.ReceiptDate, receipt.Amount, receipt.CurrencyCode, receipt.MerchantName,
        receipt.InvoiceNumber, receipt.Remark, receipt.OcrStatus, receipt.OcrError));

    private IQueryable<ReceiptDetail> ProjectDetail(IQueryable<Receipt> query) => query.Select(receipt => new ReceiptDetail(
        receipt.Id, receipt.Version, receipt.FileItemId,
        receipt.FileItem == null ? null : receipt.FileItem.OriginalName,
        receipt.FileItem == null ? null : receipt.FileItem.ContentType,
        receipt.FileItem == null ? null : receipt.FileItem.Md5,
        receipt.FileItem == null ? null : (long?)receipt.FileItem.SizeBytes,
        receipt.DocumentTypeDictId,
        receipt.Type, receipt.SiteCategoryDictId,
        receipt.ReceiptDate, receipt.Amount, receipt.CurrencyCode, receipt.MerchantName,
        receipt.InvoiceNumber, receipt.Remark, receipt.OcrStatus, receipt.OcrError,
        receipt.OcrText, receipt.OcrResultJson));

    /// <summary>排序和分頁留在實體側，投影放最後；順序反過來 EF 就把整個 record 構造器當成不可翻譯的成員訪問。</summary>
    public async Task<(List<ReceiptListItem> Items, int Total, ReceiptTotals Totals)> ListAsync(
        ReceiptFilter filter, int page, int pageSize)
    {
        var total = await Filtered(filter).CountAsync(Token);
        var pageQuery = Filtered(filter).AsNoTracking()
            .OrderByDescending(receipt => receipt.ReceiptDate).ThenByDescending(receipt => receipt.Id)
            .Skip((page - 1) * pageSize).Take(pageSize);
        var items = await ProjectList(pageQuery).ToListAsync(Token);
        // 合計按整份篩選結果算，不受分頁影響；幣種分開，HKD / CNY / USD 不會加在一起
        var amounts = await Filtered(filter).AsNoTracking()
            .Where(receipt => receipt.Amount != null)
            .GroupBy(receipt => receipt.CurrencyCode)
            .Select(group => new ReceiptCurrencyTotal(group.Key, group.Count(), group.Sum(receipt => receipt.Amount) ?? 0m))
            .ToListAsync(Token);
        // 未填幣種的排最後，其餘按幣種字母序，合計行順序穩定
        amounts = amounts.OrderBy(item => item.CurrencyCode is null).ThenBy(item => item.CurrencyCode, StringComparer.Ordinal).ToList();
        return (items, total, new ReceiptTotals(amounts, total - amounts.Sum(item => item.Count)));
    }

    /// <summary>導出 ZIP 用：只查目錄名和原圖大小，不整行拉。</summary>
    public Task<List<ReceiptExport>> ExportAsync(ReceiptFilter filter) => Filtered(filter).AsNoTracking()
        .Where(receipt => receipt.FileItemId != null)
        .OrderBy(receipt => receipt.ReceiptDate).ThenBy(receipt => receipt.Id).Take(5001)
        .Select(receipt => new ReceiptExport(receipt.Id, receipt.FileItemId!.Value, receipt.ReceiptDate,
            receipt.SiteCategoryDictId, receipt.SiteCategoryDict == null ? null : receipt.SiteCategoryDict.Label,
            receipt.Type, receipt.TypeDict.Label,
            receipt.FileItem!.OriginalName, receipt.FileItem.SizeBytes))
        .ToListAsync(Token);

    public Task<ReceiptDetail?> FindDetailAsync(Guid id) => ProjectDetail(Filtered(new()).AsNoTracking()
        .Where(receipt => receipt.Id == id)).FirstOrDefaultAsync(Token);

    /// <summary>寫入用的整行實體：只有保存、識別需要，查詢一律走上面的投影。</summary>
    public Task<Receipt?> FindTrackedAsync(Guid id) => db.Receipts
        .Include(receipt => receipt.FileItem).Include(receipt => receipt.DocumentTypeDict)
        .Include(receipt => receipt.TypeDict).Include(receipt => receipt.SiteCategoryDict)
        .FirstOrDefaultAsync(receipt => receipt.Id == id, Token);

    public async Task AddAsync(Receipt receipt)
    {
        db.Receipts.Add(receipt);
        try { await db.SaveChangesAsync(Token); }
        catch { db.Entry(receipt).State = EntityState.Detached; throw; }
    }

    public async Task DeleteAsync(Receipt receipt)
    {
        db.Receipts.Remove(receipt);
        await db.SaveChangesAsync(Token);
    }

    public async Task InTransactionAsync(Func<Task> operation)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await operation();
        await transaction.CommitAsync(Token);
    }

    public async Task SaveAsync(Receipt receipt)
    {
        receipt.Version = Guid.NewGuid();
        receipt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(Token);
    }

    public Task<bool> IsFileReferencedAsync(Guid id) => db.Receipts.IgnoreQueryFilters().AnyAsync(receipt => receipt.FileItemId == id, Token);

    public async Task<bool> StartRecognitionAsync(Receipt receipt)
    {
        var staleBefore = DateTime.UtcNow.AddMinutes(-3);
        var version = Guid.NewGuid();
        var started = DateTime.UtcNow;
        var changed = await db.Receipts.Where(item => item.Id == receipt.Id && item.Version == receipt.Version
            && (item.OcrStatus != "Processing" || item.OcrStartedAt < staleBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.OcrStatus, "Processing")
                .SetProperty(item => item.OcrStartedAt, started).SetProperty(item => item.OcrError, (string?)null)
                .SetProperty(item => item.Version, version), Token);
        if (changed == 0) return false;
        await db.Entry(receipt).ReloadAsync(Token);
        return true;
    }

    public async Task SaveRecognitionAsync(Receipt receipt)
    {
        receipt.Version = Guid.NewGuid();
        receipt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}