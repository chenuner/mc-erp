using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.System.Services;

/// <summary>
/// 歸檔分類字典服務（地盤 / 時間 / 事件原因）。
/// 系統級共享字典，地盤文件、會計收據等模組均可引用。
/// </summary>
public sealed class CategoryService(ErpDbContext db)
{
    public async Task<List<(ArchiveCategory Category, int FileCount)>> 列表Async(CancellationToken ct)
    {
        return await db.ArchiveCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.EventDate)
            .ThenByDescending(c => c.CreatedAt)
            .Select(c => new ValueTuple<ArchiveCategory, int>(
                c,
                db.FileItems.Count(f => f.CategoryId == c.Id && !f.IsDeleted)))
            .ToListAsync(ct);
    }

    public async Task<ArchiveCategory> 创建Async(
        string 地盘, DateTime 时间, string 事件原因, string? 备注, CancellationToken ct)
    {
        var category = new ArchiveCategory
        {
            SiteName = 地盘,
            EventDate = 时间,
            EventReason = 事件原因,
            Remark = 备注,
        };
        db.ArchiveCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<ArchiveCategory?> 更新Async(
        Guid id, string? 地盘, DateTime? 时间, string? 事件原因, string? 备注, CancellationToken ct)
    {
        var category = await db.ArchiveCategories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (category is null) return null;

        if (地盘 is not null) category.SiteName = 地盘;
        if (时间 is not null) category.EventDate = 时间.Value;
        if (事件原因 is not null) category.EventReason = 事件原因;
        if (备注 is not null) category.Remark = 备注;
        category.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>有文件引用的分類不允許刪除</summary>
    public async Task<bool> 删除Async(Guid id, CancellationToken ct)
    {
        var category = await db.ArchiveCategories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (category is null) return false;

        var hasFiles = await db.FileItems.AnyAsync(f => f.CategoryId == id && !f.IsDeleted, ct);
        if (hasFiles)
        {
            throw new InvalidOperationException("該分類下仍有文件，請先刪除文件");
        }

        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>獲取單個分類（供其他模組校驗引用是否存在）</summary>
    public async Task<ArchiveCategory?> 获取Async(Guid id, CancellationToken ct)
    {
        return await db.ArchiveCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
    }
}
