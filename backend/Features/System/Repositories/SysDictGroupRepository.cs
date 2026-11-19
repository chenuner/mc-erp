using MC.Erp.Data;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.System.Repositories;

public sealed class SysDictGroupRepository(ErpDbContext db) : ISysDictGroupRepository
{
    public async Task<List<SysDictGroupSummary>> ListAsync(CancellationToken ct = default)
    {
        return await db.SysDictGroups
            .OrderBy(g => g.Sort)
            .ThenBy(g => g.Name)
            .Select(g => new SysDictGroupSummary(
                g,
                db.SysDicts.Count(d => d.GroupName == g.Code),
                db.SysDicts.Count(d => d.GroupName == g.Code && d.Enabled)))
            .ToListAsync(ct);
    }

    public Task<SysDictGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.SysDictGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<SysDictGroup?> GetByCodeAsync(string code, CancellationToken ct = default)
        => db.SysDictGroups.FirstOrDefaultAsync(g => g.Code == code, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => db.SysDictGroups.AnyAsync(g => g.Code == code, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
        => db.SysDictGroups.AnyAsync(g => g.Name == name && (excludeId == null || g.Id != excludeId), ct);

    public async Task<int> NextSortAsync(CancellationToken ct = default)
    {
        var max = await db.SysDictGroups.MaxAsync(g => (int?)g.Sort, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(SysDictGroup group, CancellationToken ct = default)
    {
        await db.SysDictGroups.AddAsync(group, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SysDictGroup group, CancellationToken ct = default)
    {
        group.UpdatedAt = DateTime.UtcNow;
        db.SysDictGroups.Update(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(SysDictGroup group, CancellationToken ct = default)
    {
        group.IsDeleted = true;
        group.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
