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
    /// <summary>存儲根目錄（相對於應用工作目錄，可用絕對路徑覆蓋）</summary>
    public string RootPath { get; set; } = "storage-files";
}
