using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MC.Erp.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeDictId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteCategoryDictId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MerchantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remark = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OcrStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OcrError = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OcrResultJson = table.Column<string>(type: "text", nullable: true),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    OcrStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManuallyEdited = table.Column<bool>(type: "boolean", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_receipts_attendance_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "attendance_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_receipts_storage_files_FileItemId",
                        column: x => x.FileItemId,
                        principalTable: "storage_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_receipts_sys_dict_DocumentTypeDictId",
                        column: x => x.DocumentTypeDictId,
                        principalTable: "sys_dict",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_receipts_sys_dict_SiteCategoryDictId",
                        column: x => x.SiteCategoryDictId,
                        principalTable: "sys_dict",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_DocumentTypeDictId",
                table: "accounting_receipts",
                column: "DocumentTypeDictId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_FileItemId",
                table: "accounting_receipts",
                column: "FileItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_ReceiptDate",
                table: "accounting_receipts",
                column: "ReceiptDate");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_SiteCategoryDictId_ReceiptDate",
                table: "accounting_receipts",
                columns: new[] { "SiteCategoryDictId", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_receipts_SiteId_ReceiptDate",
                table: "accounting_receipts",
                columns: new[] { "SiteId", "ReceiptDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_receipts");
        }
    }
}
