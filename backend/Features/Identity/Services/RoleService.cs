using MC.Erp.Features.Identity.Entities;
using MC.Erp.Features.Identity.Repositories;

namespace MC.Erp.Features.Identity.Services;

public sealed class RoleService(IRoleRepository roleRepo)
{
    public async Task<Role> CreateAsync(string name, string? description, CancellationToken ct = default)
    {
        var existing = await roleRepo.GetByNameAsync(name, ct);
        if (existing is not null)
            throw new InvalidOperationException("角色名已存在");

        var role = new Role { Name = name, Description = description };
        await roleRepo.AddAsync(role, ct);
        return role;
    }

    public async Task<Role?> UpdateAsync(Guid id, string? name, string? description, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        if (role is null)
            return null;

        if (name is not null) role.Name = name;
        if (description is not null) role.Description = description;

        await roleRepo.UpdateAsync(role, ct);
        return role;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        if (role is null)
            return false;

        await roleRepo.DeleteAsync(role, ct);
        return true;
    }

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => roleRepo.GetByIdAsync(id, ct);

    public Task<(List<Role> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => roleRepo.ListAsync(page, pageSize, ct);

    public async Task AssignToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
        => await roleRepo.AssignToUserAsync(userId, roleId, ct);

    public async Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
        => await roleRepo.RemoveFromUserAsync(userId, roleId, ct);
}
