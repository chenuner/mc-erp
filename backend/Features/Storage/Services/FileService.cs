using System.IO.Compression;
using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Features.System.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 文件服務：上傳 / 列表 / 下載 / 刪除
/// </summary>
public sealed class FileService(ErpDbContext db, IFileStorage storage, ICurrentUser user, SysDictService dictService,
    AccountingFileProtectionService accounting, IHttpContextAccessor context)
{
    public async Task<FileItem> 上传Async(Guid type, string originalName, string contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        var dict = await dictService.GetByIdAsync(type, ct);
        if (dict is null || !dict.Enabled)
            throw new InvalidOperationException("請選擇有效且啟用的文件類別");
        if (dict.GroupName == "Accounting" && !user.IsInRole("Admin")) throw new UnauthorizedAccessException();

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
            .Where(f => user.IsInRole("Admin") || f.TypeDict == null || f.TypeDict.GroupName != "Accounting")
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<(Stream Content, FileItem File)?> 下载Async(Guid id, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        var file = await db.FileItems.AsNoTracking().Include(f => f.TypeDict).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (file?.TypeDict?.GroupName == "Accounting" && !user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        return file is null ? null : (storage.OpenRead(file.StoredName), file);
    }

    public async Task 设置类别Async(Guid id, Guid type)
    {
        if (!user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        var ct = context.HttpContext?.RequestAborted ?? default;
        var dict = await dictService.GetByIdAsync(type, ct);
        if (dict is null || !dict.Enabled || dict.GroupName != "Accounting") throw new InvalidOperationException("請選擇有效支出分類");
        var file = await db.FileItems.Include(item => item.TypeDict).FirstOrDefaultAsync(item => item.Id == id, ct);
        if (file?.TypeDict?.GroupName != "Accounting") throw new InvalidOperationException("會計圖片不存在");
        file.Type = dict.Id;
        file.TypeDict = dict;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Stream> 打包Async(IReadOnlyList<(Guid FileId, string Path)> entries)
    {
        var ct = context.HttpContext?.RequestAborted ?? default;
        var temporary = new FileStream(Path.Combine(Path.GetTempPath(), $"mc-erp-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var archive = new ZipArchive(temporary, ZipArchiveMode.Create, true))
            {
                foreach (var (fileId, path) in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var download = await 下载Async(fileId, ct) ?? throw new IOException("票據原圖不存在");
                    await using var source = download.Content;
                    var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, ct);
                }
            }
            temporary.Position = 0;
            return temporary;
        }
        catch { await temporary.DisposeAsync(); throw; }
    }

    public async Task<bool> 删除Async(Guid id, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        var file = await db.FileItems.Include(f => f.TypeDict).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (file is null) return false;
        if (file.TypeDict?.GroupName == "Accounting" && !user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        if (await accounting.IsReferencedAsync(id)) throw new InvalidOperationException("圖片已關聯會計票據，不能刪除");

        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // 元數據軟刪除後，物理文件同步清理
        await storage.DeleteAsync(file.StoredName, ct);
        return true;
    }
}
