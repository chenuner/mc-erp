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
        // 不 Include Parent：上級與下級在同一個分組的清單裡，跟蹤查詢會自動補好導覽
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
        if (dict.GroupName is "Accounting" or "AccountingDocumentType" or "AccountingSiteCategory")
        {
            if (await db.FileItems.IgnoreQueryFilters().AnyAsync(file => file.Type == dict.Id, ct)
                || await db.Receipts.IgnoreQueryFilters().AnyAsync(receipt => receipt.Type == dict.Id
                    || receipt.DocumentTypeDictId == dict.Id || receipt.SiteCategoryDictId == dict.Id, ct))
                throw new InvalidOperationException("分類已被會計票據引用，請停用而不要刪除");
        }
        // 薪酬表的職位是歷史快照（標籤已複製），刪字典不影響已生成的月份；只有現職員工引用時才阻止刪除
        if (dict.GroupName == "AttendancePosition"
            && await db.Workers.IgnoreQueryFilters().AnyAsync(worker => worker.PositionDictId == dict.Id, ct))
            throw new InvalidOperationException("職位分類已被員工引用，請停用而不要刪除");
        // 地盤是考勤和會計共用的分組：被打卡明細引用時不能刪，否則明細會跟着消失；要隱藏請停用
        if (dict.GroupName == "AccountingSiteCategory"
            && await db.AttendanceRecords.IgnoreQueryFilters().AnyAsync(record => record.SiteDictId == dict.Id, ct))
            throw new InvalidOperationException("地盤已被打卡明細引用，請停用而不要刪除");
        dict.IsDeleted = true;
        dict.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
