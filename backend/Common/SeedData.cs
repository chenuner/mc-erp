using MC.Erp.Common.Security;
using MC.Erp.Data;
using MC.Erp.Features.Identity.Entities;
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

        if (await db.Users.AnyAsync())
            return;

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
            PasswordHash = hasher.Hash("Admin@123"),
            DisplayName = "系統管理員",
            IsActive = true
        };
        db.Users.Add(admin);

        // 分配角色
        db.UserRoles.Add(new UserRole { User = admin, Role = adminRole });

        await db.SaveChangesAsync();
    }
}
