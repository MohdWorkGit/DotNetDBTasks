using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Moves authorization out of the <c>[Authorize(Roles = ...)]</c> attributes and into a
    /// role → permission matrix an administrator can edit.
    ///
    /// <para>
    /// Every seeded role is given exactly the capabilities it already had, so the upgrade
    /// changes nothing on the day it runs. That includes the two runtime toggles this replaces:
    /// an installation that had turned per-query access <em>on</em>, or user-group management
    /// <em>off</em>, keeps that decision as a permission rather than losing it.
    /// </para>
    ///
    /// <para>
    /// Admin gets no rows on purpose — it is pinned to every permission in code, so it cannot
    /// be edited into a state where nobody can open the Permissions tab.
    /// </para>
    /// </summary>
    public partial class AddRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "IsSeeded",
                table: "Roles",
                type: "NUMBER(5)",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Permission = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            SeedCurrentBehaviour(migrationBuilder);
        }

        /// <summary>
        /// Grants each seeded role what it could already do. A no-op on a fresh database, where
        /// the roles do not exist yet — <c>DatabaseSeeder</c> applies the same defaults there.
        /// </summary>
        private static void SeedCurrentBehaviour(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""Roles"" SET ""IsSeeded"" = 1
                WHERE ""Name"" IN ('Admin', 'User', 'Auditor', 'AccessManager')");

            // NOT EXISTS rather than a bare INSERT: the seeder may have run first on a database
            // that was created and migrated in one startup.
            migrationBuilder.Sql(@"
                INSERT INTO ""RolePermissions"" (""RoleId"", ""Permission"")
                SELECT r.""Id"", d.p
                FROM ""Roles"" r
                JOIN (
                    SELECT 'User'          AS rolename, 'queries.run'              AS p FROM dual
                    UNION ALL SELECT 'Auditor',         'logs.view'                     FROM dual
                    UNION ALL SELECT 'Auditor',         'audit.view'                    FROM dual
                    UNION ALL SELECT 'Auditor',         'scheduledTasks.viewAll'        FROM dual
                    UNION ALL SELECT 'AccessManager',   'queries.view'                  FROM dual
                    UNION ALL SELECT 'AccessManager',   'access.manageGroup'            FROM dual
                    UNION ALL SELECT 'AccessManager',   'userGroups.view'               FROM dual
                    UNION ALL SELECT 'AccessManager',   'userGroups.manage'             FROM dual
                    UNION ALL SELECT 'AccessManager',   'users.view'                    FROM dual
                    UNION ALL SELECT 'AccessManager',   'users.manage'                  FROM dual
                    UNION ALL SELECT 'AccessManager',   'directory.view'                FROM dual
                ) d ON d.rolename = r.""Name""
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""RolePermissions"" rp
                    WHERE rp.""RoleId"" = r.""Id"" AND rp.""Permission"" = d.p)");

            // The two settings this matrix replaces. Only a row that disagrees with the old
            // default has anything to say — the rest already matches what was just seeded.
            migrationBuilder.Sql(@"
                INSERT INTO ""RolePermissions"" (""RoleId"", ""Permission"")
                SELECT r.""Id"", 'access.manageQuery'
                FROM ""Roles"" r
                WHERE r.""Name"" = 'AccessManager'
                  AND EXISTS (SELECT 1 FROM ""SystemSettings"" s
                              WHERE s.""Key"" = 'accessManager.canManageQueryAccess'
                                AND LOWER(s.""Value"") = 'true')
                  AND NOT EXISTS (SELECT 1 FROM ""RolePermissions"" rp
                                  WHERE rp.""RoleId"" = r.""Id""
                                    AND rp.""Permission"" = 'access.manageQuery')");

            migrationBuilder.Sql(@"
                DELETE FROM ""RolePermissions""
                WHERE ""Permission"" = 'userGroups.manage'
                  AND ""RoleId"" IN (SELECT ""Id"" FROM ""Roles"" WHERE ""Name"" = 'AccessManager')
                  AND EXISTS (SELECT 1 FROM ""SystemSettings"" s
                              WHERE s.""Key"" = 'accessManager.canManageUserGroups'
                                AND LOWER(s.""Value"") = 'false')");

            // Their keys are retired now that the matrix owns both decisions; leaving them would
            // leave a Settings page reading rows nothing consults.
            migrationBuilder.Sql(@"
                DELETE FROM ""SystemSettings""
                WHERE ""Key"" IN ('accessManager.canManageQueryAccess',
                                 'accessManager.canManageUserGroups')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the two settings from the matrix, so rolling back keeps the decisions
            // rather than silently reverting them to the compiled defaults.
            migrationBuilder.Sql(@"
                INSERT INTO ""SystemSettings"" (""Id"", ""Key"", ""Value"", ""CreatedAt"")
                SELECT SYS_GUID(), 'accessManager.canManageQueryAccess',
                       CASE WHEN EXISTS (
                           SELECT 1 FROM ""RolePermissions"" rp
                           JOIN ""Roles"" r ON r.""Id"" = rp.""RoleId""
                           WHERE r.""Name"" = 'AccessManager' AND rp.""Permission"" = 'access.manageQuery')
                       THEN 'true' ELSE 'false' END,
                       SYSTIMESTAMP
                FROM dual
                WHERE NOT EXISTS (SELECT 1 FROM ""SystemSettings""
                                  WHERE ""Key"" = 'accessManager.canManageQueryAccess')");

            migrationBuilder.Sql(@"
                INSERT INTO ""SystemSettings"" (""Id"", ""Key"", ""Value"", ""CreatedAt"")
                SELECT SYS_GUID(), 'accessManager.canManageUserGroups',
                       CASE WHEN EXISTS (
                           SELECT 1 FROM ""RolePermissions"" rp
                           JOIN ""Roles"" r ON r.""Id"" = rp.""RoleId""
                           WHERE r.""Name"" = 'AccessManager' AND rp.""Permission"" = 'userGroups.manage')
                       THEN 'true' ELSE 'false' END,
                       SYSTIMESTAMP
                FROM dual
                WHERE NOT EXISTS (SELECT 1 FROM ""SystemSettings""
                                  WHERE ""Key"" = 'accessManager.canManageUserGroups')");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "IsSeeded",
                table: "Roles");
        }
    }
}
