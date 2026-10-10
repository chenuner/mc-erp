using System.IO;
using Microsoft.Extensions.Options;

namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 本地磁盤存儲實現。文件名用 Guid 防衝突與路徑穿越，擴展名白名單清洗。
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private string RootPath => Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(Stream content, string originalName, CancellationToken ct)
    {
        var storedName = $"{Guid.NewGuid():N}{SanitizeExtension(originalName)}";
        Directory.CreateDirectory(RootPath);

        await using var output = File.Create(Path.Combine(RootPath, storedName));
        await content.CopyToAsync(output, ct);
        return storedName;
    }

    public Stream OpenRead(string storedName)
    {
        return File.OpenRead(Path.Combine(RootPath, storedName));
    }

    public Task DeleteAsync(string storedName, CancellationToken ct)
    {
        var path = Path.Combine(RootPath, storedName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    /// <summary>擴展名清洗：只留字母數字，長度限制，防路徑穿越</summary>
    private static string SanitizeExtension(string originalName)
    {
        var ext = Path.GetExtension(originalName).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) || ext.Length > 10 || !ext.All(char.IsLetterOrDigit)
            ? string.Empty
            : $".{ext}";
    }
}
