using MC.Erp.Features.Attendance.Entities;
using MC.Erp.Features.Identity.Entities;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.System.Entities;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Data;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Attendance
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<MonthlyWorkSummary> MonthlyWorkSummaries => Set<MonthlyWorkSummary>();

    // System（歸檔分類字典，跨模組共享）
    public DbSet<ArchiveCategory> ArchiveCategories => Set<ArchiveCategory>();

    // Storage
    public DbSet<FileItem> FileItems => Set<FileItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity 模組
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("identity_users");
            e.HasIndex(u => u.Username).IsUnique();
            e.HasQueryFilter(u => !u.IsDeleted);
            e.HasMany(u => u.UserRoles)
                .WithOne(ur => ur.User)
                .HasForeignKey(ur => ur.UserId);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("identity_roles");
            e.HasIndex(r => r.Name).IsUnique();
            e.HasQueryFilter(r => !r.IsDeleted);
            e.HasMany(r => r.UserRoles)
                .WithOne(ur => ur.Role)
                .HasForeignKey(ur => ur.RoleId);
            e.HasMany(r => r.RolePermissions)
                .WithOne(rp => rp.Role)
                .HasForeignKey(rp => rp.RoleId);
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("identity_permissions");
            e.HasIndex(p => p.Name).IsUnique();
            e.HasMany(p => p.RolePermissions)
                .WithOne(rp => rp.Permission)
                .HasForeignKey(rp => rp.PermissionId);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("identity_user_roles");
            e.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("identity_role_permissions");
            e.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
        });

        // Attendance 模組
        modelBuilder.Entity<Worker>(e =>
        {
            e.ToTable("attendance_workers");
            e.HasQueryFilter(w => !w.IsDeleted);
            e.HasIndex(w => w.EmployeeId);
            e.HasMany(w => w.Records)
                .WithOne(r => r.Worker)
                .HasForeignKey(r => r.WorkerId);
            e.HasMany(w => w.Summaries)
                .WithOne(s => s.Worker)
                .HasForeignKey(s => s.WorkerId);
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.ToTable("attendance_sites");
            e.HasQueryFilter(s => !s.IsDeleted);
            e.HasIndex(s => s.Name);
            e.HasMany(s => s.Records)
                .WithOne(r => r.Site)
                .HasForeignKey(r => r.SiteId);
        });

        modelBuilder.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("attendance_records");
            e.HasIndex(r => new { r.WorkerId, r.ClockInTime });
        });

        modelBuilder.Entity<MonthlyWorkSummary>(e =>
        {
            e.ToTable("attendance_monthly_summaries");
            e.HasIndex(s => new { s.WorkerId, s.Year, s.Month }).IsUnique();
        });

        // System 模組：歸檔分類字典（跨模組共享）
        modelBuilder.Entity<ArchiveCategory>(e =>
        {
            e.ToTable("system_archive_categories");
            e.HasQueryFilter(c => !c.IsDeleted);
            e.HasIndex(c => c.SiteName);
            e.HasIndex(c => c.EventDate);
        });

        // Storage 模組
        modelBuilder.Entity<FileItem>(e =>
        {
            e.ToTable("storage_files");
            e.HasQueryFilter(f => !f.IsDeleted);
            e.HasIndex(f => f.CategoryId);
            e.HasIndex(f => f.CreatedAt);
            e.HasOne(f => f.Category)
                .WithMany()
                .HasForeignKey(f => f.CategoryId);
        });
    }
}
