using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCardScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_card_scans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    YearConfidence = table.Column<float>(type: "real", nullable: true),
                    Month = table.Column<int>(type: "integer", nullable: true),
                    MonthConfidence = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NameConfidence = table.Column<float>(type: "real", nullable: true),
                    ReviewFields = table.Column<List<string>>(type: "text[]", nullable: false),
                    FileItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_card_scans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_card_scans_storage_files_FileItemId",
                        column: x => x.FileItemId,
                        principalTable: "storage_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_card_punches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    AmIn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    AmInConfidence = table.Column<float>(type: "real", nullable: true),
                    AmOut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    AmOutConfidence = table.Column<float>(type: "real", nullable: true),
                    PmIn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    PmInConfidence = table.Column<float>(type: "real", nullable: true),
                    PmOut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    PmOutConfidence = table.Column<float>(type: "real", nullable: true),
                    OtIn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    OtInConfidence = table.Column<float>(type: "real", nullable: true),
                    OtOut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    OtOutConfidence = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_card_punches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_card_punches_attendance_card_scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "attendance_card_scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_card_punches_ScanId_Day",
                table: "attendance_card_punches",
                columns: new[] { "ScanId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_card_scans_FileItemId",
                table: "attendance_card_scans",
                column: "FileItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_card_scans_Year_Month",
                table: "attendance_card_scans",
                columns: new[] { "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_card_punches");

            migrationBuilder.DropTable(
                name: "attendance_card_scans");
        }
    }
}
