using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Lets a query sit in several query groups: the single <c>DynamicQueries.QueryGroupId</c>
    /// becomes the <c>QueryGroupQueries</c> join table, and each query's current group is copied
    /// into it before the column goes, so nothing is regrouped by the upgrade.
    ///
    /// <para>
    /// Every statement tolerates having been applied before: Oracle auto-commits DDL, and a retry
    /// after a part-applied run must not die on the half that already succeeded. Same pattern as
    /// AddDashboardRound2; see DATABASE_MIGRATION_NOTES.txt RULE 2. The copy is guarded the same
    /// way — it runs only while the old column still exists, and skips filings already copied.
    /// </para>
    /// </summary>
    public partial class AddQueryGroupMemberships : Migration
    {
        /// <summary>ORA-00955: name is already used by an existing object.</summary>
        private const int NameAlreadyUsed = -955;

        /// <summary>ORA-00942: table or view does not exist.</summary>
        private const int TableMissing = -942;

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
        /// Runs one statement, swallowing only <paramref name="ignoredSqlCode"/> so a re-run
        /// succeeds while every other Oracle error still raises.
        /// </summary>
        private static void Idempotent(MigrationBuilder migrationBuilder, string sql, int ignoredSqlCode)
        {
            migrationBuilder.Sql($@"
                BEGIN
                    EXECUTE IMMEDIATE '{sql}';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != {ignoredSqlCode} THEN RAISE; END IF;
                END;");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"CREATE TABLE ""QueryGroupQueries"" (" +
                @"""QueryGroupId"" RAW(16) NOT NULL, " +
                @"""DynamicQueryId"" RAW(16) NOT NULL, " +
                @"CONSTRAINT ""PK_QueryGroupQueries"" PRIMARY KEY (""QueryGroupId"", ""DynamicQueryId""), " +
                @"CONSTRAINT ""FK_QueryGroupQueries_DynamicQueries_DynamicQueryId"" FOREIGN KEY (""DynamicQueryId"") " +
                @"REFERENCES ""DynamicQueries"" (""Id"") ON DELETE CASCADE, " +
                @"CONSTRAINT ""FK_QueryGroupQueries_QueryGroups_QueryGroupId"" FOREIGN KEY (""QueryGroupId"") " +
                @"REFERENCES ""QueryGroups"" (""Id"") ON DELETE CASCADE)",
                NameAlreadyUsed);

            Idempotent(migrationBuilder,
                @"CREATE INDEX ""IX_QueryGroupQueries_DynamicQueryId"" ON ""QueryGroupQueries"" (""DynamicQueryId"")",
                NameAlreadyUsed);

            // Dynamic SQL, so a re-run after the column is gone fails with ORA-00904 at execution
            // (and is ignored) rather than at compile time of the block.
            Idempotent(migrationBuilder,
                @"INSERT INTO ""QueryGroupQueries"" (""QueryGroupId"", ""DynamicQueryId"") " +
                @"SELECT q.""QueryGroupId"", q.""Id"" FROM ""DynamicQueries"" q " +
                @"WHERE q.""QueryGroupId"" IS NOT NULL AND NOT EXISTS (" +
                @"SELECT 1 FROM ""QueryGroupQueries"" m " +
                @"WHERE m.""QueryGroupId"" = q.""QueryGroupId"" AND m.""DynamicQueryId"" = q.""Id"")",
                ColumnMissing);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DynamicQueries"" DROP CONSTRAINT ""FK_DynamicQueries_QueryGroups_QueryGroupId""",
                ConstraintMissing);

            Idempotent(migrationBuilder,
                @"DROP INDEX ""IX_DynamicQueries_QueryGroupId""",
                IndexMissing);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DynamicQueries"" DROP COLUMN ""QueryGroupId""",
                ColumnMissing);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DynamicQueries"" ADD ""QueryGroupId"" RAW(16)",
                ColumnAlreadyExists);

            // One column holds one group, so a query filed in several keeps only one of them.
            Idempotent(migrationBuilder,
                @"UPDATE ""DynamicQueries"" q SET ""QueryGroupId"" = (" +
                @"SELECT MIN(m.""QueryGroupId"") FROM ""QueryGroupQueries"" m WHERE m.""DynamicQueryId"" = q.""Id"") " +
                @"WHERE q.""QueryGroupId"" IS NULL",
                TableMissing);

            Idempotent(migrationBuilder,
                @"CREATE INDEX ""IX_DynamicQueries_QueryGroupId"" ON ""DynamicQueries"" (""QueryGroupId"")",
                NameAlreadyUsed);

            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DynamicQueries"" ADD CONSTRAINT ""FK_DynamicQueries_QueryGroups_QueryGroupId"" " +
                @"FOREIGN KEY (""QueryGroupId"") REFERENCES ""QueryGroups"" (""Id"") ON DELETE SET NULL",
                ConstraintAlreadyExists);

            Idempotent(migrationBuilder,
                @"DROP TABLE ""QueryGroupQueries""",
                TableMissing);
        }
    }
}
