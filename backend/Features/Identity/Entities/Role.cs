using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Identity.Entities;

public sealed class Role : AuditableEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];
    public List<RolePermission> RolePermissions { get; set; } = [];
}
