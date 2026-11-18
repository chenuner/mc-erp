using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddSysDictGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_dict_group",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Remark = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sys_dict_group", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sys_dict_group_Code",
                table: "sys_dict_group",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_dict_group");
        }
    }
}
