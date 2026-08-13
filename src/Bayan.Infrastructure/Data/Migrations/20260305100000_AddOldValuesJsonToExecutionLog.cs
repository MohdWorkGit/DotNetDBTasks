using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOldValuesJsonToExecutionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable CLOB column to store pre-update column values for UPDATE queries.
            migrationBuilder.AddColumn<string>(
                name: "OldValuesJson",
                table: "QueryExecutionLogs",
                type: "CLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OldValuesJson",
                table: "QueryExecutionLogs");
        }
    }
}
