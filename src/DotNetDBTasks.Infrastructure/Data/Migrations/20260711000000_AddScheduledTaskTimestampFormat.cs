using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ScheduledTasks.TimestampFormat: an optional .NET date format for the
    /// timestamp suffix of export file names (per-query and combined). NULL keeps
    /// the previous behaviour ("_yyyyMMdd-HHmmss").
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260711000000_AddScheduledTaskTimestampFormat")]
    public partial class AddScheduledTaskTimestampFormat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimestampFormat",
                table: "ScheduledTasks",
                type: "NVARCHAR2(50)",
                maxLength: 50,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TimestampFormat", table: "ScheduledTasks");
        }
    }
}
