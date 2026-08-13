using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncEFCore10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DynamicQueryDepartments_Department",
                table: "DynamicQueryDepartments");

            migrationBuilder.AlterColumn<short>(
                name: "IsActive",
                table: "Users",
                type: "NUMBER(5)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "NUMBER(1)");

            migrationBuilder.AlterColumn<short>(
                name: "IsRequired",
                table: "QueryParameters",
                type: "NUMBER(5)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "NUMBER(1)");

            migrationBuilder.AlterColumn<short>(
                name: "IsSuccess",
                table: "QueryExecutionLogs",
                type: "NUMBER(5)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "NUMBER(1)");

            migrationBuilder.AlterColumn<short>(
                name: "IsEnabled",
                table: "DynamicQueries",
                type: "NUMBER(5)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "NUMBER(1)");

            migrationBuilder.AlterColumn<short>(
                name: "IsActive",
                table: "DatabaseUsers",
                type: "NUMBER(5)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "NUMBER(1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "NUMBER(1)",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "NUMBER(5)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRequired",
                table: "QueryParameters",
                type: "NUMBER(1)",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "NUMBER(5)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSuccess",
                table: "QueryExecutionLogs",
                type: "NUMBER(1)",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "NUMBER(5)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "DynamicQueries",
                type: "NUMBER(1)",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "NUMBER(5)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "DatabaseUsers",
                type: "NUMBER(1)",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "NUMBER(5)");

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueryDepartments_Department",
                table: "DynamicQueryDepartments",
                column: "Department");
        }
    }
}
