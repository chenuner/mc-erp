using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendancePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PositionDictId",
                table: "attendance_workers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Position",
                table: "attendance_monthly_summaries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PositionDictId",
                table: "attendance_monthly_summaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_workers_PositionDictId",
                table: "attendance_workers",
                column: "PositionDictId");

            // 專案規定資料庫不建外鍵；EF 原本會為 Worker.Position 產生這個 FK，這裡直接清掉
            migrationBuilder.Sql("""
                ALTER TABLE attendance_workers DROP CONSTRAINT IF EXISTS "FK_attendance_workers_sys_dict_PositionDictId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_attendance_workers_PositionDictId",
                table: "attendance_workers");

            migrationBuilder.DropColumn(
                name: "PositionDictId",
                table: "attendance_workers");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "attendance_monthly_summaries");

            migrationBuilder.DropColumn(
                name: "PositionDictId",
                table: "attendance_monthly_summaries");
        }
    }
}
