using System.Globalization;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Repositories;

namespace MC.Erp.Features.System.Services;

public sealed class SysDictService(ISysDictRepository repo, ISysDictGroupRepository groups)
{
    public Task<SysDict?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => repo.GetByIdAsync(id, ct);

    public Task<List<SysDict>> ListByGroupAsync(string groupName, CancellationToken ct = default)
        => repo.ListByGroupAsync(groupName, ct);

    public Task<(List<SysDict> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => repo.ListAsync(page, pageSize, ct);

    public async Task<SysDict> CreateAsync(string groupName, string label, string? value = null, int? sort = null,
        bool? enabled = null, string? remark = null, CancellationToken ct = default)
    {
        groupName = (groupName ?? string.Empty).Trim();
        label = (label ?? string.Empty).Trim();
        if (label.Length == 0) throw new InvalidOperationException("請輸入標籤");
        var group = await groups.GetByCodeAsync(groupName, ct)
            ?? throw new InvalidOperationException("分組不存在，請先建立分組");

        var items = await repo.ListByGroupAsync(groupName, ct);
        value = string.IsNullOrWhiteSpace(value) ? NextValue(items) : value.Trim();
        if (items.Any(dict => dict.Value == value))
            throw new InvalidOperationException($"分組「{group.Name}」下已存在識別碼「{value}」");

        var dict = new SysDict
        {
            GroupName = groupName,
            Label = label,
            Value = value,
            Sort = sort ?? NextSort(items),
            Enabled = enabled ?? true,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()
        };

        await repo.AddAsync(dict, ct);
        return dict;
    }

    public async Task<SysDict?> UpdateAsync(Guid id, string? label = null, string? value = null, int? sort = null,
        bool? enabled = null, string? remark = null, CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null) return null;

        if (label is not null)
        {
            var trimmed = label.Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException("請輸入標籤");
            dict.Label = trimmed;
        }

        if (value is not null)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException("識別碼不能為空");
            if (trimmed != dict.Value)
            {
                var group = await groups.GetByCodeAsync(dict.GroupName, ct);
                if (group?.IsSystem == true)
                    throw new InvalidOperationException("系統分組的識別碼不可修改");
                var items = await repo.ListByGroupAsync(dict.GroupName, ct);
                if (items.Any(item => item.Id != id && item.Value == trimmed))
                    throw new InvalidOperationException($"分組下已存在識別碼「{trimmed}」");
                dict.Value = trimmed;
            }
        }

        if (sort is not null) dict.Sort = sort.Value;
        if (enabled is not null)
        {
            if (enabled.Value is false && dict.Enabled)
                await EnsureCanDisableAsync(dict, ct);
            dict.Enabled = enabled.Value;
        }
        if (remark is not null) dict.Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();

        await repo.UpdateAsync(dict, ct);
        return dict;
    }

    public async Task<List<SysDict>> ReorderAsync(string groupName, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var items = await repo.ListByGroupAsync((groupName ?? string.Empty).Trim(), ct);
        var itemIds = items.Select(item => item.Id).ToHashSet();
        if (ids.Count != items.Count || ids.Distinct().Count() != ids.Count || !ids.All(itemIds.Contains))
            throw new InvalidOperationException("排序清單與分組字典項不一致，請重新載入後重試");

        var byId = items.ToDictionary(item => item.Id);
        for (var index = 0; index < ids.Count; index++)
        {
            var item = byId[ids[index]];
            item.Sort = index + 1;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await repo.SaveAsync(ct);
        return items.OrderBy(item => item.Sort).ToList();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null) return false;
        if (IsUploadPending(dict))
            throw new InvalidOperationException("「待分類」是票據上傳流程的必要項，不能刪除；如不需要可在其他分類中處理");
        if (dict.Enabled && dict.GroupName == AccountingDocumentTypeGroup)
        {
            var others = await repo.ListByGroupAsync(dict.GroupName, ct);
            if (!others.Any(item => item.Id != dict.Id && item.Enabled))
                throw new InvalidOperationException("票據類型至少要保留一個啟用項");
        }

        await repo.DeleteAsync(dict, ct);
        return true;
    }

    private const string AccountingDocumentTypeGroup = "AccountingDocumentType";

    private static bool IsUploadPending(SysDict dict)
        => dict.GroupName == "Accounting" && dict.Value == "pending";

    private async Task EnsureCanDisableAsync(SysDict dict, CancellationToken ct)
    {
        if (IsUploadPending(dict))
            throw new InvalidOperationException("「待分類」是票據上傳流程的必要項，不能停用");

        if (dict.GroupName != AccountingDocumentTypeGroup) return;
        var items = await repo.ListByGroupAsync(dict.GroupName, ct);
        if (!items.Any(item => item.Id != dict.Id && item.Enabled))
            throw new InvalidOperationException("票據類型至少要保留一個啟用項");
    }

    private static string NextValue(List<SysDict> items)
    {
        var max = items
            .Select(dict => int.TryParse(dict.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static int NextSort(List<SysDict> items)
        => items.Count == 0 ? 1 : items.Max(dict => dict.Sort) + 1;
}
