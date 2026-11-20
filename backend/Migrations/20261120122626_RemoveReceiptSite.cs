using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReceiptSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_receipts_attendance_sites_SiteId",
                table: "accounting_receipts");

            migrationBuilder.DropIndex(
                name: "IX_accounting_receipts_SiteId_ReceiptDate",
                table: "accounting_receipts");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "accounting_receipts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "accounting_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_SiteId_ReceiptDate",
                table: "accounting_receipts",
                columns: new[] { "SiteId", "ReceiptDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_receipts_attendance_sites_SiteId",
                table: "accounting_receipts",
                column: "SiteId",
                principalTable: "attendance_sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
