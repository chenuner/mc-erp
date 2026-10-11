using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_storage_files_system_archive_categories_CategoryId",
                table: "storage_files");

            migrationBuilder.DropTable(
                name: "system_archive_categories");

            migrationBuilder.DropIndex(
                name: "IX_storage_files_CategoryId",
                table: "storage_files");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "storage_files");

            migrationBuilder.CreateTable(
                name: "sys_dict",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Remark = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sys_dict", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_sys_dict_enabled",
                table: "sys_dict",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "idx_sys_dict_group_name",
                table: "sys_dict",
                column: "GroupName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_dict");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "storage_files",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "system_archive_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EventReason = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "text", nullable: true),
                    SiteName = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_archive_categories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_storage_files_CategoryId",
                table: "storage_files",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_system_archive_categories_EventDate",
                table: "system_archive_categories",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_system_archive_categories_SiteName",
                table: "system_archive_categories",
                column: "SiteName");

            migrationBuilder.AddForeignKey(
                name: "FK_storage_files_system_archive_categories_CategoryId",
                table: "storage_files",
                column: "CategoryId",
                principalTable: "system_archive_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
