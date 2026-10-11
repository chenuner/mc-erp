using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Repositories;

namespace MC.Erp.Features.System.Services;

public sealed class SysDictService(ISysDictRepository repo)
{
    public Task<SysDict?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => repo.GetByIdAsync(id, ct);

    public Task<List<SysDict>> ListByGroupAsync(string groupName, CancellationToken ct = default)
        => repo.ListByGroupAsync(groupName, ct);

    public Task<(List<SysDict> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => repo.ListAsync(page, pageSize, ct);

    public async Task<SysDict> CreateAsync(string groupName, string label, string value, int sort = 0, bool enabled = true, string? remark = null, CancellationToken ct = default)
    {
        var dict = new SysDict
        {
            GroupName = groupName,
            Label = label,
            Value = value,
            Sort = sort,
            Enabled = enabled,
            Remark = remark
        };

        await repo.AddAsync(dict, ct);
        return dict;
    }

    public async Task<SysDict?> UpdateAsync(Guid id, string? label = null, string? value = null, int? sort = null, bool? enabled = null, string? remark = null, CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null) return null;
        if (label is not null) dict.Label = label;
        if (value is not null) dict.Value = value;
        if (sort is not null) dict.Sort = sort.Value;
        if (enabled is not null) dict.Enabled = enabled.Value;
        if (remark is not null) dict.Remark = remark;

        await repo.UpdateAsync(dict, ct);
        return dict;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null) return false;
        await repo.DeleteAsync(dict, ct);
        return true;
    }
}
