using Bayan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds DynamicQueries.QueryType (0=Select, 1=Insert, 2=Update, 3=Delete, 4=Other), derived
    /// from the leading keyword of SqlQuery. Previously every consumer re-parsed the SQL text;
    /// storing it lets list queries filter by type in the database instead.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260803000002_AddQueryTypeToDynamicQuery")]
    public partial class AddQueryTypeToDynamicQuery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QueryType",
                table: "DynamicQueries",
                type: "NUMBER(10)",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows to match what QueryTypeClassifier.FromSql would return.
            //
            // SqlQuery is a CLOB, so read its head through DBMS_LOB.SUBSTR rather than applying
            // string functions to the LOB directly. LTRIM is given an explicit character set:
            // it strips only spaces by default, whereas C#'s TrimStart() strips all whitespace,
            // so a query stored with a leading newline or tab would otherwise be classified as
            // Other here while the application classified it correctly.
            migrationBuilder.Sql(@"
                UPDATE ""DynamicQueries""
                   SET ""QueryType"" =
                       CASE
                           WHEN UPPER(LTRIM(DBMS_LOB.SUBSTR(""SqlQuery"", 100, 1),
                                            ' ' || CHR(9) || CHR(10) || CHR(13))) LIKE 'SELECT%' THEN 0
                           WHEN UPPER(LTRIM(DBMS_LOB.SUBSTR(""SqlQuery"", 100, 1),
                                            ' ' || CHR(9) || CHR(10) || CHR(13))) LIKE 'INSERT%' THEN 1
                           WHEN UPPER(LTRIM(DBMS_LOB.SUBSTR(""SqlQuery"", 100, 1),
                                            ' ' || CHR(9) || CHR(10) || CHR(13))) LIKE 'UPDATE%' THEN 2
                           WHEN UPPER(LTRIM(DBMS_LOB.SUBSTR(""SqlQuery"", 100, 1),
                                            ' ' || CHR(9) || CHR(10) || CHR(13))) LIKE 'DELETE%' THEN 3
                           ELSE 4
                       END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "QueryType", table: "DynamicQueries");
        }
    }
}
