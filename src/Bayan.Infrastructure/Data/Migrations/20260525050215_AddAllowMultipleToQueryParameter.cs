using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowMultipleToQueryParameter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Oracle EF Core can't materialize a typed defaultValue when the CLR
            // property is bool but the column is NUMBER(1) (throws Int16→Bool cast
            // during SQL generation). defaultValueSql emits the literal "0"
            // directly, bypassing the type-conversion path.
            migrationBuilder.AddColumn<short>(
                name: "AllowMultiple",
                table: "QueryParameters",
                type: "NUMBER(1)",
                nullable: false,
                defaultValueSql: "0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowMultiple",
                table: "QueryParameters");
        }
    }
}
