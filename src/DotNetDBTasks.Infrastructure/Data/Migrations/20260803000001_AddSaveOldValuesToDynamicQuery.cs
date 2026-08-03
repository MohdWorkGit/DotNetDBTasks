using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds DynamicQueries.SaveOldValues: whether an UPDATE/DELETE run snapshots the pre-change
    /// rows into the execution log. Defaults to 1 so existing queries keep the audit trail they
    /// have today; admins can switch it off for statements that affect large row counts.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260803000001_AddSaveOldValuesToDynamicQuery")]
    public partial class AddSaveOldValuesToDynamicQuery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "SaveOldValues",
                table: "DynamicQueries",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SaveOldValues", table: "DynamicQueries");
        }
    }
}
