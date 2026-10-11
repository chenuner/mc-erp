using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 文件服務：上傳 / 列表 / 下載 / 刪除
/// </summary>
public sealed class FileService(ErpDbContext db, IFileStorage storage, ICurrentUser user, SysDictService dictService)
{
    public async Task<FileItem> 上传Async(Guid type, string originalName, string contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        var dict = await dictService.GetByIdAsync(type, ct);
        if (dict is null || !dict.Enabled)
            throw new InvalidOperationException("請選擇有效且啟用的文件類別");

        var saved = await storage.SaveAsync(content, originalName, ct);
        var file = new FileItem
        {
            Type = dict.Id,
            TypeDict = dict,
            OriginalName = originalName,
            StoredName = saved.StoredName,
            Md5 = saved.Md5,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploaderId = user.UserId,
            UploaderName = user.Username,
        };

        db.FileItems.Add(file);
        await db.SaveChangesAsync(ct);
        return file;
    }

    public async Task<List<FileItem>> 列表Async(Guid? type, CancellationToken ct)
    {
        return await db.FileItems
            .AsNoTracking()
            .Include(f => f.TypeDict)
            .Where(f => !f.IsDeleted && (type == null || f.Type == type))
            .OrderByDescending(f => f.CreatedAt)
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
