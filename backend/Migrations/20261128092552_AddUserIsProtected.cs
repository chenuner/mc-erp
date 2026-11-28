using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsProtected : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProtected",
                table: "identity_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 既有資料庫不會再跑 SeedData，所以在這裡把種子主管理員標成受保護
            migrationBuilder.Sql(
                "UPDATE identity_users SET \"IsProtected\" = TRUE WHERE \"Username\" = 'admin' AND \"IsDeleted\" = FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProtected",
                table: "identity_users");
        }
    }
}
