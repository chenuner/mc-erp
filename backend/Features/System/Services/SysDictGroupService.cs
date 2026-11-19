using System.Text;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Repositories;

namespace MC.Erp.Features.System.Services;

public sealed class SysDictGroupService(ISysDictGroupRepository repo, ISysDictRepository dicts)
{
    public Task<List<SysDictGroupSummary>> ListAsync(CancellationToken ct = default)
        => repo.ListAsync(ct);

    public async Task<SysDictGroup> CreateAsync(string name, string? code = null, int? sort = null, string? remark = null, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("請輸入分組名稱");
        if (await repo.NameExistsAsync(name, ct: ct)) throw new InvalidOperationException($"分組「{name}」已存在");

        var explicitCode = !string.IsNullOrWhiteSpace(code);
        var normalizedCode = explicitCode ? code!.Trim() : Slug(name);
        if (normalizedCode.Length is 0 or > 50 || !normalizedCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new InvalidOperationException("分組識別碼只能使用英文字母、數字、- 和 _，最多 50 字");

        if (explicitCode)
        {
            if (await repo.CodeExistsAsync(normalizedCode, ct))
                throw new InvalidOperationException($"分組識別碼「{normalizedCode}」已存在");
        }
        else
        {
            normalizedCode = await UniqueCodeAsync(normalizedCode, ct);
        }

        var group = new SysDictGroup
        {
            Code = normalizedCode,
            Name = name,
            Sort = sort ?? await repo.NextSortAsync(ct),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
            IsSystem = false
        };

        await repo.AddAsync(group, ct);
        return group;
    }

    public async Task<SysDictGroup?> UpdateAsync(Guid id, string? name = null, int? sort = null, string? remark = null, CancellationToken ct = default)
    {
        var group = await repo.GetByIdAsync(id, ct);
        if (group is null) return null;

        if (name is not null)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException("請輸入分組名稱");
            if (trimmed != group.Name && await repo.NameExistsAsync(trimmed, id, ct))
                throw new InvalidOperationException($"分組「{trimmed}」已存在");
            group.Name = trimmed;
        }

        if (sort is not null) group.Sort = sort.Value;
        if (remark is not null) group.Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();

        await repo.UpdateAsync(group, ct);
        return group;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var group = await repo.GetByIdAsync(id, ct);
        if (group is null) return false;
        if (group.IsSystem) throw new InvalidOperationException("此分組由系統使用，不可刪除");
        if ((await dicts.ListByGroupAsync(group.Code, ct)).Count > 0)
            throw new InvalidOperationException("分組下仍有字典項，請先刪除或移走字典項");

        await repo.DeleteAsync(group, ct);
        return true;
    }

    private async Task<string> UniqueCodeAsync(string code, CancellationToken ct)
    {
        var candidate = code;
        for (var suffix = 2; await repo.CodeExistsAsync(candidate, ct); suffix++)
            candidate = $"{code[..Math.Min(code.Length, 44)]}-{suffix}";
        return candidate;
    }

    /// <summary>把名稱轉成可讀的 ASCII 識別碼；純中文名稱回退成 group。</summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var value = builder.ToString().Trim('-');
        return value.Length == 0 ? "group" : value[..Math.Min(value.Length, 40)];
    }
}
