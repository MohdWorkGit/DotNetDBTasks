using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ServerType and DatabaseName columns to DatabaseUsers table
    /// to support SQL Server, PostgreSQL, and MySQL connections.
    /// </summary>
    public partial class AddMultiDatabaseSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ServiceName is no longer required (only used for Oracle)
            migrationBuilder.AlterColumn<string>(
                name: "ServiceName",
                table: "DatabaseUsers",
                type: "NVARCHAR2(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(200)",
                oldMaxLength: 200);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
