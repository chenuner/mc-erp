using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 分類字典服務（地盤 / 時間 / 事件原因）
/// </summary>
public sealed class CategoryService(ErpDbContext db)
{
    public async Task<List<(StorageCategory Category, int FileCount)>> 列表Async(CancellationToken ct)
    {
        return await db.StorageCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.EventDate)
            .Select(c => new ValueTuple<StorageCategory, int>(c, c.Files.Count(f => !f.IsDeleted)))
            .ToListAsync(ct);
    }

    public async Task<StorageCategory> 创建Async(
        string 地盘, DateTime 时间, string 事件原因, string? 备注, CancellationToken ct)
    {
        var category = new StorageCategory
        {
            SiteName = 地盘,
            EventDate = 时间,
            EventReason = 事件原因,
            Remark = 备注,
        };
        db.StorageCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<StorageCategory?> 更新Async(
        Guid id, string? 地盘, DateTime? 时间, string? 事件原因, string? 备注, CancellationToken ct)
    {
        var category = await db.StorageCategories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
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
        var category = await db.StorageCategories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
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
}
