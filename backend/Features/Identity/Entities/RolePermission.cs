using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Identity.Entities;

public sealed class RolePermission : Entity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
