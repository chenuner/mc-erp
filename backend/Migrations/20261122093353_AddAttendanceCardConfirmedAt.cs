using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCardConfirmedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "attendance_card_scans",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "attendance_card_scans");
        }
    }
}
