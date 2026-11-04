using CompanyEnrichment.Application;
using CompanyEnrichment.Infrastructure.Statistics;
using CompanyEnrichment.Observability;
using Cvr.Client.Models;
using cvrapi.Infrastructure.Cvr;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using StatBank.Client.Models;

namespace CompanyEnrichment
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddOptions<CvrOptions>().Bind(builder.Configuration.GetSection(CvrOptions.SectionName));
            builder.Services.AddOptions<StatisticsOptions>().Bind(builder.Configuration.GetSection(StatisticsOptions.SectionName));
            builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<StatisticsOptions>>().Value);

            builder.Services.AddSingleton<DependencyMetrics>();
            builder.Services.AddScoped<IEnrichmentService, EnrichmentService>();

            builder.Services.AddHttpClient<ICvrClient, CvrClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<CvrOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
            });

            builder.Services.AddHttpClient<IStatisticsClient, StatisticsClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<StatisticsOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
            });

            builder.Services.AddHttpClient("cvr-health");

            // Liveness reports process health only. Readiness checks the CVR dependency but is
            // tagged "external" and intentionally excluded from /health/live so an upstream outage
            // never causes the process itself to be recycled.
            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
                .AddCheck<CvrDependencyHealthCheck>("cvr-dependency", tags: ["external"]);

            var otelResourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName: "CompanyEnrichment", serviceVersion: "1.0.0");

            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(otelResourceBuilder);
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                ConfigureExporters(options, builder.Configuration);
            });

            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddSource(EnrichmentService.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .ConfigureExporters(builder.Configuration))
                .WithMetrics(metrics => metrics
                    .SetResourceBuilder(otelResourceBuilder)
                    .AddMeter(DependencyMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .ConfigureExporters(builder.Configuration));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
                app.MapGet("/", () => Results.Redirect("/scalar"));
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
            });

            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
            });

            app.Run();
        }

        private static void ConfigureExporters(OpenTelemetryLoggerOptions options, IConfiguration configuration)
        {
            if (UseOtlp(configuration))
            {
                options.AddOtlpExporter();
            }
            else
            {
                options.AddConsoleExporter();
            }
        }

        private static bool UseOtlp(IConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
    }

    internal static class OpenTelemetryBuilderExtensions
    {
        public static TracerProviderBuilder ConfigureExporters(this TracerProviderBuilder builder, IConfiguration configuration) =>
            HasOtlpEndpoint(configuration) ? builder.AddOtlpExporter() : builder.AddConsoleExporter();

        public static MeterProviderBuilder ConfigureExporters(this MeterProviderBuilder builder, IConfiguration configuration) =>
            HasOtlpEndpoint(configuration) ? builder.AddOtlpExporter() : builder.AddConsoleExporter();

        private static bool HasOtlpEndpoint(IConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
    }
}
