using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds DatabaseUsers table, UserDatabaseUserAccess join table,
    /// and a nullable DatabaseUserId FK on DynamicQueries.
    /// </summary>
    public partial class AddDatabaseUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: false),
                    ServerType = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 0),
                    Host = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: false),
                    Port = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 1521),
                    // Nullable: Oracle treats empty strings as NULL; ServiceName is only used for Oracle connections.
                    ServiceName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    // Nullable: DatabaseName is only used for non-Oracle connections.
                    DatabaseName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    DbUsername = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<short>(type: "NUMBER(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseUsers_Name",
                table: "DatabaseUsers",
                column: "Name",
                unique: true);

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

            migrationBuilder.AddColumn<Guid>(
                name: "DatabaseUserId",
                table: "DynamicQueries",
                type: "RAW(16)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueries_DatabaseUserId",
                table: "DynamicQueries",
                column: "DatabaseUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DynamicQueries_DatabaseUsers_DatabaseUserId",
                table: "DynamicQueries",
                column: "DatabaseUserId",
                principalTable: "DatabaseUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DynamicQueries_DatabaseUsers_DatabaseUserId",
                table: "DynamicQueries");

            migrationBuilder.DropIndex(
                name: "IX_DynamicQueries_DatabaseUserId",
                table: "DynamicQueries");

            migrationBuilder.DropColumn(
                name: "DatabaseUserId",
                table: "DynamicQueries");

            migrationBuilder.DropTable(name: "UserDatabaseUserAccess");
            migrationBuilder.DropTable(name: "DatabaseUsers");
        }
    }
}
