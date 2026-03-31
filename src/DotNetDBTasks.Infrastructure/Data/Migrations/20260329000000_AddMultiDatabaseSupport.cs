using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ServerType and DatabaseName columns to DatabaseUsers table
    /// to support SQL Server, PostgreSQL, and MySQL connections.
    ///
    /// NOTE: These columns are now also included in the AddDatabaseUsers CreateTable
    /// call so that fresh databases get them immediately. This migration is kept for
    /// backward-compatibility with databases that already ran AddDatabaseUsers without
    /// the columns. All statements are idempotent (Oracle PL/SQL checks) so they
    /// safely no-op when the columns already exist.
    /// </summary>
    public partial class AddMultiDatabaseSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: add ServerType only if missing (Oracle does not support IF NOT EXISTS on DDL).
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'ALTER TABLE ""DatabaseUsers"" ADD ""ServerType"" NUMBER(10) DEFAULT 0 NOT NULL';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -1430 THEN RAISE; END IF;
                END;
            ");

            // Idempotent: add DatabaseName only if missing.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'ALTER TABLE ""DatabaseUsers"" ADD ""DatabaseName"" NVARCHAR2(200) NULL';
                EXCEPTION
                    WHEN OTHERS THEN
                        IF SQLCODE != -1430 THEN RAISE; END IF;
                END;
            ");

            // Make ServiceName nullable (Oracle treats empty string as NULL).
            // Safe to run multiple times — Oracle does not error on redundant MODIFY to same type.
            migrationBuilder.Sql(@"
                BEGIN
                    EXECUTE IMMEDIATE 'ALTER TABLE ""DatabaseUsers"" MODIFY ""ServiceName"" NVARCHAR2(200) NULL';
                EXCEPTION
                    WHEN OTHERS THEN NULL;
                END;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatabaseName",
                table: "DatabaseUsers");

            migrationBuilder.DropColumn(
                name: "ServerType",
                table: "DatabaseUsers");
        }
    }
}
