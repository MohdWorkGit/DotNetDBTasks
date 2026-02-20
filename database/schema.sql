-- DotNetDBTasks Database Schema
-- This schema is applied automatically via EF Core migrations on startup.
-- This file is provided for reference and manual setup scenarios.

CREATE TABLE [Users] (
    [Id]                    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Username]              NVARCHAR(100)    NOT NULL,
    [Email]                 NVARCHAR(256)    NOT NULL,
    [PasswordHash]          NVARCHAR(512)    NOT NULL,
    [FirstName]             NVARCHAR(100)    NOT NULL,
    [LastName]              NVARCHAR(100)    NOT NULL,
    [IsActive]              BIT              NOT NULL DEFAULT 1,
    [RefreshToken]          NVARCHAR(512)    NULL,
    [RefreshTokenExpiryTime] DATETIME2       NULL,
    [CreatedAt]             DATETIME2        NOT NULL,
    [UpdatedAt]             DATETIME2        NULL,
    CONSTRAINT [UQ_Users_Username] UNIQUE ([Username]),
    CONSTRAINT [UQ_Users_Email]    UNIQUE ([Email])
);

CREATE TABLE [Roles] (
    [Id]          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Name]        NVARCHAR(50)     NOT NULL,
    [Description] NVARCHAR(500)    NULL,
    [CreatedAt]   DATETIME2        NOT NULL,
    [UpdatedAt]   DATETIME2        NULL,
    CONSTRAINT [UQ_Roles_Name] UNIQUE ([Name])
);

CREATE TABLE [UserRoles] (
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    PRIMARY KEY ([UserId], [RoleId]),
    FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE,
    FOREIGN KEY ([RoleId]) REFERENCES [Roles]([Id]) ON DELETE CASCADE
);

CREATE TABLE [DynamicQueries] (
    [Id]              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Name]            NVARCHAR(200)    NOT NULL,
    [Description]     NVARCHAR(1000)   NOT NULL,
    [SqlQuery]        NVARCHAR(4000)   NOT NULL,
    [IsEnabled]       BIT              NOT NULL DEFAULT 1,
    [TimeoutSeconds]  INT              NOT NULL DEFAULT 30,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt]       DATETIME2        NOT NULL,
    [UpdatedAt]       DATETIME2        NULL
);

CREATE TABLE [QueryParameters] (
    [Id]             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [DynamicQueryId] UNIQUEIDENTIFIER NOT NULL,
    [Name]           NVARCHAR(100)    NOT NULL,
    [DisplayName]    NVARCHAR(200)    NOT NULL,
    [ParameterType]  INT              NOT NULL,
    [IsRequired]     BIT              NOT NULL,
    [DefaultValue]   NVARCHAR(500)    NULL,
    [SortOrder]      INT              NOT NULL DEFAULT 0,
    [CreatedAt]      DATETIME2        NOT NULL,
    [UpdatedAt]      DATETIME2        NULL,
    FOREIGN KEY ([DynamicQueryId]) REFERENCES [DynamicQueries]([Id]) ON DELETE CASCADE
);

CREATE TABLE [DynamicQueryRoles] (
    [DynamicQueryId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId]         UNIQUEIDENTIFIER NOT NULL,
    PRIMARY KEY ([DynamicQueryId], [RoleId]),
    FOREIGN KEY ([DynamicQueryId]) REFERENCES [DynamicQueries]([Id]) ON DELETE CASCADE,
    FOREIGN KEY ([RoleId])         REFERENCES [Roles]([Id])          ON DELETE CASCADE
);

CREATE TABLE [QueryExecutionLogs] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [DynamicQueryId]      UNIQUEIDENTIFIER NOT NULL,
    [UserId]              UNIQUEIDENTIFIER NOT NULL,
    [ParametersJson]      NVARCHAR(4000)   NOT NULL,
    [ExecutedAt]          DATETIME2        NOT NULL,
    [ExecutionDurationMs] BIGINT           NOT NULL,
    [RowsReturned]        INT              NOT NULL,
    [IsSuccess]           BIT              NOT NULL,
    [ErrorMessage]        NVARCHAR(2000)   NULL,
    [CreatedAt]           DATETIME2        NOT NULL,
    [UpdatedAt]           DATETIME2        NULL,
    FOREIGN KEY ([DynamicQueryId]) REFERENCES [DynamicQueries]([Id]) ON DELETE CASCADE,
    FOREIGN KEY ([UserId])         REFERENCES [Users]([Id])          ON DELETE NO ACTION
);

CREATE INDEX [IX_QueryExecutionLogs_ExecutedAt] ON [QueryExecutionLogs]([ExecutedAt]);
CREATE INDEX [IX_QueryExecutionLogs_UserId]     ON [QueryExecutionLogs]([UserId]);
