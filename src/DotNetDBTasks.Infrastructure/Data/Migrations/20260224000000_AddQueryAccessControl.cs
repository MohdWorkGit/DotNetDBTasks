using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DynamicQueryDepartments",
                columns: table => new
                {
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Department = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicQueryDepartments", x => new { x.DynamicQueryId, x.Department });
                    table.ForeignKey(
                        name: "FK_DynamicQueryDepartments_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicQueryUsers",
                columns: table => new
                {
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicQueryUsers", x => new { x.DynamicQueryId, x.UserId });
                    table.ForeignKey(
                        name: "FK_DynamicQueryUsers_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DynamicQueryUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueryDepartments_Department",
                table: "DynamicQueryDepartments",
                column: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueryUsers_UserId",
                table: "DynamicQueryUsers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DynamicQueryDepartments");
            migrationBuilder.DropTable(name: "DynamicQueryUsers");
        }
    }
}
