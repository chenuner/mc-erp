using MC.Erp.Features.System.Entities;

namespace MC.Erp.Features.System.Repositories;

public interface ISysDictRepository
{
    Task<SysDict?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<SysDict>> ListByGroupAsync(string groupName, CancellationToken ct = default);
    Task<(List<SysDict> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(SysDict dict, CancellationToken ct = default);
    Task UpdateAsync(SysDict dict, CancellationToken ct = default);
    Task DeleteAsync(SysDict dict, CancellationToken ct = default);
}
