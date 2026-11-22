using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <summary>
    /// 移除所有外鍵約束（專案規定：資料庫不建 FOREIGN KEY）。
    /// EF 的導航屬性和 Include 聯表靠模型生成 SQL，不依賴資料庫約束；
    /// 拿掉約束之後清表、刪資料不用再管順序。模型本身沒有改動，所以這是純 SQL 的 migration。
    /// </summary>
    [DbContext(typeof(Data.ErpDbContext))]
    [Migration("20261122091500_DropAllForeignKeys")]
    public partial class DropAllForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            ALTER TABLE identity_user_roles DROP CONSTRAINT IF EXISTS "FK_identity_user_roles_identity_users_UserId";
            ALTER TABLE identity_user_roles DROP CONSTRAINT IF EXISTS "FK_identity_user_roles_identity_roles_RoleId";
            ALTER TABLE identity_role_permissions DROP CONSTRAINT IF EXISTS "FK_identity_role_permissions_identity_roles_RoleId";
            ALTER TABLE identity_role_permissions DROP CONSTRAINT IF EXISTS "FK_identity_role_permissions_identity_permissions_PermissionId";
            ALTER TABLE attendance_records DROP CONSTRAINT IF EXISTS "FK_attendance_records_attendance_workers_WorkerId";
            ALTER TABLE attendance_records DROP CONSTRAINT IF EXISTS "FK_attendance_records_attendance_sites_SiteId";
            ALTER TABLE attendance_monthly_summaries DROP CONSTRAINT IF EXISTS "FK_attendance_monthly_summaries_attendance_workers_WorkerId";
            ALTER TABLE attendance_card_punches DROP CONSTRAINT IF EXISTS "FK_attendance_card_punches_attendance_card_scans_ScanId";
            ALTER TABLE attendance_card_scans DROP CONSTRAINT IF EXISTS "FK_attendance_card_scans_storage_files_FileItemId";
            ALTER TABLE storage_files DROP CONSTRAINT IF EXISTS "FK_storage_files_sys_dict_Type";
            ALTER TABLE accounting_receipts DROP CONSTRAINT IF EXISTS "FK_accounting_receipts_storage_files_FileItemId";
            ALTER TABLE accounting_receipts DROP CONSTRAINT IF EXISTS "FK_accounting_receipts_sys_dict_DocumentTypeDictId";
            ALTER TABLE accounting_receipts DROP CONSTRAINT IF EXISTS "FK_accounting_receipts_sys_dict_SiteCategoryDictId";
            ALTER TABLE accounting_receipts DROP CONSTRAINT IF EXISTS "FK_accounting_receipts_sys_dict_Type";
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            ALTER TABLE identity_user_roles ADD CONSTRAINT "FK_identity_user_roles_identity_users_UserId"
                FOREIGN KEY ("UserId") REFERENCES identity_users ("Id") ON DELETE CASCADE;
            ALTER TABLE identity_user_roles ADD CONSTRAINT "FK_identity_user_roles_identity_roles_RoleId"
                FOREIGN KEY ("RoleId") REFERENCES identity_roles ("Id") ON DELETE CASCADE;
            ALTER TABLE identity_role_permissions ADD CONSTRAINT "FK_identity_role_permissions_identity_roles_RoleId"
                FOREIGN KEY ("RoleId") REFERENCES identity_roles ("Id") ON DELETE CASCADE;
            ALTER TABLE identity_role_permissions ADD CONSTRAINT "FK_identity_role_permissions_identity_permissions_PermissionId"
                FOREIGN KEY ("PermissionId") REFERENCES identity_permissions ("Id") ON DELETE CASCADE;
            ALTER TABLE attendance_records ADD CONSTRAINT "FK_attendance_records_attendance_workers_WorkerId"
                FOREIGN KEY ("WorkerId") REFERENCES attendance_workers ("Id") ON DELETE CASCADE;
            ALTER TABLE attendance_records ADD CONSTRAINT "FK_attendance_records_attendance_sites_SiteId"
                FOREIGN KEY ("SiteId") REFERENCES attendance_sites ("Id") ON DELETE CASCADE;
            ALTER TABLE attendance_monthly_summaries ADD CONSTRAINT "FK_attendance_monthly_summaries_attendance_workers_WorkerId"
                FOREIGN KEY ("WorkerId") REFERENCES attendance_workers ("Id") ON DELETE CASCADE;
            ALTER TABLE attendance_card_punches ADD CONSTRAINT "FK_attendance_card_punches_attendance_card_scans_ScanId"
                FOREIGN KEY ("ScanId") REFERENCES attendance_card_scans ("Id") ON DELETE CASCADE;
            ALTER TABLE attendance_card_scans ADD CONSTRAINT "FK_attendance_card_scans_storage_files_FileItemId"
                FOREIGN KEY ("FileItemId") REFERENCES storage_files ("Id") ON DELETE RESTRICT;
            ALTER TABLE storage_files ADD CONSTRAINT "FK_storage_files_sys_dict_Type"
                FOREIGN KEY ("Type") REFERENCES sys_dict ("Id") ON DELETE RESTRICT;
            ALTER TABLE accounting_receipts ADD CONSTRAINT "FK_accounting_receipts_storage_files_FileItemId"
                FOREIGN KEY ("FileItemId") REFERENCES storage_files ("Id") ON DELETE RESTRICT;
            ALTER TABLE accounting_receipts ADD CONSTRAINT "FK_accounting_receipts_sys_dict_DocumentTypeDictId"
                FOREIGN KEY ("DocumentTypeDictId") REFERENCES sys_dict ("Id") ON DELETE RESTRICT;
            ALTER TABLE accounting_receipts ADD CONSTRAINT "FK_accounting_receipts_sys_dict_SiteCategoryDictId"
                FOREIGN KEY ("SiteCategoryDictId") REFERENCES sys_dict ("Id") ON DELETE RESTRICT;
            ALTER TABLE accounting_receipts ADD CONSTRAINT "FK_accounting_receipts_sys_dict_Type"
                FOREIGN KEY ("Type") REFERENCES sys_dict ("Id") ON DELETE RESTRICT;
            """);
    }
}
