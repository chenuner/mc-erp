using MC.Erp.Common.Domain;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Storage.Entities;

/// <summary>
/// 上傳的文件（元數據在庫，內容走 IFileStorage）
/// </summary>
public sealed class FileItem : AuditableEntity
{
    public Guid? Type { get; set; }
    public SysDict? TypeDict { get; set; }

    public string OriginalName { get; set; } = null!;  // 原始文件名
    public string StoredName { get; set; } = null!;     // 磁盤存儲名（Guid + 擴展名）
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }

    public Guid? UploaderId { get; set; }   // 上傳人（UserId）
    public string? UploaderName { get; set; }
}
