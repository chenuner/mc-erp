namespace MC.Erp.Features.Storage.Services;

/// <summary>
/// 文件存儲抽象。MVP 用本地磁盤實現，未來換對象存儲只需換 DI 註冊。
/// </summary>
public interface IFileStorage
{
    /// <summary>保存文件，返回存儲名及內容 MD5</summary>
    Task<(string StoredName, string Md5)> SaveAsync(Stream content, string originalName, CancellationToken ct);

    /// <summary>打開文件讀流（調用方負責 Dispose）</summary>
    Stream OpenRead(string storedName);

    /// <summary>刪除物理文件</summary>
    Task DeleteAsync(string storedName, CancellationToken ct);
}

/// <summary>
/// 本地磁盤存儲配置
/// </summary>
public sealed class StorageOptions
{
    /// <summary>上傳文件保存目錄（相對進程工作目錄；本地 = backend\storage-files，生產 = /app/storage-files）</summary>
    public string RootPath { get; set; } = "storage-files";

    /// <summary>臨時緩衝目錄（上傳中的大文件、打包中的 ZIP）；不配置則用系統臨時目錄（容器是 /tmp）</summary>
    public string? TempPath { get; set; }
}
