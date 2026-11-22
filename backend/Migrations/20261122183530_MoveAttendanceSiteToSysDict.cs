using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <summary>
    /// 地盤統一用系統字典 <c>AccountingSiteCategory</c>（顯示名「地盤分類」），考勤和會計共用同一份名單：
    /// 舊 attendance_sites 的資料原樣搬成字典項（字典項 Id 沿用舊地盤 Id，打卡明細的 SiteId 不用改就對得上），
    /// 順手清掉測試用的「123」分類，再刪掉舊表。
    /// </summary>
    public partial class MoveAttendanceSiteToSysDict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. 先清掉測試用的「123」分類（連已軟刪除的一併清走）；引用它的票據改為未分類，
            //    列表顯示「—」、匯出歸到「未分類地盤」，之後重新保存時必須選一個真實地盤
            migrationBuilder.Sql("""
                UPDATE accounting_receipts
                SET "SiteCategoryDictId" = NULL
                WHERE "SiteCategoryDictId" IN (
                    SELECT "Id" FROM sys_dict
                    WHERE "GroupName" = 'AccountingSiteCategory' AND "Label" = '123');

                DELETE FROM sys_dict
                WHERE "GroupName" = 'AccountingSiteCategory' AND "Label" = '123';
                """);

            // 2. 再把舊地盤搬成地盤分類字典項（必須在刪表之前做）；識別碼和排序接在現有項之後，避免撞號
            migrationBuilder.Sql("""
                INSERT INTO sys_dict ("Id", "GroupName", "Label", "Value", "Sort", "Enabled", "Remark",
                                      "CreatedAt", "UpdatedAt", "IsDeleted", "DeletedAt")
                SELECT site."Id", 'AccountingSiteCategory', site."Name",
                       ((SELECT coalesce(max(d."Value"::int), 0) FROM sys_dict d
                         WHERE d."GroupName" = 'AccountingSiteCategory' AND d."Value" ~ '^[0-9]+$')
                        + row_number() OVER (ORDER BY site."CreatedAt", site."Id"))::text,
                       ((SELECT coalesce(max(d."Sort"), 0) FROM sys_dict d
                         WHERE d."GroupName" = 'AccountingSiteCategory')
                        + row_number() OVER (ORDER BY site."CreatedAt", site."Id"))::int,
                       site."IsActive" AND NOT site."IsDeleted",
                       NULLIF(btrim(coalesce(site."Address", '')), ''),
                       site."CreatedAt", site."UpdatedAt", site."IsDeleted", site."DeletedAt"
                FROM attendance_sites site;
                """);

            // 3. 專案規定資料庫不建外鍵；舊表上的 FK 可能已被 DropAllForeignKeys 清掉，用 IF EXISTS 保險
            migrationBuilder.Sql("""
                ALTER TABLE attendance_records DROP CONSTRAINT IF EXISTS "FK_attendance_records_attendance_sites_SiteId";
                """);

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "attendance_records",
                newName: "SiteDictId");

            migrationBuilder.RenameIndex(
                name: "IX_attendance_records_SiteId",
                table: "attendance_records",
                newName: "IX_attendance_records_SiteDictId");

            // 4. 資料搬完才刪舊表
            migrationBuilder.DropTable(
                name: "attendance_sites");

            // 5. EF 會為 SiteDictId 產生到 sys_dict 的外鍵，這裡直接清掉（同 AddAttendancePosition 的做法）
            migrationBuilder.Sql("""
                ALTER TABLE attendance_records DROP CONSTRAINT IF EXISTS "FK_attendance_records_sys_dict_SiteDictId";
                """);
        }

        /// <inheritdoc />
        /// <remarks>「123」的清空不可逆，Down 只把表和欄位還原，不重建那些測試分類。</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SiteDictId",
                table: "attendance_records",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_attendance_records_SiteDictId",
                table: "attendance_records",
                newName: "IX_attendance_records_SiteId");

            migrationBuilder.CreateTable(
                name: "attendance_sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_sites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sites_Name",
                table: "attendance_sites",
                column: "Name");

            // 只把打卡明細實際用到的地盤還原成獨立表；字典項保留，因為同一批現在也給會計票據用
            migrationBuilder.Sql("""
                INSERT INTO attendance_sites ("Id", "Name", "Address", "IsActive",
                                              "CreatedAt", "UpdatedAt", "IsDeleted", "DeletedAt")
                SELECT dict."Id", dict."Label", dict."Remark", dict."Enabled",
                       dict."CreatedAt", dict."UpdatedAt", dict."IsDeleted", dict."DeletedAt"
                FROM sys_dict dict
                WHERE dict."GroupName" = 'AccountingSiteCategory'
                  AND EXISTS (SELECT 1 FROM attendance_records record WHERE record."SiteId" = dict."Id")
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }
    }
}
