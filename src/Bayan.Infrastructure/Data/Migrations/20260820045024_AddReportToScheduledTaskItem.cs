using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Lets a scheduled task item run a report instead of a single query.
    ///
    /// <para>
    /// Unlike AddReports, this one alters a table an <b>earlier release</b> created and that
    /// already holds rows, so each statement has to tolerate having been applied before: Oracle
    /// auto-commits DDL, and a retry after a part-applied run must not die on the half that
    /// already succeeded. See DATABASE_MIGRATION_NOTES.txt section 1B and RULE 2.
    /// </para>
    ///
    /// <para>
    /// Existing rows are untouched: every one keeps its DynamicQueryId, and ReportId stays null.
    /// The column becomes nullable only so a <i>new</i> item can carry a report instead — which
    /// is why the application layer, not the database, enforces that exactly one of the two is set.
    /// A CHECK constraint would have been the stricter place for that, but it would also reject
    /// the existing rows of any installation that had a data problem predating this change,
    /// turning an upgrade into an outage.
    /// </para>
    /// </summary>
    public partial class AddReportToScheduledTaskItem : Migration
    {
        /// <summary>ORA-00955: name is already used by an existing object.</summary>
        private const int NameAlreadyUsed = -955;

        /// <summary>ORA-01430: column being added already exists in table.</summary>
        private const int ColumnAlreadyExists = -1430;

        /// <summary>ORA-01451: column already permits nulls, so this MODIFY is a no-op.</summary>
        private const int ColumnAlreadyNullable = -1451;

        /// <summary>ORA-02275: such a referential constraint already exists in the table.</summary>
        private const int ConstraintAlreadyExists = -2275;

        /// <summary>ORA-00942: table or view does not exist.</summary>
        private const int TableMissing = -942;

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
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" ADD ""ReportId"" RAW(16)",
                ColumnAlreadyExists);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" MODIFY (""DynamicQueryId"" NULL)",
                ColumnAlreadyNullable);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" ADD CONSTRAINT ""FK_ScheduledTaskItems_Reports_ReportId"" " +
                @"FOREIGN KEY (""ReportId"") REFERENCES ""Reports"" (""Id"")",
                ConstraintAlreadyExists);

            Idempotent(migrationBuilder,
                @"CREATE INDEX ""IX_ScheduledTaskItems_ReportId"" ON ""ScheduledTaskItems"" (""ReportId"")",
                NameAlreadyUsed);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"DROP INDEX ""IX_ScheduledTaskItems_ReportId""", IndexMissing);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" DROP CONSTRAINT ""FK_ScheduledTaskItems_Reports_ReportId""",
                ConstraintMissing);

            // Reverting to NOT NULL is only safe once every row has a query again; a row that
            // ran a report has none, so those are cleared first rather than blocking the rollback.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'DELETE FROM ""ScheduledTaskItems"" WHERE ""DynamicQueryId"" IS NULL';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != " + TableMissing + @" THEN RAISE; END IF;
                END;");

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" MODIFY (""DynamicQueryId"" NOT NULL)",
                ColumnAlreadyNullable);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""ScheduledTaskItems"" DROP COLUMN ""ReportId""", ColumnAlreadyExists);
        }
    }
}
