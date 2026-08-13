using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds combined-output columns to ScheduledTasks: CombineOutput switches the task
    /// to writing all read-query results into ONE file in item order (header from the
    /// first query), configured by CombinedFileName/CombinedFormat/CombinedCsvSeparator/
    /// CombinedAppendTimestamp. IncludeHeaders (default on) lets any task's CSV/Excel
    /// exports omit the header row. Defaults keep every existing task behaving as before.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260709000000_AddScheduledTaskCombinedOutput")]
    public partial class AddScheduledTaskCombinedOutput : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "CombineOutput",
                table: "ScheduledTasks",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "IncludeHeaders",
                table: "ScheduledTasks",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "CombinedFileName",
                table: "ScheduledTasks",
                type: "NVARCHAR2(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CombinedFormat",
                table: "ScheduledTasks",
                type: "NUMBER(10)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CombinedCsvSeparator",
                table: "ScheduledTasks",
                type: "NVARCHAR2(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "CombinedAppendTimestamp",
                table: "ScheduledTasks",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CombineOutput", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "IncludeHeaders", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "CombinedFileName", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "CombinedFormat", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "CombinedCsvSeparator", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "CombinedAppendTimestamp", table: "ScheduledTasks");
        }
    }
}
