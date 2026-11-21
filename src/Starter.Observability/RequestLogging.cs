using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Starter.Observability;

/// <summary>Middleware for server applications (Starter.Api, Starter.Identity).</summary>
public static class RequestLogging
{
    private const string ValidationErrorsItem = "Starter.ValidationErrors";
    private const string ProblemTitleItem = "Starter.ProblemTitle";

    /// <summary>
    /// Adds the signed-in user's id (<c>sub</c>) to every log entry written while the request is handled.
    /// Place it right after <c>UseAuthentication()</c>.
    /// </summary>
    public static IApplicationBuilder UseUserLogScope(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Starter.Request");
        return app.Use(async (context, next) =>
        {
            var userId = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using (userId is null ? null : logger.BeginScope(new Dictionary<string, object?> { [LogProperties.UserId] = userId }))
            {
                await next(context);
            }
        });
    }

    /// <summary>
    /// Logs every request below <paramref name="pathPrefix"/> as one <c>api.request</c> event (category Api):
    /// method, route, path, status, duration, user, and for errors the problem title and the validation messages
    /// (field names and messages only, never the submitted values). Place it after <c>UseAuthentication()</c>
    /// so that 401, 403 and 429 responses are logged too.
    /// </summary>
    public static IApplicationBuilder UseApiRequestLogging(this IApplicationBuilder app, string pathPrefix = "/api")
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Starter.Api.Requests");
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(pathPrefix))
            {
                await next(context);
                return;
            }

            var started = Stopwatch.GetTimestamp();
            Exception? failure = null;
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                failure = ex; // the exception itself is logged by the exception handler; here only the request summary
                throw;
            }
            finally
            {
                var status = failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
                var details = new Dictionary<string, object?>
                {
                    ["HttpMethod"] = context.Request.Method,
                    ["Route"] = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
                    ["Path"] = context.Request.Path.Value,
                    ["StatusCode"] = status,
                    [LogProperties.DurationMs] = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                };
                if (context.Items.TryGetValue(ProblemTitleItem, out var title))
                {
                    details["ProblemTitle"] = title;
                }

                if (context.Items.TryGetValue(ValidationErrorsItem, out var errors))
                {
                    details["ValidationErrors"] = errors;
                }

                logger.LogActivity(
                    LogCategories.Api,
                    "api.request",
                    status < 400 ? EventOutcome.Success : EventOutcome.Failure,
                    details,
                    level: status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information);
            }
        });
    }

    /// <summary>
    /// Use as <c>AddProblemDetails(o => o.CustomizeProblemDetails = RequestLogging.CaptureProblemDetails)</c>.
    /// Remembers the error description for the request log. The response already carries <c>traceId</c>, which the
    /// web app shows to the user as a reference number.
    /// </summary>
    public static void CaptureProblemDetails(Microsoft.AspNetCore.Http.ProblemDetailsContext context)
    {
        context.HttpContext.Items[ProblemTitleItem] = context.ProblemDetails.Title;
        if (context.ProblemDetails is HttpValidationProblemDetails validation && validation.Errors.Count > 0)
        {
            context.HttpContext.Items[ValidationErrorsItem] = string.Join("; ",
                validation.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
        }
    }
}
