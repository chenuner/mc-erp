using MC.Erp.Data;
using MC.Erp.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Identity.Repositories;

public sealed class RoleRepository(ErpDbContext db) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
        => db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<(List<Role> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await db.Roles.CountAsync(ct);
        var items = await db.Roles
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAsync(Role role, CancellationToken ct = default)
    {
        await db.Roles.AddAsync(role, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Role role, CancellationToken ct = default)
    {
        role.UpdatedAt = DateTime.UtcNow;
        db.Roles.Update(role);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Role role, CancellationToken ct = default)
    {
        role.IsDeleted = true;
        role.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        await db.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = roleId }, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        var ur = await db.UserRoles.FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct);
        if (ur is not null)
        {
            db.UserRoles.Remove(ur);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<string>> GetRoleNamesAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync(ct);
    }
}
