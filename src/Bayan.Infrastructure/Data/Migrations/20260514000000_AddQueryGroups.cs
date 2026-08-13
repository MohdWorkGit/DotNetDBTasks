using System;
using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds QueryGroups (folder-like containers for DynamicQueries) along with their own
    /// role/department/user access assignments. Each DynamicQuery gets an optional
    /// QueryGroupId. Group-level access is additive — a user with access to a group sees
    /// every query inside it on the My Queries page.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260514000000_AddQueryGroups")]
    public partial class AddQueryGroups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueryGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueryGroups_Name",
                table: "QueryGroups",
                column: "Name",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QueryGroupId",
                table: "DynamicQueries",
                type: "RAW(16)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueries_QueryGroupId",
                table: "DynamicQueries",
                column: "QueryGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_DynamicQueries_QueryGroups_QueryGroupId",
                table: "DynamicQueries",
                column: "QueryGroupId",
                principalTable: "QueryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateTable(
                name: "QueryGroupRoles",
                columns: table => new
                {
                    QueryGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryGroupRoles", x => new { x.QueryGroupId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_QueryGroupRoles_QueryGroups_QueryGroupId",
                        column: x => x.QueryGroupId,
                        principalTable: "QueryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueryGroupRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueryGroupRoles_RoleId",
                table: "QueryGroupRoles",
                column: "RoleId");

            migrationBuilder.CreateTable(
                name: "QueryGroupDepartments",
                columns: table => new
                {
                    QueryGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Department = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryGroupDepartments", x => new { x.QueryGroupId, x.Department });
                    table.ForeignKey(
                        name: "FK_QueryGroupDepartments_QueryGroups_QueryGroupId",
                        column: x => x.QueryGroupId,
                        principalTable: "QueryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryGroupUsers",
                columns: table => new
                {
                    QueryGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryGroupUsers", x => new { x.QueryGroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_QueryGroupUsers_QueryGroups_QueryGroupId",
                        column: x => x.QueryGroupId,
                        principalTable: "QueryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueryGroupUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueryGroupUsers_UserId",
                table: "QueryGroupUsers",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DynamicQueries_QueryGroups_QueryGroupId",
                table: "DynamicQueries");

            migrationBuilder.DropIndex(
                name: "IX_DynamicQueries_QueryGroupId",
                table: "DynamicQueries");

            migrationBuilder.DropColumn(
                name: "QueryGroupId",
                table: "DynamicQueries");

            migrationBuilder.DropTable(name: "QueryGroupRoles");
            migrationBuilder.DropTable(name: "QueryGroupDepartments");
            migrationBuilder.DropTable(name: "QueryGroupUsers");
            migrationBuilder.DropTable(name: "QueryGroups");
        }
    }
}
