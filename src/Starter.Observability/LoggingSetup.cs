using Elastic.CommonSchema;
using Elastic.CommonSchema.Serilog;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Starter.Observability;

/// <summary>
/// One call per application: <c>builder.AddStarterLogging("api")</c>.
///
/// The normal <see cref="Microsoft.Extensions.Logging.ILogger"/> stays the API used in code and the console output is
/// unchanged. Serilog is added as an extra logging provider that stores selected events as Elastic Common Schema (ECS)
/// JSON - in daily files and/or directly in Elasticsearch. Both destinations receive identical documents, so moving
/// from files to Kibana is a configuration change only.
/// </summary>
public static class LoggingSetup
{
    public static WebApplicationBuilder AddStarterLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var section = builder.Configuration.GetSection(ActivityLoggingOptions.SectionName);
        var options = section.Get<ActivityLoggingOptions>() ?? new ActivityLoggingOptions();
        var filter = new CategoryFilter(options);

        // Categories and the master switch are re-read whenever the configuration changes (file edit, App Service setting).
        ChangeToken.OnChange(section.GetReloadToken, () => filter.Update(section.Get<ActivityLoggingOptions>() ?? new ActivityLoggingOptions()));

        if (builder.Environment.IsDevelopment())
        {
            // Problems of the log sinks themselves (e.g. Elasticsearch not reachable) are printed to the console.
            SelfLog.Enable(message => Console.Error.WriteLine($"[logging] {message}"));
        }

        var ecs = EcsFormatting(serviceName);
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose() // levels are already filtered by the "Logging" section of appsettings
            .Enrich.FromLogContext()
            .Filter.ByIncludingOnly(filter.ShouldStore);

        if (options.Destination is LogDestination.File or LogDestination.Both)
        {
            var path = Path.Combine(options.File.Path, serviceName, serviceName + "-.json");
            configuration.WriteTo.File(
                new EcsTextFormatter<LogEventEcsDocument>(ecs),
                path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, options.RetentionDays),
                shared: true); // the migration step and the app may write at the same time
        }

        if (options.Destination is LogDestination.Elasticsearch or LogDestination.Both)
        {
            if (string.IsNullOrWhiteSpace(options.Elasticsearch.Url))
            {
                throw new InvalidOperationException("ActivityLogging:Elasticsearch:Url must be set when the destination includes Elasticsearch.");
            }

            configuration.WriteTo.Elasticsearch(
                [new Uri(options.Elasticsearch.Url)],
                sink =>
                {
                    // logs-starter.<service>-<namespace>, e.g. logs-starter.api-development.
                    sink.DataStream = new DataStreamName("logs", "starter." + serviceName, options.Elasticsearch.Namespace);
                    sink.TextFormatting = ecs;
                    // The index template (mappings, 7-day retention) is created by deploy/elastic, not by the app.
                    sink.BootstrapMethod = BootstrapMethod.None;
                },
                transport =>
                {
                    if (!string.IsNullOrWhiteSpace(options.Elasticsearch.ApiKey))
                    {
                        transport.Authentication(new ApiKey(options.Elasticsearch.ApiKey));
                    }
                });
        }

        // Events are buffered and written in the background; a slow or unavailable Elasticsearch never blocks a request.
        builder.Logging.AddSerilog(configuration.CreateLogger(), dispose: true);
        builder.Services.AddSingleton(filter);
        builder.Services.Configure<ActivityLoggingOptions>(section);
        return builder;
    }

    private static EcsTextFormatterConfiguration<LogEventEcsDocument> EcsFormatting(string serviceName) => new()
    {
        IncludeHost = false,
        IncludeProcess = false,
        // Otherwise user.name would be the operating system account running the app, not the signed-in user.
        IncludeUser = false,
        // Turns our structured properties into first-class ECS fields that Kibana understands.
        MapCustom = (document, logEvent) =>
        {
            document.Service ??= new Service();
            document.Service.Name = serviceName;

            if (Text(logEvent, LogProperties.Action) is { } action)
            {
                document.Event ??= new Event();
                document.Event.Action = action;
                document.Event.Outcome = Text(logEvent, LogProperties.Outcome);
            }

            if (Text(logEvent, LogProperties.UserId) is { } userId)
            {
                document.User ??= new User();
                document.User.Id = userId;
            }

            return document;
        },
    };

    private static string? Text(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: { } scalar }
            ? scalar.ToString()
            : null;
}

/// <summary>Decides which events are stored, from the current <see cref="ActivityLoggingOptions"/>.</summary>
public sealed class CategoryFilter(ActivityLoggingOptions initial)
{
    private volatile ActivityLoggingOptions options = initial;

    public void Update(ActivityLoggingOptions updated) => options = updated;

    public bool ShouldStore(LogEvent logEvent)
    {
        var current = options;
        if (!current.Enabled)
        {
            return false;
        }

        var categories = current.Categories;

        // Errors always count as errors, whatever category they also belong to.
        if (logEvent.Level >= LogEventLevel.Error && categories.Errors)
        {
            return true;
        }

        if (logEvent.Properties.TryGetValue(LogProperties.Category, out var value) && value is ScalarValue { Value: string category })
        {
            return category switch
            {
                LogCategories.Security => categories.Security,
                LogCategories.Api => categories.Api,
                LogCategories.Frontend => categories.Frontend,
                _ => false,
            };
        }

        // Uncategorised framework and application messages: only warnings and errors are worth storing.
        return logEvent.Level >= LogEventLevel.Warning && categories.Errors;
    }
}
