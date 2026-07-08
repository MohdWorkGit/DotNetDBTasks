using System;
using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Moves a scheduled task's recurrence from single columns on ScheduledTasks into a
    /// ScheduledTaskTriggers child table so one task can fire on several rules at once
    /// (e.g. daily at 03:00 + monthly on day 14 + daily at 14:00). Each existing task's
    /// schedule is copied into one trigger row, then the old columns are dropped.
    /// NextRunAt stays on the task and now holds the earliest occurrence across triggers.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260710000000_AddScheduledTaskTriggers")]
    public partial class AddScheduledTaskTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTaskTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ScheduledTaskId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Frequency = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    TimeOfDay = table.Column<string>(type: "NVARCHAR2(5)", maxLength: 5, nullable: true),
                    DayOfWeek = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    DayOfMonth = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskTriggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTaskTriggers_ScheduledTasks_ScheduledTaskId",
                        column: x => x.ScheduledTaskId,
                        principalTable: "ScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskTriggers_ScheduledTaskId",
                table: "ScheduledTaskTriggers",
                column: "ScheduledTaskId");

            // Every existing task keeps its schedule as a single trigger row.
            migrationBuilder.Sql("""
                INSERT INTO "ScheduledTaskTriggers"
                    ("Id", "ScheduledTaskId", "Frequency", "IntervalMinutes", "TimeOfDay", "DayOfWeek", "DayOfMonth", "SortOrder", "CreatedAt")
                SELECT SYS_GUID(), "Id", "Frequency", "IntervalMinutes", "TimeOfDay", "DayOfWeek", "DayOfMonth", 0, SYSTIMESTAMP
                FROM "ScheduledTasks"
                """);

            migrationBuilder.DropColumn(name: "Frequency", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "IntervalMinutes", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "TimeOfDay", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "DayOfWeek", table: "ScheduledTasks");
            migrationBuilder.DropColumn(name: "DayOfMonth", table: "ScheduledTasks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Frequency",
                table: "ScheduledTasks",
                type: "NUMBER(10)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "IntervalMinutes",
                table: "ScheduledTasks",
                type: "NUMBER(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeOfDay",
                table: "ScheduledTasks",
                type: "NVARCHAR2(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                table: "ScheduledTasks",
                type: "NUMBER(10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayOfMonth",
                table: "ScheduledTasks",
                type: "NUMBER(10)",
                nullable: true);

            // Best effort: restore each task's first trigger (by SortOrder) as its schedule.
            migrationBuilder.Sql("""
                UPDATE "ScheduledTasks" t SET
                    "Frequency" = (SELECT MIN("Frequency") KEEP (DENSE_RANK FIRST ORDER BY "SortOrder", "Id")
                                   FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id"),
                    "IntervalMinutes" = (SELECT MIN("IntervalMinutes") KEEP (DENSE_RANK FIRST ORDER BY "SortOrder", "Id")
                                         FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id"),
                    "TimeOfDay" = (SELECT MIN("TimeOfDay") KEEP (DENSE_RANK FIRST ORDER BY "SortOrder", "Id")
                                   FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id"),
                    "DayOfWeek" = (SELECT MIN("DayOfWeek") KEEP (DENSE_RANK FIRST ORDER BY "SortOrder", "Id")
                                   FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id"),
                    "DayOfMonth" = (SELECT MIN("DayOfMonth") KEEP (DENSE_RANK FIRST ORDER BY "SortOrder", "Id")
                                    FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id")
                WHERE EXISTS (SELECT 1 FROM "ScheduledTaskTriggers" tr WHERE tr."ScheduledTaskId" = t."Id")
                """);

            migrationBuilder.DropTable(name: "ScheduledTaskTriggers");
        }
    }
}
