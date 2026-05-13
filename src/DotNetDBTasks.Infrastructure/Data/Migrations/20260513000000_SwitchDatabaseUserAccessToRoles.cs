using System;
using DotNetDBTasks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Replaces UserDatabaseUserAccess (per-user grants) with DatabaseUserRoleAccess (per-role grants).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260513000000_SwitchDatabaseUserAccessToRoles")]
    public partial class SwitchDatabaseUserAccessToRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserDatabaseUserAccess");

            migrationBuilder.CreateTable(
                name: "DatabaseUserRoleAccess",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DatabaseUserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseUserRoleAccess", x => new { x.RoleId, x.DatabaseUserId });
                    table.ForeignKey(
                        name: "FK_DatabaseUserRoleAccess_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatabaseUserRoleAccess_DatabaseUsers_DatabaseUserId",
                        column: x => x.DatabaseUserId,
                        principalTable: "DatabaseUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseUserRoleAccess_DatabaseUserId",
                table: "DatabaseUserRoleAccess",
                column: "DatabaseUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DatabaseUserRoleAccess");

            migrationBuilder.CreateTable(
                name: "UserDatabaseUserAccess",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DatabaseUserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDatabaseUserAccess", x => new { x.UserId, x.DatabaseUserId });
                    table.ForeignKey(
                        name: "FK_UserDatabaseUserAccess_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDatabaseUserAccess_DatabaseUsers_DatabaseUserId",
                        column: x => x.DatabaseUserId,
                        principalTable: "DatabaseUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseUserAccess_DatabaseUserId",
                table: "UserDatabaseUserAccess",
                column: "DatabaseUserId");
        }
    }
}
