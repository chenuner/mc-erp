using MC.Erp.Data;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.System.Repositories;

public sealed class SysDictRepository(ErpDbContext db) : ISysDictRepository
{
    public Task<SysDict?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.SysDicts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<List<SysDict>> ListByGroupAsync(string groupName, CancellationToken ct = default)
    {
        return await db.SysDicts
            .Where(d => d.GroupName == groupName)
            .OrderBy(d => d.Sort)
            .ToListAsync(ct);
    }

    public async Task<(List<SysDict> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await db.SysDicts.CountAsync(ct);
        var items = await db.SysDicts
            .OrderBy(d => d.GroupName)
            .ThenBy(d => d.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAsync(SysDict dict, CancellationToken ct = default)
    {
        await db.SysDicts.AddAsync(dict, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SysDict dict, CancellationToken ct = default)
    {
        dict.UpdatedAt = DateTime.UtcNow;
        db.SysDicts.Update(dict);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(SysDict dict, CancellationToken ct = default)
    {
        dict.IsDeleted = true;
        dict.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
