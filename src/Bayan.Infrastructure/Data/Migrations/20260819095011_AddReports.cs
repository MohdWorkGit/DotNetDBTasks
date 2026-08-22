using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bayan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds the Reports schema. A report is several saved queries composed into one templated
    /// document, so it needs its own datasets, its own shared parameters, the mapping that fans
    /// one parameter out across those datasets, access grants shaped exactly like a query's,
    /// and a run log.
    ///
    /// <para>
    /// Raw SQL rather than the scaffolded CreateTable calls: Oracle auto-commits DDL, so a
    /// migration that fails part-way leaves its tables behind and the retry must not die on
    /// ORA-00955 (name is already used by an existing object). Every statement below is
    /// therefore independently idempotent. See DATABASE_MIGRATION_NOTES.txt section 1B.
    /// </para>
    ///
    /// <para>
    /// Every column these tables will ever need is created here, including the join and
    /// master-detail columns that stay null until those source types ship. RULE 1 exists
    /// because adding a column later to a table an earlier migration created is the exact
    /// failure that broke AddMultiDatabaseSupport.
    /// </para>
    /// </summary>
    public partial class AddReports : Migration
    {
        /// <summary>ORA-00955: name is already used by an existing object.</summary>
        private const int NameAlreadyUsed = -955;

        /// <summary>ORA-00942: table or view does not exist. Already gone is a no-op, not a failure.</summary>
        private const int TableMissing = -942;

        /// <summary>ORA-01430: column being added already exists in table.</summary>
        private const int ColumnAlreadyExists = -1430;

        /// <summary>ORA-02275: such a referential constraint already exists in the table.</summary>
        private const int ConstraintAlreadyExists = -2275;

        /// <summary>
        /// Runs one DDL statement, swallowing only <paramref name="ignoredSqlCode"/> so a re-run
        /// after a part-applied migration succeeds while every other Oracle error still raises.
        /// The statement is embedded in a PL/SQL string literal, so a single quote inside it
        /// would need doubling. None of the DDL here contains one.
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

        private static void CreateTable(MigrationBuilder migrationBuilder, string ddl) =>
            Idempotent(migrationBuilder, ddl, NameAlreadyUsed);

        private static void CreateIndex(MigrationBuilder migrationBuilder, string ddl) =>
            Idempotent(migrationBuilder, ddl, NameAlreadyUsed);

        private static void DropTable(MigrationBuilder migrationBuilder, string table) =>
            Idempotent(migrationBuilder, $@"DROP TABLE ""{table}"" CASCADE CONSTRAINTS", TableMissing);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateTable(migrationBuilder, @"CREATE TABLE ""Reports"" (
                ""Id"" RAW(16) NOT NULL,
                ""Name"" NVARCHAR2(200) NOT NULL,
                ""Description"" NVARCHAR2(1000),
                ""IsEnabled"" NUMBER(5) NOT NULL,
                ""CreatedByUserId"" RAW(16) NOT NULL,
                ""QueryGroupId"" RAW(16),
                ""TemplateDocx"" BLOB,
                ""TemplateFileName"" NVARCHAR2(255),
                ""AllowedExportFormats"" NVARCHAR2(100),
                ""TimeoutSeconds"" NUMBER(10) DEFAULT 120 NOT NULL,
                ""MaxDetailRows"" NUMBER(10) DEFAULT 100 NOT NULL,
                ""MaxTotalRows"" NUMBER(10) NOT NULL,
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_Reports"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_Reports_QueryGroups_QueryGroupId"" FOREIGN KEY (""QueryGroupId"")
                    REFERENCES ""QueryGroups"" (""Id"") ON DELETE SET NULL)");

            // A database that already created "Reports" before this column existed skips the
            // CREATE above on ORA-00955, so the column has to be added separately as well.
            // Both statements are idempotent, so whichever one is a no-op simply does nothing.
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""Reports"" ADD ""QueryGroupId"" RAW(16)", ColumnAlreadyExists);
            Idempotent(migrationBuilder,
                @"ALTER TABLE ""Reports"" ADD CONSTRAINT ""FK_Reports_QueryGroups_QueryGroupId"" " +
                @"FOREIGN KEY (""QueryGroupId"") REFERENCES ""QueryGroups"" (""Id"") ON DELETE SET NULL",
                ConstraintAlreadyExists);

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportDatasets"" (
                ""Id"" RAW(16) NOT NULL,
                ""ReportId"" RAW(16) NOT NULL,
                ""DatasetKey"" NVARCHAR2(100) NOT NULL,
                ""DisplayName"" NVARCHAR2(200) NOT NULL,
                ""SourceType"" NUMBER(10) NOT NULL,
                ""SortOrder"" NUMBER(10) NOT NULL,
                ""IsVisibleInViewer"" NUMBER(5) NOT NULL,
                ""DynamicQueryId"" RAW(16),
                ""LeftDatasetId"" RAW(16),
                ""RightDatasetId"" RAW(16),
                ""JoinType"" NUMBER(10),
                ""LeftColumn"" NVARCHAR2(128),
                ""RightColumn"" NVARCHAR2(128),
                ""ColumnSelectionJson"" CLOB,
                ""ParentDatasetId"" RAW(16),
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_ReportDatasets"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportDatasets_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ReportDatasets_DynamicQueries_DynamicQueryId"" FOREIGN KEY (""DynamicQueryId"")
                    REFERENCES ""DynamicQueries"" (""Id""))");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportParameters"" (
                ""Id"" RAW(16) NOT NULL,
                ""ReportId"" RAW(16) NOT NULL,
                ""Name"" NVARCHAR2(100) NOT NULL,
                ""DisplayName"" NVARCHAR2(200) NOT NULL,
                ""ParameterType"" NUMBER(10) NOT NULL,
                ""IsRequired"" NUMBER(5) NOT NULL,
                ""DefaultValue"" NVARCHAR2(500),
                ""SortOrder"" NUMBER(10) NOT NULL,
                ""AllowMultiple"" NUMBER(5) NOT NULL,
                ""DropdownSourceType"" NUMBER(10),
                ""DropdownStaticValues"" CLOB,
                ""DropdownQueryId"" RAW(16),
                ""DropdownQueryValueColumn"" NVARCHAR2(100),
                ""DropdownQueryLabelColumn"" NVARCHAR2(100),
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_ReportParameters"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportParameters_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE)");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportParameterMaps"" (
                ""Id"" RAW(16) NOT NULL,
                ""ReportDatasetId"" RAW(16) NOT NULL,
                ""TargetParameterName"" NVARCHAR2(128) NOT NULL,
                ""SourceKind"" NUMBER(10) NOT NULL,
                ""ReportParameterId"" RAW(16),
                ""ConstantValue"" NVARCHAR2(2000),
                ""ParentColumn"" NVARCHAR2(128),
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_ReportParameterMaps"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportParameterMaps_ReportDatasets_ReportDatasetId"" FOREIGN KEY (""ReportDatasetId"")
                    REFERENCES ""ReportDatasets"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ReportParameterMaps_ReportParameters_ReportParameterId"" FOREIGN KEY (""ReportParameterId"")
                    REFERENCES ""ReportParameters"" (""Id""))");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportRoles"" (
                ""ReportId"" RAW(16) NOT NULL,
                ""RoleId"" RAW(16) NOT NULL,
                CONSTRAINT ""PK_ReportRoles"" PRIMARY KEY (""ReportId"", ""RoleId""),
                CONSTRAINT ""FK_ReportRoles_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ReportRoles_Roles_RoleId"" FOREIGN KEY (""RoleId"")
                    REFERENCES ""Roles"" (""Id"") ON DELETE CASCADE)");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportUserGroups"" (
                ""ReportId"" RAW(16) NOT NULL,
                ""UserGroupId"" RAW(16) NOT NULL,
                CONSTRAINT ""PK_ReportUserGroups"" PRIMARY KEY (""ReportId"", ""UserGroupId""),
                CONSTRAINT ""FK_ReportUserGroups_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ReportUserGroups_UserGroups_UserGroupId"" FOREIGN KEY (""UserGroupId"")
                    REFERENCES ""UserGroups"" (""Id"") ON DELETE CASCADE)");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportUsers"" (
                ""ReportId"" RAW(16) NOT NULL,
                ""UserId"" RAW(16) NOT NULL,
                CONSTRAINT ""PK_ReportUsers"" PRIMARY KEY (""ReportId"", ""UserId""),
                CONSTRAINT ""FK_ReportUsers_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ReportUsers_Users_UserId"" FOREIGN KEY (""UserId"")
                    REFERENCES ""Users"" (""Id"") ON DELETE CASCADE)");

            CreateTable(migrationBuilder, @"CREATE TABLE ""ReportRuns"" (
                ""Id"" RAW(16) NOT NULL,
                ""ReportId"" RAW(16) NOT NULL,
                ""UserId"" RAW(16) NOT NULL,
                ""ParametersJson"" CLOB,
                ""StartedAt"" TIMESTAMP(7) NOT NULL,
                ""DurationMs"" NUMBER(19) NOT NULL,
                ""IsSuccess"" NUMBER(5) NOT NULL,
                ""ErrorMessage"" NVARCHAR2(2000),
                ""DatasetResultsJson"" CLOB,
                ""CreatedAt"" TIMESTAMP(7) NOT NULL,
                ""UpdatedAt"" TIMESTAMP(7),
                CONSTRAINT ""PK_ReportRuns"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportRuns_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                    REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE)");

            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_Reports_Name"" ON ""Reports"" (""Name"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_Reports_QueryGroupId"" ON ""Reports"" (""QueryGroupId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportDatasets_DynamicQueryId"" ON ""ReportDatasets"" (""DynamicQueryId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportDatasets_ParentDatasetId"" ON ""ReportDatasets"" (""ParentDatasetId"")");

            // The Word template addresses datasets by key, so a duplicate key within one report
            // would make its {{RESULTS:key}} marker ambiguous. Enforced here, not only in the validator.
            CreateIndex(migrationBuilder, @"CREATE UNIQUE INDEX ""IX_ReportDatasets_ReportId_DatasetKey"" ON ""ReportDatasets"" (""ReportId"", ""DatasetKey"")");

            // Two maps feeding one target parameter would make the bound value depend on load order.
            CreateIndex(migrationBuilder, @"CREATE UNIQUE INDEX ""IX_ReportParameterMaps_Dataset_Target"" ON ""ReportParameterMaps"" (""ReportDatasetId"", ""TargetParameterName"")");

            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportParameterMaps_ReportParameterId"" ON ""ReportParameterMaps"" (""ReportParameterId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportParameters_ReportId"" ON ""ReportParameters"" (""ReportId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportParameters_DropdownQueryId"" ON ""ReportParameters"" (""DropdownQueryId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportRoles_RoleId"" ON ""ReportRoles"" (""RoleId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportUserGroups_UserGroupId"" ON ""ReportUserGroups"" (""UserGroupId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportUsers_UserId"" ON ""ReportUsers"" (""UserId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportRuns_ReportId"" ON ""ReportRuns"" (""ReportId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportRuns_UserId"" ON ""ReportRuns"" (""UserId"")");
            CreateIndex(migrationBuilder, @"CREATE INDEX ""IX_ReportRuns_StartedAt"" ON ""ReportRuns"" (""StartedAt"")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse dependency order; CASCADE CONSTRAINTS takes the foreign keys with them.
            DropTable(migrationBuilder, "ReportParameterMaps");
            DropTable(migrationBuilder, "ReportRuns");
            DropTable(migrationBuilder, "ReportUsers");
            DropTable(migrationBuilder, "ReportUserGroups");
            DropTable(migrationBuilder, "ReportRoles");
            DropTable(migrationBuilder, "ReportDatasets");
            DropTable(migrationBuilder, "ReportParameters");
            DropTable(migrationBuilder, "Reports");
        }
    }
}
