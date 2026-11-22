using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <summary>
    /// 打卡明細加上職位分類（<c>PositionDictId</c>），薪酬表加上地盤（<c>SiteDictId</c> + <c>Site</c> 快照）。
    /// 薪酬不再是一人一月一行，而是「員工 × 職位 × 地盤」各一行，所以把原本
    /// (WorkerId, Year, Month) 的唯一索引換成含職位、地盤的普通索引——職位可為空，
    /// PostgreSQL 的唯一索引對 NULL 各行視為不同，擋不住重複，唯一性改由程式保證。
    /// </summary>
    public partial class AddPunchPositionAndPayrollSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_attendance_monthly_summaries_WorkerId_Year_Month",
                table: "attendance_monthly_summaries");

            migrationBuilder.AddColumn<Guid>(
                name: "PositionDictId",
                table: "attendance_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "attendance_monthly_summaries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteDictId",
                table: "attendance_monthly_summaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_PositionDictId",
                table: "attendance_records",
                column: "PositionDictId");

            migrationBuilder.CreateIndex(
                name: "idx_attendance_summary_worker_month_group",
                table: "attendance_monthly_summaries",
                columns: new[] { "WorkerId", "Year", "Month", "PositionDictId", "SiteDictId" });

            // 既有打卡明細回填職位：用員工當下的職位是最合理的猜測；之後重新同步會以員工職位重寫
            migrationBuilder.Sql("""
                UPDATE attendance_records record
                SET "PositionDictId" = worker."PositionDictId"
                FROM attendance_workers worker
                WHERE worker."Id" = record."WorkerId"
                  AND record."PositionDictId" IS NULL
                  AND worker."PositionDictId" IS NOT NULL;
                """);

            // 既有薪酬表回填地盤：只回填「該月明細全部在同一個地盤」的情況，多個地盤的不猜，
            // 留空等下一次同步重算時按明細重新分組。
            // 注意兩點：
            //   1. PostgreSQL 沒有 min(uuid)，要先轉 text 再轉回來；
            //   2. 年月要用 `AT TIME ZONE` 換成香港時間再取，不能用「+ interval '8 hours'」：
            //      timestamptz + interval 之後 date_part 仍會按 session 時區換算，資料庫時區不是 UTC 時會多算一次。
            migrationBuilder.Sql("""
                UPDATE attendance_monthly_summaries summary
                SET "SiteDictId" = grouped."SiteDictId", "Site" = dict."Label"
                FROM (
                    SELECT record."WorkerId",
                           (EXTRACT(YEAR FROM (record."ClockInTime" AT TIME ZONE 'Asia/Hong_Kong')))::int AS "Year",
                           (EXTRACT(MONTH FROM (record."ClockInTime" AT TIME ZONE 'Asia/Hong_Kong')))::int AS "Month",
                           min(record."SiteDictId"::text)::uuid AS "SiteDictId"
                    FROM attendance_records record
                    GROUP BY 1, 2, 3
                    HAVING count(DISTINCT record."SiteDictId") = 1
                ) grouped
                JOIN sys_dict dict ON dict."Id" = grouped."SiteDictId"
                WHERE summary."WorkerId" = grouped."WorkerId"
                  AND summary."Year" = grouped."Year"
                  AND summary."Month" = grouped."Month"
                  AND summary."SiteDictId" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_attendance_records_sys_dict_PositionDictId",
                table: "attendance_records",
                column: "PositionDictId",
                principalTable: "sys_dict",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 專案規定資料庫不建外鍵（AGENTS.md 金科玉律 8）；EF 為 Position 導航產生的 FK 立刻清掉，
            // 做法同 AddAttendancePosition / MoveAttendanceSiteToSysDict
            migrationBuilder.Sql("""
                ALTER TABLE attendance_records DROP CONSTRAINT IF EXISTS "FK_attendance_records_sys_dict_PositionDictId";
                """);
        }

        /// <inheritdoc />
        /// <remarks>回填的職位和地盤不可逆；Down 只還原結構。</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendance_records_sys_dict_PositionDictId",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "IX_attendance_records_PositionDictId",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "idx_attendance_summary_worker_month_group",
                table: "attendance_monthly_summaries");

            migrationBuilder.DropColumn(
                name: "PositionDictId",
                table: "attendance_records");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "attendance_monthly_summaries");

            migrationBuilder.DropColumn(
                name: "SiteDictId",
                table: "attendance_monthly_summaries");

            // 還原成一人一月一行之前，同一員工同一月可能已經有多行；保留工數最多的一行，其餘刪掉
            migrationBuilder.Sql("""
                DELETE FROM attendance_monthly_summaries duplicate
                USING attendance_monthly_summaries keeper
                WHERE duplicate."WorkerId" = keeper."WorkerId"
                  AND duplicate."Year" = keeper."Year"
                  AND duplicate."Month" = keeper."Month"
                  AND (keeper."WorkDays" > duplicate."WorkDays"
                       OR (keeper."WorkDays" = duplicate."WorkDays" AND keeper."Id" < duplicate."Id"));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_monthly_summaries_WorkerId_Year_Month",
                table: "attendance_monthly_summaries",
                columns: new[] { "WorkerId", "Year", "Month" },
                unique: true);
        }
    }
}
