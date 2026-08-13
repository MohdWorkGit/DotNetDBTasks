using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds incremental-checkpoint columns to ScheduledTaskItems: KeyColumn/KeyParameter/
    /// InitialKey configure the checkpoint, LastKeyValue records where the last successful
    /// run stopped so the next run continues from there. Also from this release scheduled
    /// tasks may contain write queries (no schema change needed for that — they simply
    /// record affected rows instead of exporting a file).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260706000000_AddScheduledTaskItemCheckpoint")]
    public partial class AddScheduledTaskItemCheckpoint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyColumn",
                table: "ScheduledTaskItems",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyParameter",
                table: "ScheduledTaskItems",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InitialKey",
                table: "ScheduledTaskItems",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastKeyValue",
                table: "ScheduledTaskItems",
                type: "NVARCHAR2(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "KeyColumn", table: "ScheduledTaskItems");
            migrationBuilder.DropColumn(name: "KeyParameter", table: "ScheduledTaskItems");
            migrationBuilder.DropColumn(name: "InitialKey", table: "ScheduledTaskItems");
            migrationBuilder.DropColumn(name: "LastKeyValue", table: "ScheduledTaskItems");
        }
    }
}
