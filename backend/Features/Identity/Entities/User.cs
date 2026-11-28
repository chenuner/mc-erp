using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Identity.Entities;

public sealed class User : AuditableEntity
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>受保護帳號：唯一的主管理員，不可刪除、不可停用、不可移除 Admin 角色。</summary>
    public bool IsProtected { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];
}
