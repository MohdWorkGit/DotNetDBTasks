using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    Username = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    EntityName = table.Column<string>(type: "NVARCHAR2(400)", maxLength: 400, nullable: true),
                    DetailsJson = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    IsSuccess = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Category",
                table: "SystemAuditLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_OccurredAt",
                table: "SystemAuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_UserId",
                table: "SystemAuditLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemAuditLogs");
        }
    }
}
