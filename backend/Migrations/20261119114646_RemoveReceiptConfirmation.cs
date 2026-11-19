using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReceiptConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "accounting_receipts");

            migrationBuilder.DropColumn(
                name: "ConfirmedById",
                table: "accounting_receipts");

            migrationBuilder.DropColumn(
                name: "IsConfirmed",
                table: "accounting_receipts");

            migrationBuilder.DropColumn(
                name: "ManuallyEdited",
                table: "accounting_receipts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "accounting_receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedById",
                table: "accounting_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConfirmed",
                table: "accounting_receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyEdited",
                table: "accounting_receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
