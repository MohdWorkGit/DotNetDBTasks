-- DotNetDBTasks Database Schema (Oracle)
-- This schema is applied automatically via EF Core migrations on startup.
-- This file is provided for reference and manual setup scenarios.

CREATE TABLE "Users" (
    "Id"                     RAW(16)        NOT NULL,
    "Username"               NVARCHAR2(100) NOT NULL,
    "Email"                  NVARCHAR2(256) NOT NULL,
    "PasswordHash"           NVARCHAR2(512) NOT NULL,
    "FirstName"              NVARCHAR2(100) NOT NULL,
    "LastName"               NVARCHAR2(100) NOT NULL,
    "IsActive"               NUMBER(1)      DEFAULT 1 NOT NULL,
    "RefreshToken"           NVARCHAR2(512) NULL,
    "RefreshTokenExpiryTime" TIMESTAMP(7)   NULL,
    "CreatedAt"              TIMESTAMP(7)   NOT NULL,
    "UpdatedAt"              TIMESTAMP(7)   NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "UQ_Users_Username" UNIQUE ("Username"),
    CONSTRAINT "UQ_Users_Email"    UNIQUE ("Email")
);

CREATE TABLE "Roles" (
    "Id"          RAW(16)        NOT NULL,
    "Name"        NVARCHAR2(50)  NOT NULL,
    "Description" NVARCHAR2(500) NULL,
    "CreatedAt"   TIMESTAMP(7)   NOT NULL,
    "UpdatedAt"   TIMESTAMP(7)   NULL,
    CONSTRAINT "PK_Roles" PRIMARY KEY ("Id"),
    CONSTRAINT "UQ_Roles_Name" UNIQUE ("Name")
);

CREATE TABLE "UserRoles" (
    "UserId" RAW(16) NOT NULL,
    "RoleId" RAW(16) NOT NULL,
    CONSTRAINT "PK_UserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_UserRoles_Users" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_UserRoles_Roles" FOREIGN KEY ("RoleId") REFERENCES "Roles"("Id") ON DELETE CASCADE
);

CREATE TABLE "DynamicQueries" (
    "Id"              RAW(16)         NOT NULL,
    "Name"            NVARCHAR2(200)  NOT NULL,
    "Description"     NVARCHAR2(1000) NOT NULL,
    "SqlQuery"        CLOB            NOT NULL,
    "IsEnabled"       NUMBER(1)       DEFAULT 1 NOT NULL,
    "TimeoutSeconds"  NUMBER(10)      DEFAULT 30 NOT NULL,
    "CreatedByUserId" RAW(16)         NOT NULL,
    "CreatedAt"       TIMESTAMP(7)    NOT NULL,
    "UpdatedAt"       TIMESTAMP(7)    NULL,
    CONSTRAINT "PK_DynamicQueries" PRIMARY KEY ("Id")
);

CREATE TABLE "QueryParameters" (
    "Id"             RAW(16)        NOT NULL,
    "DynamicQueryId" RAW(16)        NOT NULL,
    "Name"           NVARCHAR2(100) NOT NULL,
    "DisplayName"    NVARCHAR2(200) NOT NULL,
    "ParameterType"  NUMBER(10)     NOT NULL,
    "IsRequired"     NUMBER(1)      NOT NULL,
    "DefaultValue"   NVARCHAR2(500) NULL,
    "SortOrder"      NUMBER(10)     DEFAULT 0 NOT NULL,
    "CreatedAt"      TIMESTAMP(7)   NOT NULL,
    "UpdatedAt"      TIMESTAMP(7)   NULL,
    CONSTRAINT "PK_QueryParameters" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_QueryParameters_DynamicQueries" FOREIGN KEY ("DynamicQueryId") REFERENCES "DynamicQueries"("Id") ON DELETE CASCADE
);

CREATE TABLE "DynamicQueryRoles" (
    "DynamicQueryId" RAW(16) NOT NULL,
    "RoleId"         RAW(16) NOT NULL,
    CONSTRAINT "PK_DynamicQueryRoles" PRIMARY KEY ("DynamicQueryId", "RoleId"),
    CONSTRAINT "FK_DynamicQueryRoles_DynamicQueries" FOREIGN KEY ("DynamicQueryId") REFERENCES "DynamicQueries"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DynamicQueryRoles_Roles"          FOREIGN KEY ("RoleId")         REFERENCES "Roles"("Id")          ON DELETE CASCADE
);

CREATE TABLE "DynamicQueryDepartments" (
    "DynamicQueryId" RAW(16)        NOT NULL,
    "Department"     NVARCHAR2(200) NOT NULL,
    CONSTRAINT "PK_DynamicQueryDepartments" PRIMARY KEY ("DynamicQueryId", "Department"),
    CONSTRAINT "FK_DynamicQueryDepartments_DynamicQueries" FOREIGN KEY ("DynamicQueryId") REFERENCES "DynamicQueries"("Id") ON DELETE CASCADE
);

CREATE TABLE "DynamicQueryUsers" (
    "DynamicQueryId" RAW(16) NOT NULL,
    "UserId"         RAW(16) NOT NULL,
    CONSTRAINT "PK_DynamicQueryUsers" PRIMARY KEY ("DynamicQueryId", "UserId"),
    CONSTRAINT "FK_DynamicQueryUsers_DynamicQueries" FOREIGN KEY ("DynamicQueryId") REFERENCES "DynamicQueries"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DynamicQueryUsers_Users"          FOREIGN KEY ("UserId")         REFERENCES "Users"("Id")          ON DELETE CASCADE
);

CREATE INDEX "IX_DynamicQueryDepartments_Department" ON "DynamicQueryDepartments"("Department");
CREATE INDEX "IX_DynamicQueryUsers_UserId"           ON "DynamicQueryUsers"("UserId");

CREATE TABLE "QueryExecutionLogs" (
    "Id"                  RAW(16)         NOT NULL,
    "DynamicQueryId"      RAW(16)         NOT NULL,
    "UserId"              RAW(16)         NOT NULL,
    "ParametersJson"      CLOB            NOT NULL,
    "ExecutedAt"          TIMESTAMP(7)    NOT NULL,
    "ExecutionDurationMs" NUMBER(19)      NOT NULL,
    "RowsReturned"        NUMBER(10)      NOT NULL,
    "IsSuccess"           NUMBER(1)       NOT NULL,
    "ErrorMessage"        NVARCHAR2(2000) NULL,
    "CreatedAt"           TIMESTAMP(7)    NOT NULL,
    "UpdatedAt"           TIMESTAMP(7)    NULL,
    CONSTRAINT "PK_QueryExecutionLogs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_QueryExecutionLogs_DynamicQueries" FOREIGN KEY ("DynamicQueryId") REFERENCES "DynamicQueries"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_QueryExecutionLogs_Users"          FOREIGN KEY ("UserId")         REFERENCES "Users"("Id")
);

CREATE INDEX "IX_QueryExecutionLogs_ExecutedAt" ON "QueryExecutionLogs"("ExecutedAt");
CREATE INDEX "IX_QueryExecutionLogs_UserId"     ON "QueryExecutionLogs"("UserId");
