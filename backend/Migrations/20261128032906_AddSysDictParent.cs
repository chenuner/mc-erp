using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <summary>
    /// 系統字典支援多層結構（例如地盤「牛房 → 一樓、二樓」）：sys_dict 加 ParentId。
    /// 專案規定資料庫不建外鍵，所以這裡只建欄位和索引，關聯只靠 EF 導覽和程式驗證。
    /// </summary>
    public partial class AddSysDictParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "sys_dict",
                type: "uuid",
                nullable: true);

            // 專案規定資料庫不建外鍵（AGENTS.md 金科玉律 8）：自引用導覽只靠 EF 和程式驗證，
            // 這裡直接建欄位，不走 AddForeignKey，免得真在庫裡留下約束
            migrationBuilder.CreateIndex(
                name: "idx_sys_dict_parent",
                table: "sys_dict",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_sys_dict_parent",
                table: "sys_dict");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "sys_dict");
        }
    }
}
