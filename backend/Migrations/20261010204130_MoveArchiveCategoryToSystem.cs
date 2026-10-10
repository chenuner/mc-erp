using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class MoveArchiveCategoryToSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 表重命名：storage_categories → system_archive_categories
            migrationBuilder.RenameTable(
                name: "storage_categories",
                newName: "system_archive_categories");

            // 主鍵重命名
            migrationBuilder.Sql(
                "ALTER TABLE system_archive_categories RENAME CONSTRAINT \"PK_storage_categories\" TO \"PK_system_archive_categories\";");

            // 索引重命名
            migrationBuilder.RenameIndex(
                name: "IX_storage_categories_SiteName",
                table: "system_archive_categories",
                newName: "IX_system_archive_categories_SiteName");

            // 新增 EventDate 索引
            migrationBuilder.CreateIndex(
                name: "IX_system_archive_categories_EventDate",
                table: "system_archive_categories",
                column: "EventDate");

            // 外鍵重命名（storage_files → system_archive_categories）
            migrationBuilder.Sql(
                "ALTER TABLE storage_files RENAME CONSTRAINT \"FK_storage_files_storage_categories_CategoryId\" TO \"FK_storage_files_system_archive_categories_CategoryId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE storage_files RENAME CONSTRAINT \"FK_storage_files_system_archive_categories_CategoryId\" TO \"FK_storage_files_storage_categories_CategoryId\";");

            migrationBuilder.DropIndex(
                name: "IX_system_archive_categories_EventDate",
                table: "system_archive_categories");

            migrationBuilder.RenameIndex(
                name: "IX_system_archive_categories_SiteName",
                table: "system_archive_categories",
                newName: "IX_storage_categories_SiteName");

            migrationBuilder.Sql(
                "ALTER TABLE system_archive_categories RENAME CONSTRAINT \"PK_system_archive_categories\" TO \"PK_storage_categories\";");

            migrationBuilder.RenameTable(
                name: "system_archive_categories",
                newName: "storage_categories");
        }
    }
}
