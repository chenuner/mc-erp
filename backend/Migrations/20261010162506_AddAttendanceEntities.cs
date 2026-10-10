using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_workers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    EmployeeId = table.Column<string>(type: "text", nullable: true),
                    DailyWage = table.Column<decimal>(type: "numeric", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_workers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_monthly_summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    WorkDays = table.Column<decimal>(type: "numeric", nullable: false),
                    LaborLeaveDays = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalWorkDays = table.Column<decimal>(type: "numeric", nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyWage = table.Column<decimal>(type: "numeric", nullable: false),
                    OvertimeAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    MpfEmployer = table.Column<decimal>(type: "numeric", nullable: false),
                    MpfEmployee = table.Column<decimal>(type: "numeric", nullable: false),
                    ActualPay = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Remark = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_monthly_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_monthly_summaries_attendance_workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "attendance_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockInTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOutTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OvertimeHours = table.Column<decimal>(type: "numeric", nullable: false),
                    IsLaborLeave = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_records_attendance_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "attendance_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attendance_records_attendance_workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "attendance_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_monthly_summaries_WorkerId_Year_Month",
                table: "attendance_monthly_summaries",
                columns: new[] { "WorkerId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_SiteId",
                table: "attendance_records",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_WorkerId_ClockInTime",
                table: "attendance_records",
                columns: new[] { "WorkerId", "ClockInTime" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sites_Name",
                table: "attendance_sites",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_workers_EmployeeId",
                table: "attendance_workers",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_monthly_summaries");

            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "attendance_sites");

            migrationBuilder.DropTable(
                name: "attendance_workers");
        }
    }
}
