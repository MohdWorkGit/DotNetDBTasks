using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Moves query and query-group access off the Active Directory department attribute and
    /// onto UserGroups, which this application owns.
    ///
    /// <para>
    /// Existing access is carried across rather than dropped: every department that currently
    /// grants something becomes a user group of the same name, seeded with the users whose AD
    /// department matched at the moment of migration, and each department grant is re-pointed
    /// at that group. Nobody's access changes on the day of the upgrade; from then on
    /// membership is edited here instead of tracking whatever the directory says.
    /// </para>
    ///
    /// <para>
    /// Matching is case-sensitive, exactly as the old <c>Department = :dept</c> check was, so
    /// two departments differing only in case stay two groups.
    /// </para>
    /// </summary>
    public partial class SwitchAccessFromAdDepartmentsToUserGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGroups",
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
                    table.PrimaryKey("PK_UserGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DynamicQueryUserGroups",
                columns: table => new
                {
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicQueryUserGroups", x => new { x.DynamicQueryId, x.UserGroupId });
                    table.ForeignKey(
                        name: "FK_DynamicQueryUserGroups_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DynamicQueryUserGroups_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryGroupUserGroups",
                columns: table => new
                {
                    QueryGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryGroupUserGroups", x => new { x.QueryGroupId, x.UserGroupId });
                    table.ForeignKey(
                        name: "FK_QueryGroupUserGroups_QueryGroups_QueryGroupId",
                        column: x => x.QueryGroupId,
                        principalTable: "QueryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueryGroupUserGroups_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroupMembers",
                columns: table => new
                {
                    UserGroupId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    UserId = table.Column<Guid>(type: "RAW(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroupMembers", x => new { x.UserGroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_UserGroupMembers_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroupMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicQueryUserGroups_UserGroupId",
                table: "DynamicQueryUserGroups",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_QueryGroupUserGroups_UserGroupId",
                table: "QueryGroupUserGroups",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_UserId",
                table: "UserGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_Name",
                table: "UserGroups",
                column: "Name",
                unique: true);

            MigrateDepartmentGrants(migrationBuilder);

            migrationBuilder.DropTable(
                name: "DynamicQueryDepartments");

            migrationBuilder.DropTable(
                name: "QueryGroupDepartments");
        }

        /// <summary>
        /// Copies the department-based grants into the new tables. Runs before the old tables
        /// are dropped, and is a no-op on a database that never used them.
        /// </summary>
        private static void MigrateDepartmentGrants(MigrationBuilder migrationBuilder)
        {
            // One group per department that actually grants something. Departments nobody was
            // ever granted through are left behind on purpose: they are directory trivia, and
            // importing them would fill the picker with groups that mean nothing here.
            // CreatedByUserId is the empty GUID — no administrator created these.
            migrationBuilder.Sql(@"
                INSERT INTO ""UserGroups"" (""Id"", ""Name"", ""Description"", ""CreatedByUserId"", ""CreatedAt"")
                SELECT SYS_GUID(), d.""Department"",
                       'Created from the Active Directory department of the same name.',
                       HEXTORAW('00000000000000000000000000000000'), SYSTIMESTAMP
                FROM (SELECT ""Department"" FROM ""DynamicQueryDepartments""
                      UNION
                      SELECT ""Department"" FROM ""QueryGroupDepartments"") d
                WHERE NOT EXISTS (SELECT 1 FROM ""UserGroups"" g WHERE g.""Name"" = d.""Department"")");

            // Membership as it stands today. This is a snapshot, not a link: a later change in
            // AD will not move anyone in or out.
            migrationBuilder.Sql(@"
                INSERT INTO ""UserGroupMembers"" (""UserGroupId"", ""UserId"")
                SELECT g.""Id"", u.""Id""
                FROM ""UserGroups"" g
                JOIN ""Users"" u ON u.""Department"" = g.""Name""");

            migrationBuilder.Sql(@"
                INSERT INTO ""DynamicQueryUserGroups"" (""DynamicQueryId"", ""UserGroupId"")
                SELECT qd.""DynamicQueryId"", g.""Id""
                FROM ""DynamicQueryDepartments"" qd
                JOIN ""UserGroups"" g ON g.""Name"" = qd.""Department""");

            migrationBuilder.Sql(@"
                INSERT INTO ""QueryGroupUserGroups"" (""QueryGroupId"", ""UserGroupId"")
                SELECT gd.""QueryGroupId"", g.""Id""
                FROM ""QueryGroupDepartments"" gd
                JOIN ""UserGroups"" g ON g.""Name"" = gd.""Department""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            // Only grants whose group still carries a department's name can be expressed in the
            // old shape; a group created here has no department to map back to and is dropped
            // with the tables below.
            migrationBuilder.Sql(@"
                INSERT INTO ""DynamicQueryDepartments"" (""DynamicQueryId"", ""Department"")
                SELECT qg.""DynamicQueryId"", g.""Name""
                FROM ""DynamicQueryUserGroups"" qg
                JOIN ""UserGroups"" g ON g.""Id"" = qg.""UserGroupId""");

            migrationBuilder.Sql(@"
                INSERT INTO ""QueryGroupDepartments"" (""QueryGroupId"", ""Department"")
                SELECT gg.""QueryGroupId"", g.""Name""
                FROM ""QueryGroupUserGroups"" gg
                JOIN ""UserGroups"" g ON g.""Id"" = gg.""UserGroupId""");

            migrationBuilder.DropTable(
                name: "DynamicQueryUserGroups");

            migrationBuilder.DropTable(
                name: "QueryGroupUserGroups");

            migrationBuilder.DropTable(
                name: "UserGroupMembers");

            migrationBuilder.DropTable(
                name: "UserGroups");
        }
    }
}
