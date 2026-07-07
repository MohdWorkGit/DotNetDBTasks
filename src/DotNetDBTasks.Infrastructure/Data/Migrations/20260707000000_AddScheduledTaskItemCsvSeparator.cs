using System;
using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ScheduledTaskItems.CsvSeparator: the field separator used when the item
    /// exports CSV (e.g. ";", "|", tab). NULL means the default comma, so existing
    /// items keep producing identical files.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260707000000_AddScheduledTaskItemCsvSeparator")]
    public partial class AddScheduledTaskItemCsvSeparator : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CsvSeparator",
                table: "ScheduledTaskItems",
                type: "NVARCHAR2(8)",
                maxLength: 8,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CsvSeparator", table: "ScheduledTaskItems");
        }
    }
}
