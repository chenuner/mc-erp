using System.IO.Compression;
using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 文件服務：上傳 / 列表 / 下載 / 刪除
/// </summary>
/// <remarks>
/// 落盤名是隨機 GUID + 原擴展名，原名只存在資料庫 storage_files.OriginalName：
/// 本地開發：D:\DevCode\mc-erp\backend\storage-files\xxxx.png（Storage:RootPath 相對工作目錄 backend）
/// 生產環境：容器內 /app/storage-files/xxxx.png（docker volume storage 掛載，重建容器不丟文件）
/// </remarks>
public sealed class FileService(ErpDbContext db, IFileStorage storage, ICurrentUser user, SysDictService dictService,
    SysDictGroupService groupService, AccountingFileProtectionService accounting, IHttpContextAccessor context)
{
    /// <summary>
    /// 可以當「文件類別」的字典分組：文件庫只認這幾類，職位分類、驗證分組之類不該出現在上傳與篩選裡。
    /// 新增一類文件就在這裡加一個分組代碼。
    /// </summary>
    public static readonly string[] FileTypeGroups =
        ["Accounting", "AccountingDocumentType", "AccountingSiteCategory", "AttendanceCard"];

    /// <summary>按分組篩選的寫法：type=group:Accounting 代表整個分組（分組本身不是字典項，沒有 id）。</summary>
    public const string GroupPrefix = "group:";

    public async Task<FileItem> 上传Async(Guid type, string originalName, string contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        var dict = await dictService.EnsureEnabledAsync(type, null, "請選擇有效且啟用的文件類別", ct);
        if (!FileTypeGroups.Contains(dict.GroupName)) throw new InvalidOperationException("這個分組不能當文件類別");
        if (dict.GroupName == "Accounting" && !user.IsInRole("Admin")) throw new UnauthorizedAccessException();

        var saved = await storage.SaveAsync(content, originalName, ct);
        // 按 MD5 去重：同內容在磁盤上只留一份，剛寫的這份若重複就丟掉，改用既有的 StoredName。
        // 元數據（原名、類別、上傳人、時間）仍然各自一行，所以同一張圖可以一次當收據、一次當考勤卡。
        var storedName = await DeduplicateAsync(saved.StoredName, saved.Md5, ct);
        var file = new FileItem
        {
            Type = dict.Id,
            TypeDict = dict,
            OriginalName = originalName,
            StoredName = storedName,
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

    /// <summary>
    /// 回傳應該使用的 StoredName：已經有同 MD5 且未刪除的記錄就沿用它，並把剛寫的物理文件刪掉。
    /// 只跟有 MD5 的記錄比對（歷史資料 Md5 為空，認不出內容）。
    /// 併發上傳同一份新內容時可能兩邊都查不到、各寫一份，不會出錯，這個系統的併發量不值得加鎖。
    /// </summary>
    private async Task<string> DeduplicateAsync(string storedName, string md5, CancellationToken ct)
    {
        var existing = await db.FileItems.AsNoTracking()
            .Where(item => item.Md5 == md5)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => item.StoredName)
            .FirstOrDefaultAsync(ct);
        if (existing is null) return storedName;
        await storage.DeleteAsync(storedName, ct);
        return existing;
    }

    /// <summary>
    /// 列表的類別篩選：空 = 全部；group:xxx = 整個分組；字典項 id = 該項連同下級子樹（子樹按 ParentId 走，識別碼會重新編號）。
    /// </summary>
    public async Task<List<Guid>?> 解析類別Async(string? type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        if (type.StartsWith(GroupPrefix, StringComparison.Ordinal))
        {
            var ids = await dictService.GroupDictIdsAsync(type[GroupPrefix.Length..], ct);
            if (ids.Count == 0) throw new InvalidOperationException("分組不存在或底下沒有分類");
            return ids;
        }
        if (!Guid.TryParse(type, out var id)) throw new InvalidOperationException("無效的文件類別");
        return await dictService.SubtreeIdsAsync(id, ct);
    }

    /// <summary>
    /// 文件類別的可選清單：只回白名單分組（含停用項，歷史文件要能按停用類別篩選）。
    /// 前端據此建樹，不用再把整份字典拉下來。
    /// </summary>
    public async Task<(List<SysDictGroup> Groups, List<SysDict> Dicts)> 類別選項Async(CancellationToken ct = default)
    {
        var groups = new List<SysDictGroup>();
        var dicts = new List<SysDict>();
        foreach (var summary in await groupService.ListAsync(ct))
        {
            if (!FileTypeGroups.Contains(summary.Group.Code)) continue;
            // 非 Admin 看不到會計支出分類的圖片，篩選裡也不該出現這個分組
            if (summary.Group.Code == "Accounting" && !user.IsInRole("Admin")) continue;
            groups.Add(summary.Group);
            dicts.AddRange(await dictService.ListByGroupAsync(summary.Group.Code, ct));
        }
        return (groups, dicts);
    }

    public async Task<(List<FileItem> Items, int Total)> 列表Async(List<Guid>? types, int page, int pageSize, CancellationToken ct)
    {
        var query = db.FileItems
            .AsNoTracking()
            .Where(f => !f.IsDeleted && (types == null || (f.Type != null && types.Contains(f.Type.Value))))
            .Where(f => user.IsInRole("Admin") || f.TypeDict == null || f.TypeDict.GroupName != "Accounting");
        var total = await query.CountAsync(ct);
        // 批量上傳的時間戳常常相同，加 Id 當次序，分頁才不會重複或漏行
        var items = await query
            .Include(f => f.TypeDict)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<(Stream Content, FileItem File)?> 下载Async(Guid id, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        var file = await db.FileItems.AsNoTracking().Include(f => f.TypeDict).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (file?.TypeDict?.GroupName == "Accounting" && !user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        return file is null ? null : (storage.OpenRead(file.StoredName), file);
    }

    /// <summary>
    /// 按內容（MD5）取文件。上傳已按 MD5 去重，同一個摘要的物理文件唯一，所以回哪一筆的字節都一樣；
    /// 元數據取最早上傳的一筆，讓結果穩定可重現。
    /// </summary>
    public async Task<(Stream Content, FileItem File)?> 按Md5下载Async(string md5, CancellationToken ct = default)
    {
        ct = context.HttpContext?.RequestAborted ?? ct;
        if (md5.Length != 32 || !md5.All(Uri.IsHexDigit)) return null;

        var normalizedMd5 = md5.ToLowerInvariant();
        var query = db.FileItems.AsNoTracking().Include(f => f.TypeDict)
            .Where(f => f.Md5 == normalizedMd5 && !f.IsDeleted);
        if (!user.IsInRole("Admin"))
        {
            query = query.Where(f => f.TypeDict == null || f.TypeDict.GroupName != "Accounting");
        }

        var file = await query.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).FirstOrDefaultAsync(ct);
        return file is null ? null : (storage.OpenRead(file.StoredName), file);
    }

    public async Task 设置类别Async(Guid id, Guid type)
    {
        if (!user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        var ct = context.HttpContext?.RequestAborted ?? default;
        var dict = await dictService.EnsureEnabledAsync(type, "Accounting", "請選擇有效支出分類", ct);
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

        // 按 MD5 去重後，同一個物理文件可能被多筆記錄共用：還有未刪除的記錄在用就先留著，
        // 最後一筆記錄刪掉時才真正刪磁盤文件（file 已軟刪除，查詢過濾會排除它）
        var stillUsed = await db.FileItems.AsNoTracking().AnyAsync(other => other.StoredName == file.StoredName, ct);
        if (!stillUsed) await storage.DeleteAsync(file.StoredName, ct);
        return true;
    }
}
