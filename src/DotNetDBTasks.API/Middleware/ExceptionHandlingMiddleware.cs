using System.Net;
using System.Text.Json;
using DotNetDBTasks.Application.Common.Exceptions;
using DotNetDBTasks.Domain.Exceptions;

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

            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse { Message = domainEx.Message }),

            OperationCanceledException => (
                HttpStatusCode.RequestTimeout,
                new ErrorResponse { Message = "The operation was cancelled or timed out." }),

            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse { Message = "An unexpected error occurred." })
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
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
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public IDictionary<string, string[]>? Errors { get; set; }
}
