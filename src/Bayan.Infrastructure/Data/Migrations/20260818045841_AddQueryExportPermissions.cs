using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Puts result exports behind two gates: the query lists which formats it may be exported
    /// as, and the role says which of those a person may use.
    ///
    /// <para>
    /// <b>This upgrade closes export everywhere.</b> Unlike <c>AddRolePermissions</c>, which was
    /// written to change nothing on the day it ran, this one deliberately starts from "no": every
    /// existing query gets a NULL <c>AllowedExportFormats</c>, and no role is granted any
    /// <c>queries.export*</c> permission. Downloads stop working until an administrator opens
    /// them per query on the query form and per role on the Roles and Permissions page. That is
    /// the chosen behaviour, not an oversight, but it does mean the first thing to do after
    /// upgrading is to walk the query list.
    /// </para>
    ///
    /// <para>
    /// Scheduled tasks are unaffected: they write files server-side from a definition an
    /// administrator already controls, and gating them here would stop every existing export
    /// job overnight. See README (Query export permissions).
    /// </para>
    /// </summary>
    public partial class AddQueryExportPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw SQL rather than AddColumn: Oracle auto-commits DDL, so a migration that fails
            // part-way leaves the column behind and the retry must not die on ORA-01430
            // ("column already exists"). See DATABASE_MIGRATION_NOTES.txt section 1B.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'ALTER TABLE ""DynamicQueries""
                                       ADD ""AllowedExportFormats"" NVARCHAR2(100)';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -1430 THEN RAISE; END IF;
                END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The permission rows go too, otherwise rolling back leaves roles holding
            // capabilities the code no longer defines, which the matrix renders as blank rows.
            migrationBuilder.Sql(@"
                DELETE FROM ""RolePermissions""
                WHERE ""Permission"" IN ('queries.exportExcel', 'queries.exportCsv',
                                        'queries.exportJson', 'queries.exportPdf',
                                        'queries.exportWord')");

            // ORA-00904 is "invalid identifier": the column is already gone, so the drop is a
            // no-op rather than a failure.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'ALTER TABLE ""DynamicQueries""
                                       DROP COLUMN ""AllowedExportFormats""';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -904 THEN RAISE; END IF;
                END;");
        }
    }
}
