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
