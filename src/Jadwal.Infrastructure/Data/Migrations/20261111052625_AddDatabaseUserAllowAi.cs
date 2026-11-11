using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jadwal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds the per-connection "Allow AI" switch: whether the AI assistant may read the
    /// connection's tables and columns to write SQL. Every existing connection starts with it off,
    /// so nothing is opened up by the upgrade — an administrator switches it on per connection,
    /// for accounts that can read data but not change it (docs/LOCAL-AI-OPTIONS.md §7).
    /// </summary>
    public partial class AddDatabaseUserAllowAi : Migration
    {
        /// <summary>ORA-01430: column being added already exists in table.</summary>
        private const int ColumnAlreadyExists = -1430;

        /// <summary>ORA-00904: invalid identifier — the column is already gone.</summary>
        private const int ColumnMissing = -904;

        /// <summary>
        /// Raw SQL rather than AddColumn: Oracle auto-commits DDL, so a migration that fails
        /// part-way must be safe to run again (DATABASE_MIGRATION_NOTES.txt, rule 2). The default
        /// is plain SQL too — a typed bool default on a NUMBER(1) column throws an Int16→Bool cast
        /// in the Oracle provider (see AddAllowMultipleToQueryParameter).
        /// </summary>
        private static void Idempotent(MigrationBuilder migrationBuilder, string ddl, int ignoredSqlCode) =>
            migrationBuilder.Sql($@"
                BEGIN
                    EXECUTE IMMEDIATE '{ddl}';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != {ignoredSqlCode} THEN RAISE; END IF;
                END;");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DatabaseUsers"" ADD ""AllowAi"" NUMBER(1) DEFAULT 0 NOT NULL",
                ColumnAlreadyExists);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""DatabaseUsers"" DROP COLUMN ""AllowAi""",
                ColumnMissing);
        }
    }
}
