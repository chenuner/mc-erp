using MC.Erp.Features.Identity.Entities;

namespace MC.Erp.Features.Identity.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<(List<Role> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    Task UpdateAsync(Role role, CancellationToken ct = default);
    Task DeleteAsync(Role role, CancellationToken ct = default);
    Task AssignToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task<List<string>> GetRoleNamesAsync(Guid userId, CancellationToken ct = default);
}
