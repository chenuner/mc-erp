using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptOptionalImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "FileItemId",
                table: "accounting_receipts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "Type",
                table: "accounting_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // 舊資料的支出分類原存在 storage_files.Type，回填到票據表；沒有原圖分類的用 pending 兜底
            migrationBuilder.Sql("""
                UPDATE accounting_receipts AS r
                SET "Type" = f."Type"
                FROM storage_files AS f
                WHERE f."Id" = r."FileItemId" AND f."Type" IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE accounting_receipts AS r
                SET "Type" = p."Id"
                FROM (SELECT "Id" FROM sys_dict WHERE "GroupName" = 'Accounting'
                      ORDER BY ("Value" = 'pending') DESC, "Sort" LIMIT 1) AS p
                WHERE r."Type" = '00000000-0000-0000-0000-000000000000'::uuid;
                """);
            migrationBuilder.Sql("""ALTER TABLE accounting_receipts ALTER COLUMN "Type" DROP DEFAULT;""");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_Type",
                table: "accounting_receipts",
                column: "Type");

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_receipts_sys_dict_Type",
                table: "accounting_receipts",
                column: "Type",
                principalTable: "sys_dict",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_receipts_sys_dict_Type",
                table: "accounting_receipts");

            migrationBuilder.DropIndex(
                name: "IX_accounting_receipts_Type",
                table: "accounting_receipts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "accounting_receipts");

            migrationBuilder.AlterColumn<Guid>(
                name: "FileItemId",
                table: "accounting_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
