using System.Data.Common;
using System.Net;
using System.Text.Json;
using DotNetDBTasks.Application.Common.Exceptions;
using DotNetDBTasks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DotNetDBTasks.API.Middleware;

/// <summary>
/// Global exception handling middleware that catches unhandled exceptions
/// and returns consistent error responses.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, response) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Message = "Validation failed.",
                    Errors = validationEx.Errors
                }),

            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                new ErrorResponse { Message = notFoundEx.Message }),

            ForbiddenAccessException forbiddenEx => (
                HttpStatusCode.Forbidden,
                new ErrorResponse { Message = forbiddenEx.Message }),

            UnauthorizedAccessException unauthorizedEx => (
                HttpStatusCode.Unauthorized,
                new ErrorResponse { Message = unauthorizedEx.Message }),

            // Before DomainException: timeouts are DomainExceptions but map to 408, not 400.
            QueryTimeoutException timeoutEx => (
                HttpStatusCode.RequestTimeout,
                new ErrorResponse { Message = timeoutEx.Message }),

            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse { Message = domainEx.Message }),

            OperationCanceledException => (
                HttpStatusCode.RequestTimeout,
                new ErrorResponse { Message = "The operation was cancelled." }),

            ExternalServiceException externalEx => (
                HttpStatusCode.BadGateway,
                new ErrorResponse { Message = externalEx.Message }),

            // EF wraps the provider's error, so an unwrapped DbUpdateException would fall
            // through to the catch-all and surface as "An unexpected error occurred" — which
            // is what a unique-constraint violation on save used to look like.
            DbUpdateException updateEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse { Message = DescribeSaveFailure(updateEx) }),

            DbException dbEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse { Message = $"Database error: {dbEx.Message}" }),

            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse { Message = "An unexpected error occurred." })
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
        }
        else if (exception is DbUpdateException or ExternalServiceException)
        {
            // Reported to the caller as a 4xx/502, but still worth the stack trace: these are
            // usually a real constraint problem or a dependency that is down.
            _logger.LogError(exception, "{ExceptionType}: {Message}",
                exception.GetType().Name, exception.Message);
        }
        else if (exception is DbException)
        {
            _logger.LogError(exception, "Database error: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception: {ExceptionType} - {Message}",
                exception.GetType().Name, exception.Message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
    }

    /// <summary>
    /// Turns a failed SaveChanges into something a person can act on. The useful detail is in
    /// the provider's inner exception, not EF's generic "An error occurred while saving".
    /// </summary>
    private static string DescribeSaveFailure(DbUpdateException exception)
    {
        var dbMessage = (exception.InnerException as DbException)?.Message
            ?? exception.InnerException?.Message;

        if (string.IsNullOrWhiteSpace(dbMessage))
            return "The change could not be saved.";

        // ORA-00001 is Oracle's unique-constraint violation. The raw text names the index
        // (e.g. "IX_Users_Email"), which tells the caller which field actually clashed.
        if (dbMessage.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase)
            || dbMessage.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || dbMessage.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return $"That value is already used by another record: {dbMessage}";
        }

        // ORA-01400: a required column got NULL — usually a blank string on a NOT NULL column,
        // since Oracle stores "" as NULL.
        if (dbMessage.Contains("ORA-01400", StringComparison.OrdinalIgnoreCase))
            return $"A required field was left empty: {dbMessage}";

        return $"The change could not be saved: {dbMessage}";
    }
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public IDictionary<string, string[]>? Errors { get; set; }
}
