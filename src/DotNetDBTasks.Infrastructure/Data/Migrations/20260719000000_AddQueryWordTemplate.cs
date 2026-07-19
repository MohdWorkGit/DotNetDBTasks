using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds DynamicQueries.WordTemplate (BLOB) and WordTemplateFileName: an optional
    /// per-query Word (.docx) template used by the Word export of query results.
    /// NULL means the built-in default document layout is used.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260719000000_AddQueryWordTemplate")]
    public partial class AddQueryWordTemplate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "WordTemplate",
                table: "DynamicQueries",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WordTemplateFileName",
                table: "DynamicQueries",
                type: "NVARCHAR2(255)",
                maxLength: 255,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WordTemplate", table: "DynamicQueries");
            migrationBuilder.DropColumn(name: "WordTemplateFileName", table: "DynamicQueries");
        }
    }
}
