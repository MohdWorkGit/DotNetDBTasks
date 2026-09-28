using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Dashboards, second round: KPI summary modes, targets and colour rules on tiles, and a
    /// dashboard as the third kind of scheduled-task item.
    ///
    /// <para>
    /// Every statement alters a table that already holds rows, so each has to tolerate having
    /// been applied before: Oracle auto-commits DDL, and a retry after a part-applied run must not
    /// die on the half that already succeeded. Same pattern as AddReportToScheduledTaskItem; see
    /// DATABASE_MIGRATION_NOTES.txt section 1B and RULE 2.
    /// </para>
    ///
    /// <para>
    /// Existing rows need nothing: a tile's new columns default to today's behaviour (the last-row
    /// KPI, no target, no rules), and every existing scheduled item keeps its query or report with
    /// DashboardId null.
    /// </para>
    /// </summary>
    public partial class AddDashboardRound2 : Migration
    {
        /// <summary>ORA-00955: name is already used by an existing object.</summary>
        private const int NameAlreadyUsed = -955;

        /// <summary>ORA-01430: column being added already exists in table.</summary>
        private const int ColumnAlreadyExists = -1430;

        /// <summary>ORA-02275: such a referential constraint already exists in the table.</summary>
        private const int ConstraintAlreadyExists = -2275;

        /// <summary>ORA-00904: invalid identifier — here, the column is already gone.</summary>
        private const int ColumnMissing = -904;

        /// <summary>ORA-02443: cannot drop constraint - nonexistent constraint.</summary>
        private const int ConstraintMissing = -2443;

        /// <summary>ORA-01418: specified index does not exist.</summary>
        private const int IndexMissing = -1418;

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
            // --- tiles
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" ADD ""KpiAggregate"" NUMBER(10) DEFAULT 0 NOT NULL",
                ColumnAlreadyExists);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" ADD ""TargetValue"" DECIMAL(18,4)",
                ColumnAlreadyExists);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" ADD ""TargetWarnPercent"" NUMBER(10) DEFAULT 10 NOT NULL",
                ColumnAlreadyExists);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DashboardTiles"" ADD ""ConditionalRulesJson"" CLOB",
                ColumnAlreadyExists);

            // --- scheduled items
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" ADD ""DashboardId"" RAW(16)",
                ColumnAlreadyExists);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" ADD CONSTRAINT ""FK_ScheduledTaskItems_Dashboards_DashboardId"" " +
                @"FOREIGN KEY (""DashboardId"") REFERENCES ""Dashboards"" (""Id"")",
                ConstraintAlreadyExists);

            Idempotent(migrationBuilder,
                @"CREATE INDEX ""IX_ScheduledTaskItems_DashboardId"" ON ""ScheduledTaskItems"" (""DashboardId"")",
                NameAlreadyUsed);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A dashboard item cannot survive losing its column; remove them before the column.
            migrationBuilder.Sql(@"DELETE FROM ""ScheduledTaskItems"" WHERE ""DashboardId"" IS NOT NULL");

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" DROP CONSTRAINT ""FK_ScheduledTaskItems_Dashboards_DashboardId""",
                ConstraintMissing);

            Idempotent(migrationBuilder,
                @"DROP INDEX ""IX_ScheduledTaskItems_DashboardId""",
                IndexMissing);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" DROP COLUMN ""DashboardId""",
                ColumnMissing);

            foreach (var column in new[] { "ConditionalRulesJson", "KpiAggregate", "TargetValue", "TargetWarnPercent" })
            {
                Idempotent(migrationBuilder,
                    $@"ALTER TABLE ""DashboardTiles"" DROP COLUMN ""{column}""",
                    ColumnMissing);
            }
        }
    }
}
