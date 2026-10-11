using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTypeSysDictRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Type",
                table: "storage_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_files_Type",
                table: "storage_files",
                column: "Type");

            migrationBuilder.AddForeignKey(
                name: "FK_storage_files_sys_dict_Type",
                table: "storage_files",
                column: "Type",
                principalTable: "sys_dict",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_storage_files_sys_dict_Type",
                table: "storage_files");

            migrationBuilder.DropIndex(
                name: "IX_storage_files_Type",
                table: "storage_files");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "storage_files");
        }
    }
}
