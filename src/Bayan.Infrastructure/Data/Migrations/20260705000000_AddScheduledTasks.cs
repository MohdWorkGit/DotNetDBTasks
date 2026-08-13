using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds scheduled export tasks: a ScheduledTask defines a recurrence (interval/daily/
    /// weekly/monthly) and an output folder; its ScheduledTaskItems each run one read query
    /// with fixed parameters and export the result to a file (Excel/CSV/JSON). Runs are
    /// recorded in ScheduledTaskRuns; ScheduledTaskViewers grants non-admin users read-only
    /// visibility of a task's status.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705000000_AddScheduledTasks")]
    public partial class AddScheduledTasks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    OutputFolder = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: false),
                    Frequency = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    TimeOfDay = table.Column<string>(type: "NVARCHAR2(5)", maxLength: 5, nullable: true),
                    DayOfWeek = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    DayOfMonth = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_Name",
                table: "ScheduledTasks",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_NextRunAt",
                table: "ScheduledTasks",
                column: "NextRunAt");

            migrationBuilder.CreateTable(
                name: "ScheduledTaskItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ScheduledTaskId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ParametersJson = table.Column<string>(type: "CLOB", nullable: true),
                    ExportFormat = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    FileNamePrefix = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    AppendTimestamp = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTaskItems_ScheduledTasks_ScheduledTaskId",
                        column: x => x.ScheduledTaskId,
                        principalTable: "ScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledTaskItems_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskItems_ScheduledTaskId",
                table: "ScheduledTaskItems",
                column: "ScheduledTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskItems_DynamicQueryId",
                table: "ScheduledTaskItems",
                column: "DynamicQueryId");

            migrationBuilder.CreateTable(
                name: "ScheduledTaskRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ScheduledTaskId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    Status = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    TriggeredByUsername = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    ItemResultsJson = table.Column<string>(type: "CLOB", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTaskRuns_ScheduledTasks_ScheduledTaskId",
                        column: x => x.ScheduledTaskId,
                        principalTable: "ScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskRuns_ScheduledTaskId",
                table: "ScheduledTaskRuns",
                column: "ScheduledTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskRuns_StartedAt",
                table: "ScheduledTaskRuns",
                column: "StartedAt");

            migrationBuilder.CreateTable(
                name: "ScheduledTaskViewers",
                columns: table => new
                {
                    ScheduledTaskId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskViewers", x => new { x.ScheduledTaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ScheduledTaskViewers_ScheduledTasks_ScheduledTaskId",
                        column: x => x.ScheduledTaskId,
                        principalTable: "ScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledTaskViewers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskViewers_UserId",
                table: "ScheduledTaskViewers",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduledTaskViewers");
            migrationBuilder.DropTable(name: "ScheduledTaskRuns");
            migrationBuilder.DropTable(name: "ScheduledTaskItems");
            migrationBuilder.DropTable(name: "ScheduledTasks");
        }
    }
}
