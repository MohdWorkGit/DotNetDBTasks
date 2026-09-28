using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dashboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DefaultRefreshSeconds = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 60),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashboards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DashboardId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    ParameterType = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IsRequired = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    DefaultValue = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    AllowMultiple = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    DropdownSourceType = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    DropdownStaticValues = table.Column<string>(type: "CLOB", nullable: true),
                    DropdownQueryId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    DropdownQueryValueColumn = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    DropdownQueryLabelColumn = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardFilters_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardRoles",
                columns: table => new
                {
                    DashboardId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardRoles", x => new { x.DashboardId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_DashboardRoles_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardTiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DashboardId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Title = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    SortOrder = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    Width = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 4),
                    Height = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 1),
                    VisualType = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CategoryColumn = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: true),
                    SeriesColumnsJson = table.Column<string>(type: "CLOB", nullable: false),
                    MaxCategories = table.Column<int>(type: "NUMBER(10)", nullable: false, defaultValue: 25),
                    ValueColumn = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: true),
                    CompareColumn = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: true),
                    ValueFormat = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    HigherIsBetter = table.Column<short>(type: "NUMBER(5)", nullable: false),
                    RefreshSeconds = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    DrillAction = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    DrillFilterId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    DrillReportId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    DrillReportParameter = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardTiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardTiles_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardTiles_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DashboardUserGroups",
                columns: table => new
                {
                    DashboardId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardUserGroups", x => new { x.DashboardId, x.UserGroupId });
                    table.ForeignKey(
                        name: "FK_DashboardUserGroups_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardUserGroups_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardUsers",
                columns: table => new
                {
                    DashboardId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardUsers", x => new { x.DashboardId, x.UserId });
                    table.ForeignKey(
                        name: "FK_DashboardUsers_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardTileParameterMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DashboardTileId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    TargetParameterName = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    SourceKind = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    DashboardFilterId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    ConstantValue = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardTileParameterMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardTileParameterMaps_DashboardTiles_DashboardTileId",
                        column: x => x.DashboardTileId,
                        principalTable: "DashboardTiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardFilters_DashboardId_Name",
                table: "DashboardFilters",
                columns: new[] { "DashboardId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardFilters_DropdownQueryId",
                table: "DashboardFilters",
                column: "DropdownQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardRoles_RoleId",
                table: "DashboardRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_Name",
                table: "Dashboards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardTileMaps_Tile_Target",
                table: "DashboardTileParameterMaps",
                columns: new[] { "DashboardTileId", "TargetParameterName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardTileParameterMaps_DashboardFilterId",
                table: "DashboardTileParameterMaps",
                column: "DashboardFilterId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardTiles_DashboardId",
                table: "DashboardTiles",
                column: "DashboardId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardTiles_DynamicQueryId",
                table: "DashboardTiles",
                column: "DynamicQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardUserGroups_UserGroupId",
                table: "DashboardUserGroups",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardUsers_UserId",
                table: "DashboardUsers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardFilters");

            migrationBuilder.DropTable(
                name: "DashboardRoles");

            migrationBuilder.DropTable(
                name: "DashboardTileParameterMaps");

            migrationBuilder.DropTable(
                name: "DashboardUserGroups");

            migrationBuilder.DropTable(
                name: "DashboardUsers");

            migrationBuilder.DropTable(
                name: "DashboardTiles");

            migrationBuilder.DropTable(
                name: "Dashboards");
        }
    }
}
