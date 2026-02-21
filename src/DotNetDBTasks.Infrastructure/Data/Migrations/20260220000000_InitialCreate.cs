using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Username = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "NVARCHAR2(512)", maxLength: 512, nullable: false),
                    FirstName = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    RefreshToken = table.Column<string>(type: "NVARCHAR2(512)", maxLength: 512, nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DynamicQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: false),
                    SqlQuery = table.Column<string>(type: "NVARCHAR2(4000)", maxLength: 4000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 30),
                    CreatedByUserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicQueries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicQueryRoles",
                columns: table => new
                {
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicQueryRoles", x => new { x.DynamicQueryId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_DynamicQueryRoles_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DynamicQueryRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    ParameterType = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IsRequired = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    DefaultValue = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryParameters_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ParametersJson = table.Column<string>(type: "NVARCHAR2(4000)", maxLength: 4000, nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    ExecutionDurationMs = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    RowsReturned = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IsSuccess = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryExecutionLogs_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueryExecutionLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueryRoles_RoleId",
                table: "DynamicQueryRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryExecutionLogs_ExecutedAt",
                table: "QueryExecutionLogs",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_QueryExecutionLogs_DynamicQueryId",
                table: "QueryExecutionLogs",
                column: "DynamicQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryExecutionLogs_UserId",
                table: "QueryExecutionLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryParameters_DynamicQueryId",
                table: "QueryParameters",
                column: "DynamicQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DynamicQueryRoles");
            migrationBuilder.DropTable(name: "QueryExecutionLogs");
            migrationBuilder.DropTable(name: "QueryParameters");
            migrationBuilder.DropTable(name: "UserRoles");
            migrationBuilder.DropTable(name: "DynamicQueries");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
