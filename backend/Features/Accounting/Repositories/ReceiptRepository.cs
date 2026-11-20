using MC.Erp.Data;
using MC.Erp.Features.Accounting.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Accounting.Repositories;

public sealed record ReceiptFilter(Guid? DocumentTypeDictId = null,
    Guid? Type = null, Guid? SiteCategoryDictId = null, DateOnly? Start = null, DateOnly? End = null);

public sealed class ReceiptRepository(ErpDbContext db, IHttpContextAccessor context)
{
    private CancellationToken Token => context.HttpContext?.RequestAborted ?? default;

    private IQueryable<Receipt> Query(ReceiptFilter filter) => db.Receipts
        .Include(receipt => receipt.FileItem)
        .Include(receipt => receipt.TypeDict)
        .Include(receipt => receipt.DocumentTypeDict).Include(receipt => receipt.SiteCategoryDict)
        // 沒有原圖的手動票據也要列出；原圖被軟刪除的票據維持不列出
        .Where(receipt => receipt.FileItemId == null
            || (receipt.FileItem != null && receipt.FileItem.TypeDict != null && receipt.FileItem.TypeDict.GroupName == "Accounting"))
        .Where(receipt => filter.DocumentTypeDictId == null || receipt.DocumentTypeDictId == filter.DocumentTypeDictId)
        .Where(receipt => filter.Type == null || receipt.Type == filter.Type)
        .Where(receipt => filter.SiteCategoryDictId == null || receipt.SiteCategoryDictId == filter.SiteCategoryDictId)
        .Where(receipt => filter.Start == null || receipt.ReceiptDate >= filter.Start)
        .Where(receipt => filter.End == null || receipt.ReceiptDate <= filter.End);

    public async Task<(List<Receipt> Items, int Total)> ListAsync(ReceiptFilter filter, int page, int pageSize)
    {
        var query = Query(filter).AsNoTracking();
        var total = await query.CountAsync(Token);
        var items = await query.OrderByDescending(receipt => receipt.ReceiptDate).ThenByDescending(receipt => receipt.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(Token);
        return (items, total);
    }

    public Task<List<Receipt>> ExportAsync(ReceiptFilter filter) => Query(filter).AsNoTracking()
        .Where(receipt => receipt.FileItemId != null)
        .OrderBy(receipt => receipt.ReceiptDate).ThenBy(receipt => receipt.Id).Take(5001).ToListAsync(Token);

    public Task<Receipt?> FindAsync(Guid id) => Query(new()).FirstOrDefaultAsync(receipt => receipt.Id == id, Token);

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