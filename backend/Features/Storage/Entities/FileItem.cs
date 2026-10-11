using MC.Erp.Common.Domain;
using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.Storage.Entities;

/// <summary>
/// 上傳的文件（元數據在庫，內容走 IFileStorage）
/// </summary>
public sealed class FileItem : AuditableEntity
{
    /// <summary>文件類別的系統字典 ID（外鍵）；歷史文件可為空。</summary>
    public Guid? Type { get; set; }

    /// <summary>
    /// 文件類別的系統字典導航屬性，用於讀取標籤等資訊，不新增資料庫欄位。
    /// 数据库表没有这个字段，它是一个导航属性，用于在查询时加载相关的 SysDict 实体。
    /// </summary>
    public SysDict? TypeDict { get; set; }

    /// <summary>上傳時的原始文件名，用於顯示及下載命名。</summary>
    public string OriginalName { get; set; } = null!;

    /// <summary>磁盤存儲名（Guid + 清洗後的擴展名），用於定位實際文件。</summary>
    public string StoredName { get; set; } = null!;

    /// <summary>文件內容的 MD5 摘要（32 位小寫十六進制），不是文件內容；歷史文件可為空。</summary>
    public string? Md5 { get; set; }

    /// <summary>文件的 MIME 類型（如 image/jpeg），用於預覽及下載響應，與字典類別無關。</summary>
    public string ContentType { get; set; } = null!;

    /// <summary>文件大小，單位為位元組。</summary>
    public long SizeBytes { get; set; }

    /// <summary>上傳人的用戶 ID；未取得用戶身份時可為空。</summary>
    public Guid? UploaderId { get; set; }

    /// <summary>上傳時記錄的用戶名；未取得用戶名時可為空。</summary>
    public string? UploaderName { get; set; }
}
