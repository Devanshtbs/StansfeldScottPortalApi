using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StansfeldScott.Portal.Common.Exceptions;
using StansfeldScott.Portal.Common.Models;

namespace StansfeldScott.Portal.Common.Middleware;


public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
            _logger.LogError(ex, "Unhandled exception processing {Path}", context.Request.Path);
            var isDevelopment = string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Development",
                StringComparison.OrdinalIgnoreCase);

            var (statusCode, response) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, ApiResponse<object>.Fail(ex.Message)),
                ValidationException ve => (HttpStatusCode.BadRequest, ApiResponse<object>.Fail(ve.Message, ve.Errors)),
                CrmIntegrationException crm => (
                    HttpStatusCode.BadGateway,
                    ApiResponse<object>.Fail(isDevelopment && crm.InnerException is not null
                        ? $"{crm.Message} Details: {crm.InnerException.Message}; trace ID: {context.TraceIdentifier}."
                        : crm.Message)),
                _ => (
                    HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Fail(isDevelopment
                        ? $"Unexpected {ex.GetType().Name}. See backend logs; trace ID: {context.TraceIdentifier}."
                        : "An unexpected error occurred."))
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
