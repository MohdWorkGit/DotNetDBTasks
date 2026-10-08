using System.Diagnostics;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Application.Features.QueryExecution;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Runs the SQL an admin is editing — not the saved copy — so it can be tried before saving.
///
/// <para>Nothing is stored and nothing is changed. A SELECT returns its first
/// <see cref="RowLimit"/> rows. An INSERT/UPDATE/DELETE runs inside a transaction that is always
/// rolled back, the same mechanism as a real run's preview, and reports how many rows it would
/// affect (and, for UPDATE/DELETE, which). Anything else — MERGE, DDL, PL/SQL — is refused:
/// Oracle commits DDL implicitly, so there is no transaction that could undo it.</para>
///
/// <para>Not in the query's execution history, which belongs to saved queries; the audit trail
/// records it as <c>queries.test</c>, SQL included, because a test reads real data.</para>
/// </summary>
public class TestDynamicQueryCommand : IRequest<QueryTestResultDto>, IQuerySqlFields
{
    /// <summary>The most rows a test returns. A test is for checking shape, not for reading.</summary>
    public const int RowLimit = 100;

    public string SqlQuery { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public Guid? DatabaseUserId { get; set; }

    /// <summary>The parameter definitions as currently edited.</summary>
    public List<QueryParameterDto> Parameters { get; set; } = new();

    /// <summary>The values to test with, in the same wire format as a real run.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

public class QueryTestResultDto
{
    public QueryType QueryType { get; set; }

    /// <summary>SELECT: the columns and up to <see cref="TestDynamicQueryCommand.RowLimit"/> rows.</summary>
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>True when the result had more rows than were returned.</summary>
    public bool IsLimitReached { get; set; }

    public int RowLimit { get; set; } = TestDynamicQueryCommand.RowLimit;

    /// <summary>INSERT/UPDATE/DELETE: the rows the statement would have affected. Rolled back.</summary>
    public int AffectedRows { get; set; }

    /// <summary>UPDATE/DELETE: the rows the WHERE clause matches, when the statement could be read.</summary>
    public List<string> AffectedRowColumns { get; set; } = new();
    public List<Dictionary<string, object?>> AffectedRowValues { get; set; } = new();

    public long DurationMs { get; set; }
}

public class TestDynamicQueryValidator : AbstractValidator<TestDynamicQueryCommand>
{
    public TestDynamicQueryValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        // The same SQL, timeout and parameter rules as a save, so a test never runs SQL that
        // could not then be saved.
        Include(new QuerySqlFieldsValidator(settings, messages));
    }
}

public class TestDynamicQueryCommandHandler : IRequestHandler<TestDynamicQueryCommand, QueryTestResultDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IAppLocalizer _messages;

    public TestDynamicQueryCommandHandler(
        IUnitOfWork unitOfWork,
        IQueryExecutor queryExecutor,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        IDatabaseConnectionFactory connectionFactory,
        IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _queryExecutor = queryExecutor;
        _currentUser = currentUser;
        _encryption = encryption;
        _connectionFactory = connectionFactory;
        _messages = messages;
    }

    public async Task<QueryTestResultDto> Handle(TestDynamicQueryCommand request, CancellationToken cancellationToken)
    {
        var queryType = QueryTypeClassifier.FromSql(request.SqlQuery);
        if (queryType == QueryType.Other)
            throw new DomainException(_messages[MessageKeys.QueryTestUnsupported]);

        // The same connection a saved query would use, under the same check that the admin may
        // use that database user at all.
        string? connectionString = null;
        DatabaseServerType? serverType = null;
        if (request.DatabaseUserId.HasValue)
        {
            var dbUser = await QueryAccess.EnsureCanUseDatabaseUserAsync(
                request.DatabaseUserId.Value, _unitOfWork, _currentUser, cancellationToken);
            connectionString = _connectionFactory.BuildConnectionString(dbUser, _encryption.Decrypt(dbUser.EncryptedPassword));
            serverType = dbUser.ServerType;
        }

        // Bound by the same code as a real run, from the definitions as edited.
        var definitions = request.Parameters.Select(p => new QueryParameter
        {
            Name = p.Name,
            DisplayName = p.DisplayName,
            ParameterType = p.ParameterType,
            IsRequired = p.IsRequired,
            AllowMultiple = p.AllowMultiple,
            DropdownSourceType = p.DropdownSourceType,
            DropdownStaticValues = p.DropdownStaticValues
        });
        var typed = QueryParameterBinder.Bind(definitions, request.Values);

        var result = new QueryTestResultDto { QueryType = queryType };
        var sw = Stopwatch.StartNew();

        if (queryType == QueryType.Select)
        {
            var rows = await _queryExecutor.ExecuteAsync(
                request.SqlQuery, typed, request.TimeoutSeconds, connectionString, serverType,
                TestDynamicQueryCommand.RowLimit, cancellationToken);
            result.Columns = rows.Columns;
            result.Rows = rows.Rows;
            result.IsLimitReached = rows.IsLimitReached;
        }
        else
        {
            // Always rolled back — ExecutePreviewAsync never commits.
            var preview = await _queryExecutor.ExecutePreviewAsync(
                request.SqlQuery, typed, request.TimeoutSeconds, connectionString, serverType, cancellationToken);
            result.AffectedRows = preview.AffectedRows;

            var affected = await AffectedRowsPreview.FetchAsync(
                _queryExecutor, request.SqlQuery, typed, request.TimeoutSeconds, connectionString, serverType,
                cancellationToken);
            if (affected is not null)
            {
                result.AffectedRowColumns = affected.Columns;
                result.AffectedRowValues = affected.Rows.Take(TestDynamicQueryCommand.RowLimit).ToList();
                result.IsLimitReached = affected.Rows.Count > TestDynamicQueryCommand.RowLimit || affected.IsLimitReached;
            }
        }

        result.DurationMs = sw.ElapsedMilliseconds;
        return result;
    }
}
