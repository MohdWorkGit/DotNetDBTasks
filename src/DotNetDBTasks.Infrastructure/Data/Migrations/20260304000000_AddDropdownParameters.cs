using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDropdownParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add dropdown-specific columns to QueryParameters
            migrationBuilder.AddColumn<int>(
                name: "DropdownSourceType",
                table: "QueryParameters",
                type: "NUMBER(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropdownStaticValues",
                table: "QueryParameters",
                type: "CLOB",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropdownQueryId",
                table: "QueryParameters",
                type: "RAW(16)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropdownQueryValueColumn",
                table: "QueryParameters",
                type: "NVARCHAR2(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropdownQueryLabelColumn",
                table: "QueryParameters",
                type: "NVARCHAR2(100)",
                maxLength: 100,
                nullable: true);

            // FK from QueryParameters.DropdownQueryId -> DynamicQueries.Id (SET NULL on delete)
            migrationBuilder.AddForeignKey(
                name: "FK_QueryParameters_DynamicQueries_DropdownQueryId",
                table: "QueryParameters",
                column: "DropdownQueryId",
                principalTable: "DynamicQueries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_QueryParameters_DropdownQueryId",
                table: "QueryParameters",
                column: "DropdownQueryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QueryParameters_DynamicQueries_DropdownQueryId",
                table: "QueryParameters");

            migrationBuilder.DropIndex(
                name: "IX_QueryParameters_DropdownQueryId",
                table: "QueryParameters");

            migrationBuilder.DropColumn(name: "DropdownSourceType", table: "QueryParameters");
            migrationBuilder.DropColumn(name: "DropdownStaticValues", table: "QueryParameters");
            migrationBuilder.DropColumn(name: "DropdownQueryId", table: "QueryParameters");
            migrationBuilder.DropColumn(name: "DropdownQueryValueColumn", table: "QueryParameters");
            migrationBuilder.DropColumn(name: "DropdownQueryLabelColumn", table: "QueryParameters");
        }
    }
}
