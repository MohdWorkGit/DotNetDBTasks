using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert the Auditor role if it does not already exist.
            migrationBuilder.Sql(@"
                BEGIN
                    INSERT INTO ""Roles"" (""Id"", ""Name"", ""Description"", ""CreatedAt"")
                    SELECT SYS_GUID(), 'Auditor', 'Auditor with access to execution logs and query accessibility management', SYSTIMESTAMP
                    FROM DUAL
                    WHERE NOT EXISTS (SELECT 1 FROM ""Roles"" WHERE ""Name"" = 'Auditor');
                    COMMIT;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the Auditor role and all associated user-role assignments.
            migrationBuilder.Sql(@"
                BEGIN
                    DELETE FROM ""UserRoles""
                    WHERE ""RoleId"" IN (SELECT ""Id"" FROM ""Roles"" WHERE ""Name"" = 'Auditor');

                    DELETE FROM ""DynamicQueryRoles""
                    WHERE ""RoleId"" IN (SELECT ""Id"" FROM ""Roles"" WHERE ""Name"" = 'Auditor');

                    DELETE FROM ""Roles"" WHERE ""Name"" = 'Auditor';
                    COMMIT;
                END;
            ");
        }
    }
}
