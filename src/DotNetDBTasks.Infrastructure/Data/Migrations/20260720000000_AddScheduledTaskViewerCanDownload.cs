using System;
using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ScheduledTaskViewers.CanDownloadFiles: an extra permission on top of the
    /// status-visibility grant that lets the viewer download the run's export files.
    /// Defaults to 0 so existing viewers keep status-only access.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260720000000_AddScheduledTaskViewerCanDownload")]
    public partial class AddScheduledTaskViewerCanDownload : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "CanDownloadFiles",
                table: "ScheduledTaskViewers",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CanDownloadFiles", table: "ScheduledTaskViewers");
        }
    }
}
