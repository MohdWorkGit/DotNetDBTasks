using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ScheduledTasks.ArchiveFolder: an optional second absolute folder that
    /// receives a copy of every export file the task produces. NULL means no copy,
    /// so existing tasks are unaffected.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260708000000_AddScheduledTaskArchiveFolder")]
    public partial class AddScheduledTaskArchiveFolder : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveFolder",
                table: "ScheduledTasks",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ArchiveFolder", table: "ScheduledTasks");
        }
    }
}
