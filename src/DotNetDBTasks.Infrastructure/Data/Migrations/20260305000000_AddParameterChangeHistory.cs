using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetDBTasks.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParameterChangeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DynamicQueryId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ParameterName = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    ChangeType = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false),
                    FieldName = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "CLOB", nullable: true),
                    NewValue = table.Column<string>(type: "CLOB", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParameterChangeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParameterChangeHistories_DynamicQueries_DynamicQueryId",
                        column: x => x.DynamicQueryId,
                        principalTable: "DynamicQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParameterChangeHistories_DynamicQueryId",
                table: "ParameterChangeHistories",
                column: "DynamicQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_ParameterChangeHistories_ChangedAt",
                table: "ParameterChangeHistories",
                column: "ChangedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParameterChangeHistories");
        }
    }
}
