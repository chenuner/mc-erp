using MC.Erp.Common.Domain;

namespace MC.Erp.Features.Identity.Entities;

public sealed class User : AuditableEntity
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;

    public List<UserRole> UserRoles { get; set; } = [];
}
