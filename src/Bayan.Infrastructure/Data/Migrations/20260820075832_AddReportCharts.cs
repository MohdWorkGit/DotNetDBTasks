using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ReportCharts — a chart drawn from one of a report's datasets and placed by a
    /// <c>{{CHART:key}}</c> marker.
    ///
    /// <para>
    /// A new table rather than columns on an existing one, so RULE 1 is satisfied by
    /// construction: nothing here alters a table an earlier migration created. Every statement
    /// is still independently idempotent, because Oracle auto-commits DDL and a retry after a
    /// part-applied run must not die on the half that already succeeded.
    /// </para>
    /// </summary>
    public partial class AddReportCharts : Migration
    {
        /// <summary>ORA-00955: name is already used by an existing object.</summary>
        private const int NameAlreadyUsed = -955;

        /// <summary>ORA-00942: table or view does not exist.</summary>
        private const int TableMissing = -942;

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
            Idempotent(migrationBuilder, @"CREATE TABLE ""ReportCharts"" (
                ""Id"" RAW(16) NOT NULL,
                ""ReportId"" RAW(16) NOT NULL,
                ""ChartKey"" NVARCHAR2(100) NOT NULL,
                ""Title"" NVARCHAR2(200),
                ""ChartType"" NUMBER(10) NOT NULL,
                ""DatasetId"" RAW(16) NOT NULL,
                ""CategoryColumn"" NVARCHAR2(128) NOT NULL,
                ""SeriesColumnsJson"" CLOB NOT NULL,
                ""MaxCategories"" NUMBER(10) DEFAULT 25 NOT NULL,
                ""SortOrder"" NUMBER(10) NOT NULL,
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_ReportCharts"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportCharts_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE)", NameAlreadyUsed);

            // The template addresses charts by key, so a duplicate within one report would make
            // its {{CHART:key}} marker ambiguous.
            Idempotent(migrationBuilder,
                @"CREATE UNIQUE INDEX ""IX_ReportCharts_ReportId_ChartKey"" ON ""ReportCharts"" (""ReportId"", ""ChartKey"")",
                NameAlreadyUsed);

            Idempotent(migrationBuilder,
                @"CREATE INDEX ""IX_ReportCharts_DatasetId"" ON ""ReportCharts"" (""DatasetId"")",
                NameAlreadyUsed);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Idempotent(migrationBuilder,
                @"DROP TABLE ""ReportCharts"" CASCADE CONSTRAINTS", TableMissing);
        }
    }
}
