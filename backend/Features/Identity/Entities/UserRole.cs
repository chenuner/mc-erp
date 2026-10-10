using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Identity.Entities;

public sealed class UserRole : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
