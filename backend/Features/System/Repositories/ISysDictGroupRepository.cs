using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.System.Repositories;

/// <summary>分組及其字典項數量。</summary>
public sealed record SysDictGroupSummary(SysDictGroup Group, int ItemCount, int EnabledCount);

public interface ISysDictGroupRepository
{
    Task<List<SysDictGroupSummary>> ListAsync(CancellationToken ct = default);
    Task<SysDictGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SysDictGroup?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken ct = default);
    Task<int> NextSortAsync(CancellationToken ct = default);
    Task AddAsync(SysDictGroup group, CancellationToken ct = default);
    Task UpdateAsync(SysDictGroup group, CancellationToken ct = default);
    Task DeleteAsync(SysDictGroup group, CancellationToken ct = default);
}
