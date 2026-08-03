using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds DynamicQueries.AllowRunWithoutConfirmation: whether a write query may be run with the
    /// preview/confirm step skipped. Defaults to 1 so existing queries keep the behaviour they
    /// have today (the option was previously offered on every write query).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260803000000_AddAllowRunWithoutConfirmationToDynamicQuery")]
    public partial class AddAllowRunWithoutConfirmationToDynamicQuery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "AllowRunWithoutConfirmation",
                table: "DynamicQueries",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowRunWithoutConfirmation", table: "DynamicQueries");
        }
    }
}
