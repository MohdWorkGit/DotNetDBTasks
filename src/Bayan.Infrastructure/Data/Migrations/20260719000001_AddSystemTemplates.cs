using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds the SystemTemplates table holding system-wide document templates keyed by
    /// purpose. The "word-default" row is the admin-editable default Word export template
    /// used when a query has no template of its own; without the row, exports fall back
    /// to the built-in starter layout.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260719000001_AddSystemTemplates")]
    public partial class AddSystemTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Key = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "NVARCHAR2(255)", maxLength: 255, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemTemplates_Key",
                table: "SystemTemplates",
                column: "Key",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SystemTemplates");
        }
    }
}
