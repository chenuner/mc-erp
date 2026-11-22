using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Identity.Entities;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Common;

public static class SeedData
{
    public static async Task InitializeSeedDataAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ErpDbContext>();

        await db.Database.MigrateAsync();

        // 分組：先確保程式內建分組存在，再把歷史資料中出現的分組補齊
        var systemGroups = new (string Code, string Name, int Sort)[]
        {
            ("Accounting", "支出分類", 0),
            ("AccountingDocumentType", "票據類型", 1),
            ("AccountingSiteCategory", "地盤分類", 2),
            ("AttendanceCard", "考勤卡圖片", 3),
            ("AttendancePosition", "職位分類", 4)
        };
        var existingGroups = await db.SysDictGroups.ToListAsync();
        foreach (var (code, name, sort) in systemGroups)
        {
            var group = existingGroups.FirstOrDefault(item => item.Code == code);
            if (group is null)
            {
                db.SysDictGroups.Add(new SysDictGroup { Code = code, Name = name, Sort = sort, IsSystem = true });
                continue;
            }

            // 系統分組的名稱及排序由程式決定，方便改名後自動追上
            group.IsSystem = true;
            group.Name = name;
            group.Sort = sort;
        }
        await db.SaveChangesAsync();

        var knownGroupCodes = (await db.SysDictGroups.Select(item => item.Code).ToListAsync()).ToHashSet();
        var dictGroupCodes = await db.SysDicts
            .Select(dict => dict.GroupName).Distinct().ToListAsync();
        foreach (var code in dictGroupCodes.Where(code => !knownGroupCodes.Contains(code)))
            db.SysDictGroups.Add(new SysDictGroup { Code = code, Name = code, Sort = 50 });
        await db.SaveChangesAsync();

        var accountingDicts = new (string Group, string Value, string Label)[]
        {
            ("AccountingDocumentType", "receipt", "收據"),
            ("AccountingDocumentType", "invoice", "發票"),
            ("Accounting", "pending", "待分類"),
            ("Accounting", "materials", "材料"),
            ("Accounting", "transport", "運輸"),
            ("Accounting", "rental", "租賃"),
            ("Accounting", "meals", "餐費"),
            ("Accounting", "fuel", "燃料"),
            ("Accounting", "other", "其他")
        };
        var existing = await db.SysDicts.IgnoreQueryFilters()
            .Where(dict => dict.GroupName == "Accounting" || dict.GroupName == "AccountingDocumentType").ToListAsync();
        foreach (var (group, value, label) in accountingDicts)
            if (!existing.Any(dict => dict.GroupName == group && dict.Value == value))
                db.SysDicts.Add(new SysDict { GroupName = group, Value = value, Label = label, Sort = Array.FindIndex(accountingDicts, item => item.Value == value), Enabled = true });
        await db.SaveChangesAsync();

        // 考勤卡原圖的文件類別：掃描上傳時用它登記到 storage_files，方便在文件模組回看
        if (!await db.SysDicts.IgnoreQueryFilters().AnyAsync(dict => dict.GroupName == "AttendanceCard" && dict.Value == "card"))
            db.SysDicts.Add(new SysDict { GroupName = "AttendanceCard", Value = "card", Label = "考勤卡", Sort = 0, Enabled = true });

        // 職位分類只預置「樓面員工」，其餘由用戶在系統字典維護；不主動回填或改動現有員工
        if (!await db.SysDicts.IgnoreQueryFilters().AnyAsync(dict => dict.GroupName == "AttendancePosition" && dict.Value == "floor"))
            db.SysDicts.Add(new SysDict { GroupName = "AttendancePosition", Value = "floor", Label = "樓面員工", Sort = 1, Enabled = true });
        await db.SaveChangesAsync();

        if (await db.Users.AnyAsync())
            return;

        var initialAdminPassword = sp.GetRequiredService<IConfiguration>()["Bootstrap:AdminPassword"]
            ?? (sp.GetRequiredService<IHostEnvironment>().IsProduction()
                ? throw new InvalidOperationException("Bootstrap:AdminPassword is required for the first production startup")
                : "Admin@123");

        // 創建角色
        var adminRole = new Role { Name = "Admin", Description = "系統管理員" };
        var managerRole = new Role { Name = "Manager", Description = "主管" };
        var workerRole = new Role { Name = "Worker", Description = "普通員工" };
        db.Roles.AddRange(adminRole, managerRole, workerRole);

        // 創建管理員
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var admin = new User
        {
            Username = "admin",
            PasswordHash = hasher.Hash(initialAdminPassword),
            DisplayName = "系統管理員",
            IsActive = true
        };
        db.Users.Add(admin);

        // 分配角色
        db.UserRoles.Add(new UserRole { User = admin, Role = adminRole });

        await db.SaveChangesAsync();
    }
}
