using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 地盤文件服務：上傳 / 列表 / 下載 / 刪除
/// </summary>
public sealed class FileService(ErpDbContext db, IFileStorage storage, ICurrentUser user)
{
    public async Task<FileItem> 上传Async(Guid categoryId, string originalName, string contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        var category = await db.ArchiveCategories.FirstOrDefaultAsync(c => c.Id == categoryId && !c.IsDeleted, ct)
            ?? throw new InvalidOperationException("分類不存在");

        var file = new FileItem
        {
            CategoryId = categoryId,
            OriginalName = originalName,
            StoredName = await storage.SaveAsync(content, originalName, ct),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploaderId = user.UserId,
            UploaderName = user.Username,
        };

        db.FileItems.Add(file);
        await db.SaveChangesAsync(ct);
        return file;
    }

    public async Task<List<(FileItem File, ArchiveCategory Category)>> 列表Async(Guid? categoryId, CancellationToken ct)
    {
        var query = db.FileItems
            .AsNoTracking()
            .Where(f => !f.IsDeleted);

        if (categoryId is not null)
        {
            query = query.Where(f => f.CategoryId == categoryId);
        }

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new ValueTuple<FileItem, ArchiveCategory>(f, f.Category))
            .ToListAsync(ct);
    }

    public async Task<(Stream Content, FileItem File)?> 下载Async(Guid id, CancellationToken ct)
    {
        var file = await db.FileItems.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        return file is null ? null : (storage.OpenRead(file.StoredName), file);
    }

    public async Task<bool> 删除Async(Guid id, CancellationToken ct)
    {
        var file = await db.FileItems.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (file is null) return false;

        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // 元數據軟刪除後，物理文件同步清理
        await storage.DeleteAsync(file.StoredName, ct);
        return true;
    }
}
