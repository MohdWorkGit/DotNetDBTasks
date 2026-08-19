using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ScheduledTaskViewerRoles and ScheduledTaskViewerUserGroups — the role- and
    /// group-level counterparts of ScheduledTaskViewers, so a scheduled task's status
    /// visibility can be granted the same three ways query access already is.
    ///
    /// <para>
    /// Both carry <c>CanDownloadFiles</c>, the same second permission the per-user grant
    /// carries: a user sees the task when any grant reaches them, and downloads its export
    /// files when any reaching grant allows it. Existing per-user grants are untouched, so
    /// this changes nobody's access on the day it runs.
    /// </para>
    ///
    /// <para>
    /// Raw SQL rather than CreateTable: Oracle auto-commits DDL, so a migration that fails
    /// part-way leaves its tables behind and the retry must not die on ORA-00955 ("name is
    /// already used by an existing object"). See DATABASE_MIGRATION_NOTES.txt section 1B.
    /// </para>
    /// </summary>
    public partial class AddScheduledTaskViewerRolesAndGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'CREATE TABLE ""ScheduledTaskViewerRoles"" (
                        ""ScheduledTaskId"" RAW(16) NOT NULL,
                        ""RoleId"" RAW(16) NOT NULL,
                        ""CanDownloadFiles"" NUMBER(5) DEFAULT 0 NOT NULL,
                        CONSTRAINT ""PK_ScheduledTaskViewerRoles"" PRIMARY KEY (""ScheduledTaskId"", ""RoleId""),
                        CONSTRAINT ""FK_ScheduledTaskViewerRoles_ScheduledTasks_ScheduledTaskId"" FOREIGN KEY (""ScheduledTaskId"")
                            REFERENCES ""ScheduledTasks"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_ScheduledTaskViewerRoles_Roles_RoleId"" FOREIGN KEY (""RoleId"")
                            REFERENCES ""Roles"" (""Id"") ON DELETE CASCADE)';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -955 THEN RAISE; END IF;
                END;");

            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'CREATE INDEX ""IX_ScheduledTaskViewerRoles_RoleId""
                                       ON ""ScheduledTaskViewerRoles"" (""RoleId"")';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -955 THEN RAISE; END IF;
                END;");

            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'CREATE TABLE ""ScheduledTaskViewerUserGroups"" (
                        ""ScheduledTaskId"" RAW(16) NOT NULL,
                        ""UserGroupId"" RAW(16) NOT NULL,
                        ""CanDownloadFiles"" NUMBER(5) DEFAULT 0 NOT NULL,
                        CONSTRAINT ""PK_ScheduledTaskViewerUserGroups"" PRIMARY KEY (""ScheduledTaskId"", ""UserGroupId""),
                        CONSTRAINT ""FK_ScheduledTaskViewerUserGroups_ScheduledTasks_ScheduledTaskId"" FOREIGN KEY (""ScheduledTaskId"")
                            REFERENCES ""ScheduledTasks"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_ScheduledTaskViewerUserGroups_UserGroups_UserGroupId"" FOREIGN KEY (""UserGroupId"")
                            REFERENCES ""UserGroups"" (""Id"") ON DELETE CASCADE)';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -955 THEN RAISE; END IF;
                END;");

            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'CREATE INDEX ""IX_ScheduledTaskViewerUserGroups_UserGroupId""
                                       ON ""ScheduledTaskViewerUserGroups"" (""UserGroupId"")';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -955 THEN RAISE; END IF;
                END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ORA-00942 is "table or view does not exist": already gone is a no-op, not a failure.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'DROP TABLE ""ScheduledTaskViewerUserGroups"" CASCADE CONSTRAINTS';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -942 THEN RAISE; END IF;
                END;");

            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'DROP TABLE ""ScheduledTaskViewerRoles"" CASCADE CONSTRAINTS';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -942 THEN RAISE; END IF;
                END;");
        }
    }
}
