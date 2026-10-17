using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Shared.Web.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning(ex, "Validation failed");
            var errors = ex.Errors.Select(e => e.ErrorMessage).Distinct().ToArray();
            await WriteAsync(context, HttpStatusCode.BadRequest, string.Join("; ", errors));
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "Domain rule violation");
            await WriteAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            logger.LogWarning(ex, "Bad HTTP request");
            var detail = ex.InnerException?.Message ?? ex.Message;
            await WriteAsync(context, HttpStatusCode.BadRequest, detail);
        }
        catch (Exception ex) when (IsDbUpdateException(ex))
        {
            logger.LogWarning(ex, "Database update failed");
            var inner = ex.InnerException?.Message ?? ex.Message;
            var message =
                inner.Contains("23505", StringComparison.Ordinal)
                || inner.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                    ? "Bạn đã đăng ký cự ly này (hoặc bản ghi bị trùng)."
                : inner.Contains("22001", StringComparison.Ordinal)
                  || inner.Contains("too long", StringComparison.OrdinalIgnoreCase)
                    ? "Một trường vượt quá độ dài cho phép (URL giấy khám / màu áo…). Hãy rút ngắn hoặc upload lại."
                    : environment.IsDevelopment()
                        ? inner
                        : "Không lưu được dữ liệu.";
            await WriteAsync(context, HttpStatusCode.BadRequest, message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            var detail = ex.InnerException?.Message ?? ex.Message;
            var message = environment.IsDevelopment()
                ? detail
                : "An unexpected error occurred.";
            await WriteAsync(context, HttpStatusCode.InternalServerError, message);
        }
    }

    private static bool IsDbUpdateException(Exception ex)
        => ex.GetType().Name.Contains("DbUpdate", StringComparison.Ordinal)
           || (ex.InnerException?.GetType().Name.Contains("Postgres", StringComparison.Ordinal) ?? false);

    private static Task WriteAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;
        var payload = JsonSerializer.Serialize(new { error = message });
        return context.Response.WriteAsync(payload);
    }
}
