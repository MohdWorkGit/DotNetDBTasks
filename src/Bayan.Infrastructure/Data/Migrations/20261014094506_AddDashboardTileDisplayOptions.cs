using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Presentation options on dashboard tiles — legend, data labels, sort, stacking, axis range,
    /// split-by and the like — as one nullable JSON column, so later options need no migration.
    ///
    /// <para>
    /// Alters a table that already holds rows, so the statement tolerates having been applied
    /// before: Oracle auto-commits DDL, and a retry after a part-applied run must not die on the
    /// half that already succeeded. Same pattern as AddDashboardRound2; see
    /// DATABASE_MIGRATION_NOTES.txt RULE 2.
    /// </para>
    ///
    /// <para>Existing tiles need nothing: null means every option at its default, which is how
    /// they drew before.</para>
    /// </summary>
    public partial class AddDashboardTileDisplayOptions : Migration
    {
        /// <summary>ORA-01430: column being added already exists in table.</summary>
        private const int ColumnAlreadyExists = -1430;

        /// <summary>ORA-00904: invalid identifier — here, the column is already gone.</summary>
        private const int ColumnMissing = -904;

        /// <summary>
        /// Runs one DDL statement, swallowing only <paramref name="ignoredSqlCode"/> so a re-run
        /// succeeds while every other Oracle error still raises.
        /// </summary>
        private static void Idempotent(MigrationBuilder migrationBuilder, string ddl, int ignoredSqlCode)
        {
            migrationBuilder.Sql($@"
                BEGIN
                    EXECUTE IMMEDIATE '{ddl}';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != {ignoredSqlCode} THEN RAISE; END IF;
                END;");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" ADD ""DisplayOptionsJson"" CLOB",
                ColumnAlreadyExists);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" DROP COLUMN ""DisplayOptionsJson""",
                ColumnMissing);
        }
    }
}
