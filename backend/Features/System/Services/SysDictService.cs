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

    /// <summary>
    /// 某一項連同整棵子樹的 id（含自己、含停用項）：列表篩選選上級 = 下級一起查出來。
    /// 識別碼會在換上級時重新編號，所以子樹一律按 ParentId 走，不按識別碼前綴。
    /// </summary>
    public async Task<List<Guid>> SubtreeIdsAsync(Guid rootId, CancellationToken ct = default)
    {
        var root = await repo.GetByIdAsync(rootId, ct);
        if (root is null) return [rootId];
        var items = await repo.ListByGroupAsync(root.GroupName, ct);
        return SubtreeIds(rootId, items);
    }

    /// <summary>純函數版子樹收集（含自己、含停用項）；seen 防環，髒資料成環也不會死循環。</summary>
    public static List<Guid> SubtreeIds(Guid rootId, List<SysDict> items)
    {
        var ids = new List<Guid> { rootId };
        var seen = new HashSet<Guid> { rootId };
        for (var index = 0; index < ids.Count; index++)
        {
            foreach (var child in items.Where(item => item.ParentId == ids[index]))
                if (seen.Add(child.Id)) ids.Add(child.Id);
        }
        return ids;
    }

    /// <summary>
    /// 某一分組的全部字典 id（含停用項）：文件庫按「分組」篩選時整組一起查（分組本身不是字典項，沒有 id）。
    /// </summary>
    public async Task<List<Guid>> GroupDictIdsAsync(string groupName, CancellationToken ct = default)
        => (await repo.ListByGroupAsync(groupName, ct)).Select(dict => dict.Id).ToList();

    /// <summary>
    /// 寫入用：分類必須存在、屬於指定分組、自己啟用，而且每個上級都啟用。
    /// 上級停用代表整條分支都不再使用，所以下級一併不可選（歷史資料照樣顯示，不影響查詢與匯出）。
    /// </summary>
    public async Task<SysDict> EnsureEnabledAsync(Guid id, string? groupName, string message,
        CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null || !dict.Enabled || (groupName is not null && dict.GroupName != groupName))
            throw new InvalidOperationException(message);

        var byId = (await repo.ListByGroupAsync(dict.GroupName, ct)).ToDictionary(item => item.Id);
        var seen = new HashSet<Guid> { dict.Id };
        var parentId = dict.ParentId;
        while (parentId is Guid current && seen.Add(current) && byId.TryGetValue(current, out var parent))
        {
            if (!parent.Enabled)
                throw new InvalidOperationException($"上級分類「{parent.Label}」已停用，請先啟用上級或改選其他分類");
            parentId = parent.ParentId;
        }
        return dict;
    }

    public async Task<SysDict> CreateAsync(string groupName, string label, string? value = null, int? sort = null,
        bool? enabled = null, string? remark = null, Guid? parentId = null, CancellationToken ct = default)
    {
        groupName = (groupName ?? string.Empty).Trim();
        label = (label ?? string.Empty).Trim();
        if (label.Length == 0) throw new InvalidOperationException("請輸入標籤");
        var group = await groups.GetByCodeAsync(groupName, ct)
            ?? throw new InvalidOperationException("分組不存在，請先建立分組");

        var items = await repo.ListByGroupAsync(groupName, ct);
        var parent = parentId is null ? null : await EnsureParentAsync(parentId.Value, groupName, Guid.Empty, items, ct);
        value = string.IsNullOrWhiteSpace(value) ? NextValue(items, parent) : value.Trim();
        if (items.Any(dict => dict.Value == value))
            throw new InvalidOperationException($"分組「{group.Name}」下已存在識別碼「{value}」");

        var dict = new SysDict
        {
            GroupName = groupName,
            Label = label,
            Value = value,
            ParentId = parent?.Id,
            Sort = sort ?? NextSort(items, parent?.Id),
            Enabled = enabled ?? true,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()
        };

        await repo.AddAsync(dict, ct);
        return dict;
    }

    public async Task<SysDict?> UpdateAsync(Guid id, string? label = null, string? value = null, int? sort = null,
        bool? enabled = null, string? remark = null, bool clearParent = false, Guid? parentId = null,
        CancellationToken ct = default)
    {
        var dict = await repo.GetByIdAsync(id, ct);
        if (dict is null) return null;

        if (label is not null)
        {
            var trimmed = label.Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException("請輸入標籤");
            dict.Label = trimmed;
        }

        // 識別碼由系統按層級生成（頂層 1、2，下級 1.1、1.2），不支援手動修改
        if (value is not null && value.Trim() != dict.Value)
            throw new InvalidOperationException("識別碼由系統自動生成，不可修改");

        // 換上級：clearParent 為 true 表示改為頂層，否則 parentId 有值才處理
        if (clearParent || parentId is Guid)
        {
            var items = await repo.ListByGroupAsync(dict.GroupName, ct);
            var parent = parentId is Guid targetParentId
                ? await EnsureParentAsync(targetParentId, dict.GroupName, dict.Id, items, ct)
                : null;
            if (parent?.Id != dict.ParentId)
            {
                // 有下級的項不搬動，只可以拆掉自己的上級，避免整棵子樹跟着跑
                if (parent is not null && items.Any(item => item.ParentId == dict.Id))
                    throw new InvalidOperationException("此項下還有下級，不能再歸到其他上級之下；請先把它們移走");
                dict.ParentId = parent?.Id;
                // 換層級後識別碼要跟着重新編號，否則 1.2 掛到別處會和同層衝突
                dict.Value = NextValue(items.Where(item => item.Id != dict.Id).ToList(), parent);
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
        // 上級刪掉會令下級變成孤兒，先讓用戶自己決定下級去哪
        var children = await repo.ListByGroupAsync(dict.GroupName, ct);
        if (children.Any(item => item.ParentId == dict.Id))
            throw new InvalidOperationException("此項下還有下級，請先移走或刪除下級再刪除");

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

    private static string NextValue(List<SysDict> items, SysDict? parent)
    {
        // 頂層：1、2、3…；下級：跟在上級識別碼後面（1.1、1.2…），一看就知道屬於誰
        if (parent is null)
        {
            var max = items
                .Where(dict => dict.ParentId is null)
                .Select(dict => int.TryParse(dict.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
                .DefaultIfEmpty(0)
                .Max();
            return (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        var siblings = items.Where(dict => dict.ParentId == parent.Id).ToList();
        var prefix = parent.Value + ".";
        var next = siblings
            .Select(dict => dict.Value.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(dict.Value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return prefix + next.ToString(CultureInfo.InvariantCulture);
    }

    private static int NextSort(List<SysDict> items, Guid? parentId)
    {
        var siblings = items.Where(dict => dict.ParentId == parentId).ToList();
        return siblings.Count == 0 ? 1 : siblings.Max(dict => dict.Sort) + 1;
    }

    /// <summary>上級必須存在、同一分組，而且不能形成環（候選上級自己就是自身下級時拒絕）。</summary>
    private async Task<SysDict> EnsureParentAsync(Guid parentId, string groupName, Guid dictId, List<SysDict> items,
        CancellationToken ct)
    {
        var parent = await repo.GetByIdAsync(parentId, ct)
            ?? throw new InvalidOperationException("上級不存在，請重新載入後重試");
        if (parent.GroupName != groupName)
            throw new InvalidOperationException("上級必須屬於同一分組");

        if (dictId == Guid.Empty) return parent;

        // 從候選上級往上走，走回自己就代表成環
        var visited = new HashSet<Guid>();
        for (var cursor = parent; cursor is not null && visited.Add(cursor.Id);)
        {
            if (cursor.Id == dictId)
                throw new InvalidOperationException("不能把字典項歸到它自己的下級之下");
            cursor = cursor.ParentId is { } ancestorId
                ? items.FirstOrDefault(item => item.Id == ancestorId)
                  ?? await repo.GetByIdAsync(ancestorId, ct)
                : null;
        }
        return parent;
    }
}
